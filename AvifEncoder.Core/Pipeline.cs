using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;   // 如果使用 System.Text.Json
using System.Text.RegularExpressions;


namespace AvifEncoder
{





    public enum CliPreset { Fast, Balanced, Best, Extreme }




    class ProbeInfo
    {
        public string PixFmt { get; set; } = "yuv420p";
        public bool HasAlpha { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        // 新增色彩元数据字段（可能为 null/unknown）
        public string? ColorPrimaries { get; set; }
        public string? ColorTransfer { get; set; }
        public string? ColorSpace { get; set; }
        public string? ColorRange { get; set; }
    }




    /// <summary>缓存管理器接口</summary>
    public interface ICacheManager
    {
        bool TryGetEncode(string key, out (string file, TimeSpan encodeTime, string commandLine) cached);
        void SetEncode(string key, string cacheFile, TimeSpan encodeTime, string commandLine);
        bool TryGetMetrics(string key, out QualityMetrics? metrics);
        void SetMetrics(string key, QualityMetrics metrics);
        /// <summary>原子更新缓存中的 QualityMetrics，确保线程安全</summary>
        void UpdateMetrics(string key, Action<QualityMetrics> updateAction);
        bool TryGetSSIM(string key, out double ssim);
        void SetSSIM(string key, double ssim);
    }



    public class CacheManager : ICacheManager
    {
        private readonly ConcurrentDictionary<string, (string file, TimeSpan encodeTime, string commandLine)> _encodeCache = new();
        private readonly ConcurrentDictionary<string, QualityMetrics> _metricsCache = new();
        private readonly ConcurrentDictionary<string, double> _ssimCache = new();

        public bool TryGetEncode(string key, out (string file, TimeSpan encodeTime, string commandLine) cached)
            => _encodeCache.TryGetValue(key, out cached);

        public void SetEncode(string key, string cacheFile, TimeSpan encodeTime, string commandLine)
            => _encodeCache[key] = (cacheFile, encodeTime, commandLine);

        public bool TryGetMetrics(string key, out QualityMetrics? metrics)   // 改为 QualityMetrics?
            => _metricsCache.TryGetValue(key, out metrics);

        public void SetMetrics(string key, QualityMetrics metrics)
            => _metricsCache[key] = metrics;

        /// <summary>
        /// 线程安全地更新缓存中的 QualityMetrics 对象。
        /// 若 key 不存在则创建新对象后执行 updateAction。
        /// </summary>
        public void UpdateMetrics(string key, Action<QualityMetrics> updateAction)
        {
            _metricsCache.AddOrUpdate(key,
                _ =>
                {
                    var metrics = new QualityMetrics();
                    updateAction(metrics);
                    return metrics;
                },
                (_, existing) =>
                {
                    updateAction(existing);
                    return existing;
                });
        }

        public bool TryGetSSIM(string key, out double ssim)
            => _ssimCache.TryGetValue(key, out ssim);

        public void SetSSIM(string key, double ssim)
            => _ssimCache[key] = ssim;
    }






    public partial class AvifPipeline : IDisposable
    {
        #region 字段与构造

        private readonly string _inputDir;
        private readonly string _outputDir;
        private readonly PresetConfig _config;
        private readonly int _maxRetries = 2;
        private readonly string _ffmpegPath;
        private readonly string _ffprobePath;


        private const double SSIMMargin = 0.0002;

        private readonly ProgressTracker _progress = new();

        private readonly IProgress<int>? _guiProgress;   // ★ 新增字段，不与 _progress 冲突

        private readonly ConcurrentDictionary<string, Task<double>> _ssimTasks = new();

        private readonly ICacheManager _cache;


        private readonly SemaphoreSlim _ssimConcurrency;
        private readonly SemaphoreSlim _ffmpegSlots;
        private readonly string _instanceId = Guid.NewGuid().ToString("N");
        private ConsoleCancelEventHandler? _cancelKeyHandler;

        private static readonly object _consoleLock = new();
        private CancellationTokenSource? _globalCts;

        private readonly ConcurrentDictionary<string, Task<QualityMetrics?>> _metricsTasks = new();

        private static void SafeWriteLine(string msg) { lock (_consoleLock) Console.WriteLine(msg); }

        private readonly ConcurrentDictionary<string, bool> _srcAlphaCache = new();

        private readonly int _maxFfmpegConcurrency;

        private int _disposed;

        private FileStream? _lockStream;

        private readonly IProcessRunner _processRunner;

        private readonly ILogger _logger;



        private readonly PresetConfig.IFileSystem _fs;   // 改为完整限定名

        // 文件级失败跟踪器（当前未使用，保留以供将来扩展）
        private readonly ConcurrentDictionary<string, FileScopedFailTracker> _failTrackers = new();


        // 记录某文件的某像素格式是否已发生“完全无法写入”的致命错误，用于跳过后续尝试
        // 记录某文件的某像素格式是否已发生“完全无法写入”的致命错误，用于跳过后续尝试
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _fatalFmts = new();
        private readonly ConcurrentDictionary<string, byte> _allocatedOutputs = new();
        private readonly ConcurrentBag<System.Diagnostics.Process> _spawnedProcesses = new();


        private readonly ConcurrentQueue<Task> _advancedMetricTasks = new();
        private readonly ConcurrentQueue<Task> _xpsnrTasks = new();
        private readonly SemaphoreSlim _advancedMetricSemaphore;

        // 无损验证报告相关
        private readonly object _failedCsvLock = new();
        private string _failedCsvPath = "";
        private string _failedVerificationDir = "";

        // CSV 持续写入
        private readonly object _csvLock = new();
        private string _csvPath = "";
        private bool _csvHeaderWritten;

        // Journal 断点续传
        private string _journalPath = "";
        private string _snapshotPath = "";
        private StreamWriter? _journalWriter;
        private readonly object _journalLock = new();
        private int _journalCountSinceSnapshot;
        private DateTime _lastSnapshotTime;








        // ===== 工具：将任意图片转为 PNG（SSIMULACRA2/Butteraugli 需要） =====
        private async Task<string?> ConvertToPngAsync(string inputPath, string tempDir)
        {
            string tempPng = Path.Combine(tempDir, $"_tool_{Guid.NewGuid():N}.png");
            string cleanInput = NormalizePathForExternalTool(inputPath);
            string cleanOutput = NormalizePathForExternalTool(tempPng);
            // ★ 使用已验证可工作的命令：-y -loglevel error -i "输入" -pix_fmt rgb24 -frames:v 1 "输出"
            string args = $"-y -loglevel error -i \"{cleanInput}\" -pix_fmt rgb24 -frames:v 1 \"{cleanOutput}\"";
            var (ok, _) = await RunFfmpegExAsync(_ffmpegPath, args, TimeSpan.FromMinutes(1));
            return ok && _fs.FileExists(tempPng) ? tempPng : null;
        }


        // ===== PNG 尾部清洗 =====
        /// <summary>
        /// 若 PNG 文件 IEND 后有额外字节，则创建清洗后的临时文件并返回其路径；
        /// 否则返回原路径（不修改原文件）。
        /// </summary>
        private async Task<string> SanitizePngIfNeededAsync(string originalPath, string tempDir)
        {
            // 仅处理 .png 文件
            if (!originalPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                return originalPath;

            byte[] bytes = await _fs.ReadAllBytesAsync(originalPath);
            int iendEnd = FindIendEndOffset(bytes);
            if (iendEnd < 0 || iendEnd == bytes.Length)
            {
                // 没找到 IEND 或干净文件，直接返回
                return originalPath;
            }

            // 有尾部垃圾，创建清洗版本
            string cleanFileName = $"_clean_{Guid.NewGuid():N}.png";
            string cleanPath = Path.Combine(tempDir, cleanFileName);
            byte[] cleanBytes = new byte[iendEnd];
            Array.Copy(bytes, cleanBytes, iendEnd);
            await _fs.WriteAllBytesAsync(cleanPath, cleanBytes);
            _logger.LogInfo($"PNG 尾部清洗: {Path.GetFileName(originalPath)} 移除 {bytes.Length - iendEnd} 字节 -> {cleanFileName}");
            return cleanPath;
        }

        /// <summary>
        /// 查找 PNG 文件中标准 IEND 块结束的偏移量（即第一个不属于 PNG 的字节位置）。
        /// 失败返回 -1，干净文件返回文件总长度。
        /// </summary>
        private static int FindIendEndOffset(byte[] bytes)
        {
            // 标准 IEND chunk: 长度 0 (4 bytes) + "IEND" (4 bytes) + CRC (4 bytes)
            int limit = bytes.Length - 12; // 至少需要 8 字节的块 + 最后可能的 CRC

            for (int i = 0; i <= limit; i++)
            {
                if (bytes[i] == 0x49 && bytes[i + 1] == 0x45 && bytes[i + 2] == 0x4E && bytes[i + 3] == 0x44)
                {
                    // 找到 "IEND"，检查前 4 字节是否为 0（块长度必须为 0）
                    if (i >= 4 && bytes[i - 4] == 0 && bytes[i - 3] == 0 && bytes[i - 2] == 0 && bytes[i - 1] == 0)
                    {
                        // IEND 块结束 = 类型起始 + 8（类型 + CRC）
                        return i + 8;
                    }
                }
            }

            // 未找到任何有效 IEND 块
            return -1;
        }


        private async Task ComputeAdvancedMetricsInBackgroundAsync(
            string refPath, string distPath, string outputDir, string cacheKey,
            bool needSsimu2, bool needButter, bool needGmsd,
            CancellationToken cancellationToken, string? inputPath = null)
        {
            await _advancedMetricSemaphore.WaitAsync(cancellationToken);
            try
            {
                string advancedTempDir = Path.Combine(outputDir, $"_advanced_metrics_{Guid.NewGuid():N}");
                try
                {
                    _fs.CreateDirectory(advancedTempDir);
                    string? cleanRef = await SanitizePngIfNeededAsync(refPath, advancedTempDir);
                    bool ownClean = cleanRef != refPath;

                    string? refPng = cleanRef;
                    if (Path.GetExtension(cleanRef).ToLower() != ".png")
                    {
                        try { refPng = await ConvertToPngAsync(cleanRef, advancedTempDir); }
                        catch { refPng = null; }
                    }

                    string? distPng = null;
                    if (needSsimu2 || needButter)
                    {
                        try { distPng = await ConvertToPngAsync(distPath, advancedTempDir); }
                        catch { distPng = null; }
                    }

                    if (needSsimu2 && refPng != null && distPng != null)
                    {
                        try
                        {
                            var s = await ComputeSSIMULACRA2Async(refPng, distPng);
                            if (s.HasValue) UpdateCachedMetrics(cacheKey, m => m.SSIMULACRA2 = s);
                        }
                        catch (Exception ex) { _logger.LogInfo($"SSIMULACRA2 后台异常: {ex.Message}"); }
                    }

                    if (needButter && refPng != null && distPng != null)
                    {
                        try
                        {
                            var (raw, p3) = await ComputeButteraugliAsync(refPng, distPng, advancedTempDir);
                            if (raw.HasValue) UpdateCachedMetrics(cacheKey, m => m.Butteraugli_Raw = raw);
                            if (p3.HasValue) UpdateCachedMetrics(cacheKey, m => m.Butteraugli_3norm = p3);
                        }
                        catch (Exception ex) { _logger.LogInfo($"Butteraugli 后台异常: {ex.Message}"); }
                    }

                    if (needGmsd)
                    {
                        try
                        {
                            var g = await ComputeGMSDAsync(cleanRef, distPath);
                            if (g.HasValue) UpdateCachedMetrics(cacheKey, m => m.GMSD = g);
                        }
                        catch (Exception ex) { _logger.LogInfo($"GMSD 后台异常: {ex.Message}"); }
                    }

                    if (ownClean && _fs.FileExists(cleanRef))
                        try { _fs.DeleteFile(cleanRef); } catch { }
                }
                finally
                {
                    if (_fs.DirectoryExists(advancedTempDir))
                        try { _fs.DeleteDirectory(advancedTempDir, true); } catch { }
                }
            }
            finally
            {
                _advancedMetricSemaphore.Release();
            }
            if (inputPath != null)
                AppendJournal(inputPath, "success");
            _progress.MarkFileProcessed();
            _guiProgress?.Report(Math.Min(100, _progress.ProcessedCount * 100 / Math.Max(1, _progress.TotalFiles)));
        }

        /// <summary> 线程安全地更新缓存中的 QualityMetrics 对象 </summary>
        /// <summary> 线程安全地更新缓存中的 QualityMetrics 对象（使用原子 AddOrUpdate） </summary>
        private void UpdateCachedMetrics(string cacheKey, Action<QualityMetrics> updateAction)
        {
            _cache.UpdateMetrics(cacheKey, updateAction);
        }



        // ===== SSIMULACRA2 =====
        private async Task<double?> ComputeSSIMULACRA2Async(string refPath, string distPath)
        {
            string exe = EncoderUtils.FindExecutable("ssimulacra2") ?? "ssimulacra2";
            string cleanRef = NormalizePathForExternalTool(refPath);
            string cleanDist = NormalizePathForExternalTool(distPath);
            string args = $"\"{cleanRef}\" \"{cleanDist}\"";
            _logger.LogInfo($"?? SSIMULACRA2 调用: {exe} {args}");   // ← 新增
            var (exitCode, stdout, stderr) = await _processRunner.RunAsync(
                exe, args, TimeSpan.FromMinutes(2), _globalCts?.Token ?? default);
            _logger.LogInfo($"?? SSIMULACRA2 返回: exit={exitCode}, stdout={stdout.Trim()}, stderr={stderr.Trim()}"); // ← 新增
            if (exitCode != 0) return null;
            string output = (stdout + stderr).Trim();
            if (double.TryParse(output, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                return val;
            return null;
        }

        // ===== Butteraugli =====
        private async Task<(double? raw, double? p3)> ComputeButteraugliAsync(string refPath, string distPath, string tempDir)
        {
            string exe = EncoderUtils.FindExecutable("butteraugli_main") ?? "butteraugli_main";
            string diffPng = Path.Combine(tempDir, $"_butteraugli_diff_{Guid.NewGuid():N}.png");
            string cleanRef = NormalizePathForExternalTool(refPath);
            string cleanDist = NormalizePathForExternalTool(distPath);
            string cleanDiff = NormalizePathForExternalTool(diffPng);
            string args = $"\"{cleanRef}\" \"{cleanDist}\" --distmap \"{cleanDiff}\"";
            _logger.LogInfo($"?? Butteraugli 调用: {exe} {args}");   // ★ 新增
            var (exitCode, stdout, stderr) = await _processRunner.RunAsync(
                exe, args, TimeSpan.FromMinutes(2), _globalCts?.Token ?? default);
            _logger.LogInfo($"?? Butteraugli 返回: exit={exitCode}, stdout={stdout.Trim()}, stderr={stderr.Trim()}"); // ★ 新增

            if (_fs.FileExists(diffPng)) try { _fs.DeleteFile(diffPng); } catch { }

            if (exitCode != 0) return (null, null);
            string output = stdout + stderr;

            var rawMatch = Regex.Match(output, @"^\s*(\d+\.?\d*)", RegexOptions.Multiline);
            double? raw = null;
            if (rawMatch.Success && double.TryParse(rawMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double r))
                raw = r;

            var p3Match = Regex.Match(output, @"3-norm:\s*(\d+\.?\d*)");
            double? p3 = null;
            if (p3Match.Success && double.TryParse(p3Match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double p))
                p3 = p;

            return (raw, p3);
        }

        // ===== GMSD（自定义实现：仿 C++ 版本，使用 ffmpeg 解码灰度图计算） =====
        private async Task<double?> ComputeGMSDAsync(string refPath, string distPath)
        {
            // 1. 解码两张图到 8 位灰度原始数据
            var refGray = await DecodeGrayRawAsync(refPath);
            if (refGray == null) return null;
            var distGray = await DecodeGrayRawAsync(distPath);
            if (distGray == null) return null;

            // 2. 尺寸必须一致
            if (refGray.Value.w != distGray.Value.w || refGray.Value.h != distGray.Value.h)
                return null;

            // 3. 计算 GMSD
            double score = ComputeGMSD_C(refGray.Value.data, refGray.Value.w, refGray.Value.h,
                                          distGray.Value.data);
            return score >= 0 ? score : null;
        }

        /// <summary> 用 ffmpeg 将任意图片解码为 8 位灰度原始字节数组，并返回宽、高。失败返回 null。 </summary>
        private async Task<(int w, int h, byte[] data)?> DecodeGrayRawAsync(string imagePath)
        {
            string cleanPath = NormalizePathForExternalTool(imagePath);
            string args = $"-loglevel error -hide_banner -i \"{cleanPath}\" -vf format=gray -f rawvideo -pix_fmt gray pipe:1";
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo(_ffmpegPath, args)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                process.Start();

                using var ms = new MemoryStream();
                var copyTask = process.StandardOutput.BaseStream.CopyToAsync(ms);
                var stderrTask = process.StandardError.ReadToEndAsync();

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    _globalCts?.Token ?? CancellationToken.None, timeoutCts.Token);
                await Task.WhenAll(copyTask, stderrTask, process.WaitForExitAsync(linkedCts.Token));

                if (process.ExitCode != 0) return null;

                byte[] rawData = ms.ToArray();

                // 获取图像分辨率
                var (w, h) = await GetResolutionAsync(imagePath);
                if (w <= 0 || h <= 0) return null;
                int expectedSize = w * h;
                if (rawData.Length != expectedSize) return null;

                return (w, h, rawData);
            }
            catch (Exception ex)
            {
                _logger.LogInfo($"DecodeGrayRawAsync 失败: {ex.Message}");
                return null;
            }
        }

        /// <summary> 计算 GMSD（梯度幅值相似度偏差）。C = 0.0026，输出为标准差。失败返回 -1。 </summary>
        private static double ComputeGMSD_C(byte[] refData, int w, int h, byte[] distData)
        {
            if (refData.Length != distData.Length || w < 3 || h < 3)
                return -1;

            const double C = 0.0026;
            double sum = 0.0;
            double sumSq = 0.0;
            int count = 0;

            int GetPix(byte[] data, int x, int y) => data[y * w + x];

            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    double grx = GetPix(refData, x + 1, y) - GetPix(refData, x - 1, y);
                    double gry = GetPix(refData, x, y + 1) - GetPix(refData, x, y - 1);
                    double gdx = GetPix(distData, x + 1, y) - GetPix(distData, x - 1, y);
                    double gdy = GetPix(distData, x, y + 1) - GetPix(distData, x, y - 1);

                    double gmR = Math.Sqrt(grx * grx + gry * gry);
                    double gmD = Math.Sqrt(gdx * gdx + gdy * gdy);

                    double gms = (2.0 * gmR * gmD + C) / (gmR * gmR + gmD * gmD + C);
                    sum += gms;
                    sumSq += gms * gms;
                    count++;
                }
            }

            if (count == 0) return -1;
            double mean = sum / count;
            double variance = (sumSq / count) - (mean * mean);
            return Math.Sqrt(Math.Max(0, variance));   // 标准差
        }







        /// <summary>
        /// 根据图像宽度和最小 tile 宽度限制，计算最大合法的 tile-columns 值（log2 列数）。
        /// 例如：宽度 ≤ 255 → 0；256~511 → 0；512~1023 → 1；1024~2047 → 2；以此类推。
        /// </summary>
        private static int GetMaxLegalTileCols(int imageWidth, int minTileWidth = 256)
        {
            if (imageWidth < minTileWidth)
                return 0;
            int maxTiles = imageWidth / minTileWidth;
            if (maxTiles < 1)
                return 0;
            return (int)Math.Floor(Math.Log2(maxTiles));
        }











        private static string TilePart(int tileCols, bool isTrueLossless)
    => isTrueLossless
        ? "-tile-columns 0 -tile-rows 0"
        : $"-tile-columns {tileCols} -tile-rows 0";

        /// <summary>
        /// 根据输入文件路径与索引生成输出完整路径，并保持子目录结构。
        /// </summary>
        /// <summary>
        /// 根据输入文件路径与索引生成输出完整路径，并保持子目录结构。
        /// ★ 新增同名检测：若文件名已存在，自动追加 _1、_2 … 以避免覆盖。
        /// </summary>
        private string GetOutputPath(string inputFilePath, int index)
        {
            // ★ 同步去除可能的长路径前缀，保证 Path.GetRelativePath 正确工作
            string safeInputDir = NormalizePathForExternalTool(_inputDir);
            string safeInputPath = NormalizePathForExternalTool(inputFilePath);
            string relPath = Path.GetRelativePath(safeInputDir, safeInputPath);
            string? relDir = Path.GetDirectoryName(relPath);
            string fileName = GetOutputFileName(inputFilePath, index);
            string targetDir = string.IsNullOrEmpty(relDir)
                ? _outputDir
                : Path.Combine(_outputDir, relDir);
            _fs.CreateDirectory(targetDir);
            string candidate = Path.Combine(targetDir, fileName);
            switch (_config.FileConflictStrategy)
            {
                case PresetConfig.ConflictStrategy.Overwrite:
                case PresetConfig.ConflictStrategy.Skip:
                    _allocatedOutputs.TryAdd(
                        NormalizePathForExternalTool(candidate).ToLowerInvariant(), 0);
                    return candidate;
                default: // Rename
                    // 自动追加序号以避免同名冲突（内存+磁盘双重检测）
                    string allocatedKey = NormalizePathForExternalTool(candidate).ToLowerInvariant();
                    if (_allocatedOutputs.ContainsKey(allocatedKey) || _fs.FileExists(candidate))
                    {
                        string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
                        string ext = Path.GetExtension(fileName);
                        int counter = 1;
                        do
                        {
                            fileName = $"{nameNoExt}_{counter}{ext}";
                            candidate = Path.Combine(targetDir, fileName);
                            counter++;
                        } while (_fs.FileExists(candidate) ||
                                 _allocatedOutputs.ContainsKey(
                                     NormalizePathForExternalTool(Path.Combine(targetDir, fileName)).ToLowerInvariant()));
                    }
                    // 标记已分配，防止同批次同名覆盖
                    _allocatedOutputs.TryAdd(
                        NormalizePathForExternalTool(candidate).ToLowerInvariant(), 0);
                    return candidate;
            }
        }

        /// <summary> 外部工具（ffmpeg 等）不接受 \\?\ 长路径，需要剥离。正确处理 UNC 路径 </summary>
        private static string NormalizePathForExternalTool(string path)
        {
            if (OperatingSystem.IsWindows() && path.StartsWith(@"\\?\"))
            {
                // \\?\UNC\server\share\path → \\server\share\path
                if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                    return @"\" + path.Substring(7);
                return path.Substring(4);
            }
            return path;
        }

        /// <summary>
        /// 根据图像宽度计算满足 AV1 tile 宽度 ≤ 4096 限制的最小 tile-columns 值（log2 列数）。
        /// 例如：宽度 ≤ 4096 → 0；4097~8192 → 1；8193~16384 → 2；以此类推。
        /// </summary>
        private static int GetMinLegalTileCols(int imageWidth)
        {
            if (imageWidth <= 4096)
                return 0;

            int colsLog2 = 0;
            // 每增加一列，tile 宽度减半，直到满足 ≤ 4096
            while (Math.Ceiling((double)imageWidth / (1 << colsLog2)) > 4096)
                colsLog2++;
            return colsLog2;
        }

























        public AvifPipeline(string inputDir, string outputDir, PresetConfig config,
                    ILogger logger,
                    IProcessRunner? processRunner = null,
                    PresetConfig.IFileSystem? fileSystem = null,   // 改为完整限定名
                    ICacheManager? cacheManager = null,
                    IProgress<int>? progress = null)
        {
            _fs = fileSystem ?? new PresetConfig.RealFileSystem();

            // ★ 启用长路径支持（Windows 下自动添加 \\?\ 前缀）
            _inputDir = EnsureLongPath(inputDir);
            _outputDir = EnsureLongPath(outputDir);
            _fs.CreateDirectory(_outputDir);
            // 防呆：输出目录互斥锁，防止多个进程同时写同一目录
            string lockFile = Path.Combine(_outputDir, ".avifencoder.lock");
            try
            {
                _lockStream = new FileStream(lockFile, FileMode.Create,
                    FileAccess.Write, FileShare.None, 4096,
                    FileOptions.DeleteOnClose);
            }
            catch (IOException)
            {
                throw new IOException(
                    $"输出目录 {outputDir} 已被另一个编码进程占用。" +
                    "请等待其完成或更换输出目录。");
            }

            // 防呆：输入输出同目录时，若存在 .avif 源文件则自动创建输出子目录
            string normalizedInput = NormalizePathForExternalTool(_inputDir);
            string normalizedOutput = NormalizePathForExternalTool(_outputDir);
            if (string.Equals(normalizedInput, normalizedOutput,
                StringComparison.OrdinalIgnoreCase))
            {
                bool hasAvifInput = false;
                try
                {
                    hasAvifInput = _fs.EnumerateFiles(normalizedInput, "*.avif",
                        SearchOption.TopDirectoryOnly).Any();
                }
                catch { }

                if (hasAvifInput)
                {
                    string subDir = Path.Combine(_outputDir, "Avifoutput");
                    _logger?.LogInfo(
                        $"[INFO] 输入输出同目录且存在 .avif 源文件，" +
                        $"输出自动重定向到: {subDir}");
                    SafeWriteLine(
                        $"[INFO] 输入和输出目录相同，为避免覆盖源 .avif 文件，" +
                        $"输出目录自动变更为: {subDir}");
                    _outputDir = EnsureLongPath(subDir);
                }
            }

            _config = config;
            _ffmpegPath = EncoderUtils.FindExecutable("ffmpeg") ?? throw new Exception("ffmpeg 未找到");
            _ffprobePath = EncoderUtils.FindExecutable("ffprobe") ?? throw new Exception("ffprobe 未找到");
            _processRunner = processRunner ?? new RealProcessRunner();
            _logger = logger;
            _cache = cacheManager ?? new CacheManager();

            bool isHardwareEncoder = !Av1EncoderFactory.Get(config.Encoder).SupportsLossless;

            // 防呆：硬件编码器不支持无损模式
            if (config.Lossless && !Av1EncoderFactory.Get(config.Encoder).SupportsLossless)
            {
                throw new ArgumentException(
                    $"编码器 {config.Encoder} 不支持无损模式。" +
                    "请改用 libaom-av1 / libsvtav1 / librav1e 等软件编码器。");
            }

            // 警告：非 libaom 编码器不支持 AOM 高级参数
            if (!Av1EncoderFactory.Get(config.Encoder).SupportsAomParams)
            {
                _logger.LogInfo(
                    $"[INFO] 编码器 {config.Encoder} 不支持 -aom-params，" +
                    "aq-mode/deltaq-mode 等参数将被忽略");
            }

            // 防呆：输出模板不含 {index} 或 {name} → 多文件可能互相覆盖
            if (!config.OutputNameFormat.Contains("{index}") &&
                !config.OutputNameFormat.Contains("{name}"))
            {
                SafeWriteLine(
                    "[WARN] 输出模板不含 {index} 或 {name}，" +
                    "编码多张图片时可能互相覆盖。");
            }

            // 防呆：CPU-used 超过编码器上限 → 自动钳制
            var cpuEnc = Av1EncoderFactory.Get(config.Encoder);
            if (config.FinalCpuUsed > cpuEnc.MaxSpeed)
            {
                SafeWriteLine(
                    $"[WARN] FinalCpuUsed={config.FinalCpuUsed} " +
                    $"超过 {config.Encoder} 上限 ({cpuEnc.MaxSpeed})，" +
                    $"已钳制为 {cpuEnc.MaxSpeed}");
                config.FinalCpuUsed = cpuEnc.MaxSpeed;
                config.SearchCpuUsed = Math.Min(config.SearchCpuUsed, cpuEnc.MaxSpeed);
            }
            if (config.SearchCpuUsed > cpuEnc.MaxSpeed)
            {
                SafeWriteLine(
                    $"[WARN] SearchCpuUsed={config.SearchCpuUsed} " +
                    $"超过 {config.Encoder} 上限 ({cpuEnc.MaxSpeed})，" +
                    $"已钳制为 {cpuEnc.MaxSpeed}");
                config.SearchCpuUsed = cpuEnc.MaxSpeed;
            }

            int cpuCount = Environment.ProcessorCount;

            // 若用户未通过 -j 指定并发数，则自动计算
            // 若用户未通过 -j 指定并发数，则自动计算
            if (!config.UserSpecifiedMaxJobs)
            {
                config.MaxJobs = isHardwareEncoder
                    ? Math.Max(2, cpuCount * 2)               // 硬件编码器可适当提高并行
                    : Math.Max(2, (int)Math.Sqrt(cpuCount));  // 软件编码器：核心数平方根
            }
            if (config.MaxJobs < 1) config.MaxJobs = 1;

            int ssimSlots = Math.Max(2, cpuCount);   // 质量评估仍可使用全部核心

            _maxFfmpegConcurrency = config.MaxJobs;
            _ssimConcurrency = new SemaphoreSlim(ssimSlots);
            _ffmpegSlots = new SemaphoreSlim(config.MaxJobs);   // 核心修复：直接使用 config.MaxJobs

            _guiProgress = progress;       // ★ 改为 _guiProgress

            _advancedMetricSemaphore = new SemaphoreSlim(Math.Max(1, Environment.ProcessorCount / 2));

            // 初始化无损验证失败隔离目录
            _failedVerificationDir = Path.Combine(_outputDir, "_failed_verification");
            if (!_fs.DirectoryExists(_failedVerificationDir))
            {
                _fs.CreateDirectory(_failedVerificationDir);
            }
            _failedCsvPath = Path.Combine(_failedVerificationDir, "failed_verification.csv");

            _csvPath = Path.Combine(_outputDir, "avif_stats.csv");

            // Journal 断点续传
            string sessionDir = Path.Combine(_outputDir, ".session");
            _fs.CreateDirectory(sessionDir);
            _journalPath = Path.Combine(sessionDir, "journal.ndjson");
            _snapshotPath = Path.Combine(sessionDir, "snapshot.json");

            // ★ 跨平台兜底：进程退出时（Ctrl+C、窗口关闭、Environment.Exit）强制清理子进程
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                foreach (var p in _spawnedProcesses)
                {
                    try
                    {
                        if (!p.HasExited)
                        {
                            p.Kill(entireProcessTree: true);
                        }
                    }
                    catch { }
                }
            };

        }

        /// <summary> 判断编码器是否支持 -still-picture 1 参数（AVIF 单帧静止图像标志） </summary>
        /// <summary>
        /// 等比缩放图片，使长边不超过 maxDim，输出为 PNG 临时文件。
        /// 保留 Alpha 通道（如果源文件有透明信息）。
        /// </summary>
        private async Task ScaleImageAsync(string input, string output, int maxDim)
        {
            var (w, h) = await GetResolutionAsync(input);
            if (w <= 0 || h <= 0)
                throw new Exception($"无法获取分辨率: {input}");

            int longSide = Math.Max(w, h);
            if (longSide <= maxDim)
            {
                _fs.CopyFile(input, output, true);   // 替换 File.Copy
                return;
            }

            double scale = (double)maxDim / longSide;
            int targetW = (int)Math.Round(w * scale) & ~1;
            int targetH = (int)Math.Round(h * scale) & ~1;
            if (targetW < 2) targetW = 2;
            if (targetH < 2) targetH = 2;

            bool hasAlpha = await SourceHasAlpha(input);
            string pixFmt = hasAlpha ? "rgba" : "rgb24";

            string filter = $"scale={targetW}:{targetH}:flags=lanczos";
            string args = $"-loglevel error -hide_banner -i \"{input}\" -vf \"{filter}\" -pix_fmt {pixFmt} \"{output}\"";

            (bool ok, string err) = await RunFfmpegExAsync(_ffmpegPath, args, TimeSpan.FromMinutes(2));
            if (!ok)
                throw new Exception($"缩放失败: {err}");
        }
        private static double ComputeMixScore(QualityMetrics m)
        {
            return MetricRegistry.ComputeMixScore(m);
        }

        private async Task<string> RunProbeAsync(string file, string args)
        {
            var (exitCode, stdout, stderr) = await _processRunner.RunAsync(
                file, args, TimeSpan.FromSeconds(30), _globalCts?.Token ?? default);
            return stdout;
        }



        #region Journal 断点续传

        private void InitJournal()
        {
            lock (_journalLock)
            {
                _journalWriter?.Dispose();
                for (int retry = 0; ; retry++)
                {
                    try
                    {
                        _journalWriter = new StreamWriter(_journalPath, append: true, Encoding.UTF8)
                        { AutoFlush = true };
                        break;
                    }
                    catch (IOException) when (retry < 20) { Thread.Sleep(200); }
                }
                _lastSnapshotTime = DateTime.UtcNow;
                _journalCountSinceSnapshot = 0;
            }
        }

        private void AppendJournal(string file, string evt, object? extra = null)
        {
            lock (_journalLock)
            {
                if (_journalWriter == null) return;
                var obj = new Dictionary<string, object>
                {
                    ["v"] = 1,
                    ["ts"] = DateTime.UtcNow.ToString("o"),
                    ["file"] = file,
                    ["evt"] = evt
                };
                if (extra != null)
                {
                    foreach (var prop in extra.GetType().GetProperties())
                        obj[prop.Name.ToLower()] = prop.GetValue(extra) ?? "";
                }
                string line = System.Text.Json.JsonSerializer.Serialize(obj);
                _journalWriter.WriteLine(line);
                _journalWriter.Flush();  // 逐行刷盘
                _journalCountSinceSnapshot++;

                // ★ 周期性快照：合并旧快照完成列表 + 本次新增
                if (_journalCountSinceSnapshot >= 500)
                {
                    var (oldDone, _, _) = LoadSnapshot();
                    var newDone = ReplayJournal(null);
                    SaveSnapshot(oldDone.Union(newDone));
                }
            }
        }

        private HashSet<string> ReplayJournal(DateTime? since)
        {
            var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!_fs.FileExists(_journalPath)) return completed;

            try
            {
                var lines = File.ReadAllLines(_journalPath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("ts", out var tsEl) &&
                            root.TryGetProperty("evt", out var evtEl) &&
                            root.TryGetProperty("file", out var fileEl))
                        {
                            if (since.HasValue &&
                                DateTime.TryParse(tsEl.GetString(), out var ts) &&
                                ts < since.Value)
                                continue;
                            if (evtEl.GetString() == "success")
                                completed.Add(fileEl.GetString() ?? "");
                        }
                    }
                    catch (JsonException)
                    {
                        // 损坏行：截断并退出
                        break;
                    }
                }
            }
            catch { }
            return completed;
        }

        private void SaveSnapshot(IEnumerable<string> completed)
        {
            if (string.IsNullOrEmpty(_snapshotPath)) return;
            try
            {
                var snapshot = new
                {
                    v = 3,
                    ts = DateTime.UtcNow.ToString("o"),
                    completed = completed.ToArray(),
                    inputDir = _inputDir,
                    config = new
                    {
                        _config.Encoder,
                        _config.Lossless,
                        _config.UseCRFSearch,
                        _config.BaseCRF,
                        _config.MinCRF,
                        _config.MaxCRF,
                        _config.MetricMode,
                        _config.PixelFormat,
                        _config.BitDepth,
                        _config.OutputNameFormat,
                        _config.RecurseSubdirectories,
                        _config.SerialEncode,
                        _config.UsePriorSearch,
                        _config.UseProxySearch,
                        _config.SearchCpuUsed,
                        _config.FinalCpuUsed,
                        _config.MaxResolution,
                        _config.MaxJobs,
                        FileConflictStrategy = _config.FileConflictStrategy.ToString(),
                        _config.InputExtensions,
                        _config.EncodeTimeoutMinutes,
                        _config.SearchTimeoutMinutes,
                        _config.SafeTimeoutMinutes,
                        _config.SsimTimeoutMinutes,
                        _config.SweepMode,
                        _config.DryRun,
                        _config.Verbose
                    }
                };
                string tmp = _snapshotPath + ".tmp";
                File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(snapshot), Encoding.UTF8);
                if (_fs.FileExists(_snapshotPath))
                    _fs.DeleteFile(_snapshotPath);
                File.Move(tmp, _snapshotPath);
                _journalCountSinceSnapshot = 0;
                _lastSnapshotTime = DateTime.UtcNow;

                // 截断 Journal：原子替换，避免 AppendJournal 在窗口期丢失条目
                lock (_journalLock)
                {
                    _journalWriter?.Flush();
                    _journalWriter?.Dispose();
                    try { if (_fs.FileExists(_journalPath)) _fs.DeleteFile(_journalPath); } catch { }
                    _journalWriter = new StreamWriter(_journalPath, append: false, Encoding.UTF8) { AutoFlush = false };
                }
            }
            catch { }
        }

        private (HashSet<string> completed, string? configJson, string? inputDir) LoadSnapshot()
        {
            if (!_fs.FileExists(_snapshotPath)) return (new HashSet<string>(), null, null);
            try
            {
                string json = File.ReadAllText(_snapshotPath, Encoding.UTF8);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string? cfgJson = null, inputDir = null;
                if (root.TryGetProperty("config", out var cfgEl))
                    cfgJson = cfgEl.GetRawText();
                if (root.TryGetProperty("inputDir", out var idEl))
                    inputDir = idEl.GetString();
                if (root.TryGetProperty("completed", out var arr))
                {
                    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var el in arr.EnumerateArray())
                        set.Add(el.GetString() ?? "");
                    return (set, cfgJson, inputDir);
                }
            }
            catch { }
            return (new HashSet<string>(), null, null);
        }

        private void CloseJournal()
        {
            lock (_journalLock)
            {
                _journalWriter?.Flush();
                _journalWriter?.Dispose();
                _journalWriter = null;
            }
        }

        /// <summary>按逗号分割 CSV 行，正确处理双引号包裹的字段（引号内逗号不分割）</summary>
        private static string[] SplitCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            int start = 0;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (line[i] == ',' && !inQuotes)
                {
                    result.Add(Unquote(line[start..i]));
                    start = i + 1;
                }
            }
            result.Add(Unquote(line[start..]));
            return result.ToArray();
        }

        private static string Unquote(string s)
        {
            s = s.Trim();
            if (s.StartsWith('"') && s.EndsWith('"') && s.Length >= 2)
                return s[1..^1].Replace("\"\"", "\"");
            return s;
        }

        #endregion

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { CloseJournal(); } catch { }
            try { FinalCleanup(); } catch { }
            try { _globalCts?.Cancel(); } catch { }
            try { _globalCts?.Dispose(); } catch { }
            _globalCts = null;
            try { _ssimConcurrency?.Dispose(); } catch { }
            try { _ffmpegSlots?.Dispose(); } catch { }
            if (_cancelKeyHandler != null)
                Console.CancelKeyPress -= _cancelKeyHandler;
            _advancedMetricSemaphore?.Dispose();
            _lockStream?.Dispose();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Probe 探测

        private readonly ConcurrentDictionary<string, ProbeInfo> _probeCache = new();

        private async Task<ProbeInfo?> GetProbeInfoAsync(string filePath)
        {
            string key = GetNormalizedPathForCache(filePath);
            if (_probeCache.TryGetValue(key, out var cached)) return cached;

            // 一次性 ffprobe 获取所有信息
            string args = $"-v error -select_streams v:0 -show_entries stream=pix_fmt,width,height,is_lossless,color_primaries,color_transfer,color_space,color_range -of json \"{filePath}\"";
            string json = await RunProbeAsync(_ffprobePath, args);
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var streams = doc.RootElement.GetProperty("streams");
                if (streams.GetArrayLength() == 0) return null;

                var stream = streams[0];
                string fmt = stream.GetProperty("pix_fmt").GetString()?.ToLower() ?? "yuv420p";
                int w = stream.GetProperty("width").GetInt32();
                int h = stream.GetProperty("height").GetInt32();

                bool hasAlpha = fmt switch
                {
                    "rgba" or "bgra" or "argb" or "abgr" => true,
                    "rgba64le" or "bgra64le" => true,
                    _ => false
                };

                // 尝试提取色彩字段，忽略 unknown/reserved
                static string? TryGetStringProperty(JsonElement element, string propertyName)
                {
                    if (element.TryGetProperty(propertyName, out var prop))
                    {
                        string val = prop.GetString()?.Trim().ToLowerInvariant() ?? "";
                        return !string.IsNullOrWhiteSpace(val) && val != "unknown" && val != "reserved" ? val : null;
                    }
                    return null;
                }

                string? colorPrimaries = TryGetStringProperty(stream, "color_primaries");
                string? colorTransfer = TryGetStringProperty(stream, "color_transfer");
                string? colorSpace = TryGetStringProperty(stream, "color_space");
                string? colorRange = TryGetStringProperty(stream, "color_range");

                var info = new ProbeInfo
                {
                    PixFmt = fmt,
                    HasAlpha = hasAlpha,
                    Width = w,
                    Height = h,
                    ColorPrimaries = colorPrimaries,
                    ColorTransfer = colorTransfer,
                    ColorSpace = colorSpace,
                    ColorRange = colorRange
                };
                _probeCache[key] = info;
                return info;
            }
            catch { return null; }
        }

        static string? TryGetStringProperty(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop))
            {
                string val = prop.GetString()?.Trim().ToLowerInvariant() ?? "";
                return !string.IsNullOrWhiteSpace(val) && val != "unknown" && val != "reserved" ? val : null;
            }
            return null;
        }






        /// <summary> 返回编码器特定的参数片段，已包含完整的速度控制和分块部分 </summary>

        private static string GetNormalizedPathForCache(string input)
        {
            try
            {
                string full = Path.GetFullPath(input).Trim();
                // 启用长路径支持，确保缓存键一致
                full = EnsureLongPath(full);
                return OperatingSystem.IsWindows() ? full.ToLowerInvariant() : full;
            }
            catch
            {
                return $"__fallback__{Path.GetFileName(input).ToLowerInvariant()}";
            }
        }


        /// <summary>
        /// 使用 libvmaf 一次性计算 ref (原图) 与 dist (编码后) 的 SSIM / PSNR?Y / MS?SSIM / VMAF。
        /// 返回 QualityMetrics，失败返回 null。会自动处理分辨率不一致的情况（缩放至相同尺寸）。
        /// </summary>
        private async Task<QualityMetrics?> ComputeAllMetricsAsync(string refPath, string distPath)
        {
            if (!EnsureFilesValid(refPath, distPath)) return null;

            string workDir = Environment.CurrentDirectory;
            string metricsDir = Path.Combine(workDir, $"avif_metrics_tmp_{_instanceId}");
            Directory.CreateDirectory(metricsDir);

            string jsonName = $"_metrics_{Guid.NewGuid():N}.json";
            string jsonPath = Path.Combine(metricsDir, jsonName);
            string logPathSafe = jsonName;

            try
            {
                var (w1, h1) = await GetResolutionAsync(refPath).WaitAsync(TimeSpan.FromSeconds(30));
                var (w2, h2) = await GetResolutionAsync(distPath).WaitAsync(TimeSpan.FromSeconds(30));

                string filter;
                if (w1 > 0 && h1 > 0 && w2 > 0 && h2 > 0 && (w1 != w2 || h1 != h2))
                {
                    int w = Math.Min(w1, w2);
                    int h = Math.Min(h1, h2);
                    filter = $"[0:v]scale={w}:{h}[ref];[1:v]scale={w}:{h}[dist];[ref][dist]libvmaf=" +
                             $"feature=name=psnr|name=float_ssim|name=float_ms_ssim:" +
                             $"model='version=vmaf_float_v0.6.1':log_path={logPathSafe}:log_fmt=json:n_threads=4";
                }
                else
                {
                    filter = $"[0:v][1:v]libvmaf=feature=name=psnr|name=float_ssim|name=float_ms_ssim:" +
                             $"model='version=vmaf_float_v0.6.1':" +
                             $"log_path={logPathSafe}:log_fmt=json:n_threads=4";
                }

                string args = $"-loglevel error -hide_banner -i \"{refPath}\" -i \"{distPath}\" " +
                              $"-filter_complex \"{filter}\" -frames:v 1 -f null -";

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo(_ffmpegPath, args)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = metricsDir
                    }
                };

                process.Start();

                // ★ 内存兜底追踪（Job Object 失败时备用）
                _spawnedProcesses.Add(process);

                // ★ 仅在 Windows 平台将子进程加入全局 Job Object
                if (OperatingSystem.IsWindows())
                {
                    JobObjectHelper.AssignProcess(process);
                }

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                var timeout = TimeSpan.FromMinutes(_config.SsimTimeoutMinutes);
                using var timeoutCts = new CancellationTokenSource(timeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    _globalCts?.Token ?? CancellationToken.None, timeoutCts.Token);

                try
                {
                    await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(linkedCts.Token));
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                    {
                        try { process.Kill(entireProcessTree: true); } catch { }
                        try { await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
                    }
                }

                string stdout = await stdoutTask;
                string stderr = await stderrTask;
                int exitCode = process.ExitCode;

                if (!string.IsNullOrWhiteSpace(stderr))
                    _logger.LogInfo($"ComputeAllMetrics stderr [{Path.GetFileName(refPath)}]: {stderr.Trim()}");

                if (exitCode != 0)
                {
                    _logger.LogInfo($"ComputeAllMetrics 失败 (exit {exitCode}) [{Path.GetFileName(refPath)}]: {stderr.Trim()}");
                    return null;
                }

                if (!File.Exists(jsonPath))
                {
                    _logger.LogInfo($"ComputeAllMetrics: JSON 文件未生成: {jsonPath}");
                    return null;
                }

                string json = await File.ReadAllTextAsync(jsonPath);
                QualityMetrics? metrics = ParseVmafJson(json);
                if (metrics == null) return null;

                // 合并 stdout 与 stderr，统一提取 VMAF，避免因输出位置不同而漏掉
                string combinedOutput = stdout + "\n" + stderr;
                double? vmafFromConsole = TryExtractVmaf(combinedOutput);

                if (vmafFromConsole.HasValue)
                {
                    // 控制台提取成功，覆盖 JSON 值（部分版本 JSON 中 VMAF 缺失或为假值）
                    metrics.VMAF = vmafFromConsole.Value;
                }
                else
                {
                    // 控制台也未提取到 → 检查 JSON 是否已给出有效 VMAF
                    if (double.IsNaN(metrics.VMAF))
                    {
                        _logger.LogInfo($"未提取到 VMAF 分数 [{Path.GetFileName(refPath)}]");
                    }
                }

                // PSNR-Y 接近 libvmaf 上限 60dB 时，用独立 PSNR 滤镜重算无上限值
                if (metrics.PSNR_Y >= 59.5)
                {
                    var uncappedPsnr = await ComputePsnrUncappedAsync(
                        refPath, distPath);
                    if (uncappedPsnr.HasValue)
                    {
                        metrics.PSNR_Y = uncappedPsnr.Value;
                    }
                }

                return metrics;
            }
            catch (Exception ex)
            {
                _logger.LogInfo($"ComputeAllMetrics 异常: {ex.Message}");
                return null;
            }
            finally
            {
                try
                {
                    if (File.Exists(jsonPath)) File.Delete(jsonPath);
                }
                catch { }
            }
        }

        /// <summary>
        /// 使用独立 ffmpeg PSNR 滤镜计算 Y 通道 PSNR，绕过 libvmaf 的 60dB 上限。
        /// 返回 PSNR-Y 值（可为 inf 即 double.PositiveInfinity），失败返回 null。
        /// </summary>
        private async Task<double?> ComputePsnrUncappedAsync(
            string refPath, string distPath)
        {
            try
            {
                string args =
                    $"-loglevel error -hide_banner " +
                    $"-i \"{refPath}\" -i \"{distPath}\" " +
                    $"-lavfi \"psnr=stats_file=-\" -f null -";

                var (exitCode, stdout, stderr) = await _processRunner.RunAsync(
                    _ffmpegPath, args, TimeSpan.FromMinutes(2),
                    _globalCts?.Token ?? default);

                if (exitCode != 0) return null;

                string output = stdout + stderr;
                // stats_file=- 输出格式: "psnr_y:inf" 或 "psnr_y:48.1234"
                var match = Regex.Match(output,
                    @"psnr_y:\s*(inf|[0-9.]+)",
                    RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string val = match.Groups[1].Value;
                    if (val.Equals("inf", StringComparison.OrdinalIgnoreCase))
                    {
                        return double.PositiveInfinity;
                    }
                    if (double.TryParse(val, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double psnr))
                    {
                        return psnr;
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogInfo($"ComputePsnrUncapped 异常: {ex.Message}");
                return null;
            }
        }

        private static double TryGetPooledDouble(JsonElement pooled, string key, string subKey)
        {
            try
            {
                if (pooled.TryGetProperty(key, out var e) &&
                    e.TryGetProperty(subKey, out var v))
                    return v.GetDouble();
            }
            catch { }
            return double.NaN;
        }

        private static double? TryExtractVmaf(string combinedOutput)
        {
            // 适配不同 FFmpeg 版本的输出格式
            var patterns = new[]
            {
        @"VMAF score:\s*([0-9.]+)",
        @"vmaf\s*=\s*([0-9.]+)",
        @"aggregate_vmaf\s*:\s*([0-9.]+)"
    };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(combinedOutput, pattern, RegexOptions.IgnoreCase);
                if (match.Success &&
                    double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var score))
                {
                    return score;
                }
            }
            return null;
        }
        private QualityMetrics? ParseVmafJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var pooled = doc.RootElement.GetProperty("pooled_metrics");

                double ssim = pooled.TryGetProperty("float_ssim", out var e) ? e.GetProperty("mean").GetDouble() : 0;
                double ms_ssim = pooled.TryGetProperty("float_ms_ssim", out e) ? e.GetProperty("mean").GetDouble() : 0;
                // VMAF 字段缺失时设为 NaN，避免 -1 或 0 被误判为有效分数
                double vmaf = pooled.TryGetProperty("vmaf", out e)
                                ? e.GetProperty("mean").GetDouble()
                                : double.NaN;
                double psnr_y = pooled.TryGetProperty("psnr_y", out e) ? e.GetProperty("mean").GetDouble() : 0;
                // CAMBI/ADM 暂不可用，择机恢复
                // double cambi = TryGetPooledDouble(pooled, "cambi", "cambi");
                // if (double.IsNaN(cambi)) cambi = TryGetPooledDouble(pooled, "cambi", "score");
                // double adm = TryGetPooledDouble(pooled, "adm", "adm");
                // if (double.IsNaN(adm)) adm = TryGetPooledDouble(pooled, "adm", "score");

                return new QualityMetrics
                {
                    SSIM = ssim,
                    PSNR_Y = psnr_y,
                    MS_SSIM = ms_ssim,
                    VMAF = vmaf,
                };
            }
            catch (Exception ex)
            {
                _logger.LogInfo($"解析 VMAF JSON 失败: {ex.Message}");
                return null;
            }
        }








        /// <summary>
        /// 根据模板生成输出文件名（不含路径）
        /// </summary>
        /// <summary>
        /// 根据模板和源文件信息生成输出文件名（不含目录）
        /// </summary>
        private string GetOutputFileName(string inputFile, int index)
        {
            string template = _config.OutputNameFormat.Trim('"', '\'').Trim();
            string name = Path.GetFileNameWithoutExtension(inputFile);
            string ext = Path.GetExtension(inputFile);
            string dir = Path.GetFileName(Path.GetDirectoryName(inputFile)) ?? "";
            var now = DateTime.Now;

            // 基础占位符
            string result = template
                .Replace("{name}", name)
                .Replace("{filename}", name)
                .Replace("{ext}", ext)
                .Replace("{dir}", dir);

            // 编码参数占位符
            result = result
                .Replace("{encoder}", _config.Encoder)
                .Replace("{crf}", _config.BaseCRF.ToString())
                .Replace("{preset}", _config.MetricMode ?? "")
                .Replace("{speed}", _config.FinalCpuUsed.ToString())
                .Replace("{pixfmt}", _config.PixelFormat ?? "auto")
                .Replace("{bitdepth}", _config.BitDepth.ToString())
                .Replace("{lossless}", _config.Lossless ? "lossless" : "lossy");

            // 时间占位符
            result = result
                .Replace("{date}", now.ToString("yyyy-MM-dd"))
                .Replace("{time}", now.ToString("HH-mm-ss"))
                .Replace("{datetime}", now.ToString("yyyy-MM-dd_HH-mm-ss"));

            // {index} 支持自定义宽度: {index}→01, {index:000}→001
            result = Regex.Replace(result, @"\{index(?::(\d+))?\}",
                m => index.ToString("D" + (m.Groups[1].Success ? m.Groups[1].Value : "2")));

            // 确保扩展名为 .avif
            if (!result.EndsWith(".avif", StringComparison.OrdinalIgnoreCase))
                result += ".avif";

            // 替换非法文件名字符
            foreach (char c in Path.GetInvalidFileNameChars())
                result = result.Replace(c, '_');

            return result.Trim();
        }



        // ==================== 主入口 ====================
        public async Task RunAsync(CancellationToken externalToken = default)
        {
            try
            {
                _globalCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
                _cancelKeyHandler = (s, e) =>
                {
                    e.Cancel = true;
                    SafeWriteLine("\n[WARN] 正在安全停止，请稍候...");
                    _globalCts?.Cancel();
                };
                Console.CancelKeyPress += _cancelKeyHandler;

                Console.OutputEncoding = Encoding.UTF8;
                _progress.Start(DateTime.Now);

                // 启动诊断：Job Object 状态
                if (OperatingSystem.IsWindows())
                {
                    if (JobObjectHelper.IsActive)
                        _logger.LogInfo("[Job] 子进程保护已激活 — 主进程退出时自动终止所有 ffmpeg");
                    else
                        _logger.LogInfo("[Job] 子进程保护未激活 — 使用内存进程列表兜底终止");
                }

                _logger.LogInfo($"Pipeline started: CRF={_config.BaseCRF} TargetSSIM={_config.TargetSSIM}");
                _logger.LogInfo(
                    $"Encoder={_config.Encoder} " +
                    $"Lossless={_config.Lossless} " +
                    $"PixelFmt={_config.PixelFormat ?? "auto"} " +
                    $"BitDepth={_config.BitDepth} " +
                    $"CRFSearch={_config.UseCRFSearch} " +
                    $"MaxJobs={_config.MaxJobs}");
                if (_config.Lossless)
                {
                    _logger.LogInfo("无损模式：编码后逐像素验证，失败文件隔离到 _failed_verification/");
                }

                await PrintStartupInfoAsync();

                var files = await ScanAndPrepareFilesAsync();
                if (files == null || files.Count == 0) return;

                // ★ 检测输出文件名冲突
                var nameGroups = files.GroupBy(f => GetOutputFileName(f.path, f.index));
                foreach (var g in nameGroups.Where(g => g.Count() > 1))
                {
                    _logger.LogInfo($"[NAME-CONFLICT] 输出重名: {g.Key} ← {string.Join(", ", g.Select(f => Path.GetFileName(f.path)))}");
                }

                // ★ 断点续传：清理草稿 + 回放日志 + 过滤已完成
                if (_config.Resume)
                {
                    _logger.LogInfo("[RESUME] 断点续传模式：清理临时文件...");
                    // 清理编码草稿
                    foreach (var f in _fs.GetFiles(_outputDir, "_tmp_*.avif"))
                        try { _fs.DeleteFile(f); } catch { }
                    foreach (var f in _fs.GetFiles(_outputDir, "_p_*.avif"))
                        try { _fs.DeleteFile(f); } catch { }
                    // 清理搜索临时目录（用 Directory.GetDirectories 而非 GetFiles）
                    try
                    {
                        foreach (var dir in Directory.GetDirectories(_outputDir, "_search_advanced_*"))
                            try { if (_fs.DirectoryExists(dir)) _fs.DeleteDirectory(dir, true); } catch { }
                        foreach (var dir in Directory.GetDirectories(_outputDir, "_advanced_metrics_*"))
                            try { if (_fs.DirectoryExists(dir)) _fs.DeleteDirectory(dir, true); } catch { }
                    }
                    catch { }

                    // 加载快照并回放日志
                    // ★ 保守策略：三个数据源取交集（全部确认完成才算完成）
                    var (snapshotDone, savedConfigJson, savedInputDir) = LoadSnapshot();

                    // 从快照恢复编码配置（--resume 时无需重新指定参数）
                    if (savedConfigJson != null)
                    {
                        try
                        {
                            using var cfgDoc = JsonDocument.Parse(savedConfigJson);
                            var cfg = cfgDoc.RootElement;
                            if (cfg.TryGetProperty("Encoder", out var enc)) _config.Encoder = enc.GetString()!;
                            if (cfg.TryGetProperty("Lossless", out var ll)) _config.Lossless = ll.GetBoolean();
                            if (cfg.TryGetProperty("UseCRFSearch", out var sr)) _config.UseCRFSearch = sr.GetBoolean();
                            if (cfg.TryGetProperty("BaseCRF", out var bcrf)) _config.BaseCRF = bcrf.GetInt32();
                            if (cfg.TryGetProperty("MinCRF", out var mn)) _config.MinCRF = mn.GetInt32();
                            if (cfg.TryGetProperty("MaxCRF", out var mx)) _config.MaxCRF = mx.GetInt32();
                            if (cfg.TryGetProperty("MetricMode", out var mm)) _config.MetricMode = mm.GetString()!;
                            if (cfg.TryGetProperty("PixelFormat", out var pf)) _config.PixelFormat = pf.GetString();
                            if (cfg.TryGetProperty("BitDepth", out var bd)) _config.BitDepth = bd.GetInt32();
                            if (cfg.TryGetProperty("OutputNameFormat", out var ot)) _config.OutputNameFormat = ot.GetString()!;
                            if (cfg.TryGetProperty("RecurseSubdirectories", out var rc)) _config.RecurseSubdirectories = rc.GetBoolean();
                            if (cfg.TryGetProperty("SerialEncode", out var se)) _config.SerialEncode = se.GetBoolean();
                            if (cfg.TryGetProperty("UsePriorSearch", out var ps)) _config.UsePriorSearch = ps.GetBoolean();
                            if (cfg.TryGetProperty("UseProxySearch", out var px)) _config.UseProxySearch = px.GetBoolean();
                            if (cfg.TryGetProperty("SearchCpuUsed", out var sc)) _config.SearchCpuUsed = sc.GetInt32();
                            if (cfg.TryGetProperty("FinalCpuUsed", out var fc)) _config.FinalCpuUsed = fc.GetInt32();
                            if (cfg.TryGetProperty("MaxResolution", out var mr)) _config.MaxResolution = mr.GetInt32();
                            if (cfg.TryGetProperty("MaxJobs", out var mj)) _config.MaxJobs = mj.GetInt32();
                            if (cfg.TryGetProperty("InputExtensions", out var ie)) _config.InputExtensions = ie.GetString();
                            if (cfg.TryGetProperty("EncodeTimeoutMinutes", out var et)) _config.EncodeTimeoutMinutes = et.GetInt32();
                            if (cfg.TryGetProperty("SearchTimeoutMinutes", out var st)) _config.SearchTimeoutMinutes = st.GetInt32();
                            if (cfg.TryGetProperty("SafeTimeoutMinutes", out var sf)) _config.SafeTimeoutMinutes = sf.GetInt32();
                            if (cfg.TryGetProperty("SsimTimeoutMinutes", out var ss)) _config.SsimTimeoutMinutes = ss.GetInt32();
                            if (cfg.TryGetProperty("SweepMode", out var sw)) _config.SweepMode = sw.GetBoolean();
                            if (cfg.TryGetProperty("DryRun", out var dr)) _config.DryRun = dr.GetBoolean();
                            if (cfg.TryGetProperty("Verbose", out var vb)) _config.Verbose = vb.GetBoolean();
                            _logger.LogInfo($"[RESUME] 已从快照恢复编码配置: Encoder={_config.Encoder} CRF={_config.BaseCRF}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogInfo($"[RESUME] 配置恢复失败: {ex.Message}，使用当前参数");
                        }
                    }
                    var journalDone = ReplayJournal(null);  // 回放全部日志（不限时间戳）
                    var csvDone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // CSV：提取 "成功" 行对应的输入文件（正确处理引号内逗号）
                    if (_fs.FileExists(_csvPath))
                    {
                        try
                        {
                            var csvLines = File.ReadAllLines(_csvPath);
                            int statusIdx = -1, fileIdx = -1;
                            for (int i = 0; i < csvLines.Length; i++)
                            {
                                var cols = SplitCsvLine(csvLines[i]);
                                if (i == 0)
                                {
                                    for (int c = 0; c < cols.Length; c++)
                                    {
                                        if (cols[c] == "状态") statusIdx = c;
                                        if (cols[c] == "文件名") fileIdx = c;
                                    }
                                    continue;
                                }
                                if (statusIdx >= 0 && fileIdx >= 0 &&
                                    statusIdx < cols.Length && fileIdx < cols.Length &&
                                    cols[statusIdx] == "成功")
                                {
                                    string csvFileName = cols[fileIdx];
                                    // 用实际 index 反向映射（而非 -1，避免索引模板错位）
                                    foreach (var (path, idx) in files)
                                    {
                                        string outPath = Path.Combine(_outputDir, GetOutputFileName(path, idx));
                                        if (Path.GetFileName(outPath) == csvFileName)
                                        {
                                            csvDone.Add(path);
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    // 交集：三源全部确认 → 才视为完成
                    var completed = new HashSet<string>(journalDone, StringComparer.OrdinalIgnoreCase);

                    _logger.LogInfo(
                        $"[RESUME] journalDone={journalDone.Count} → completed={completed.Count}");

                    // 文件系统交叉验证：仅日志缺失时记录，不自动标记完成（避免参数变更误判）
                    foreach (var (path, idx) in files)
                    {
                        if (completed.Contains(path)) continue;
                        string outPath = Path.Combine(_outputDir, GetOutputFileName(path, idx));
                        if (_fs.FileExists(outPath) && _fs.GetFileLength(outPath) >= 200)
                            _logger.LogInfo(
                                $"[RESUME] 输出文件存在但日志无记录: {Path.GetFileName(outPath)}，删除旧文件并重新编码");
                        try { _fs.DeleteFile(outPath); } catch { }
                    }

                    // 过滤已完成
                    var remaining = files.Where(f => !completed.Contains(f.path)).ToList();
                    int skipped = files.Count - remaining.Count;
                    _logger.LogInfo($"[RESUME] {skipped}/{files.Count} 已完成，剩余 {remaining.Count} 待处理");
                    if (remaining.Count == 0)
                    {
                        _logger.LogInfo("[RESUME] 全部已完成，无需处理");
                        return;
                    }
                    files = remaining;
                    // 总文件数不变（ScanAndPrepareFilesAsync 已设），只调整已完成计数
                    _progress.SetInitialProcessed(skipped);
                    _guiProgress?.Report(Math.Min(100, _progress.ProcessedCount * 100 / Math.Max(1, _progress.TotalFiles)));
                }

                // 初始化 Journal；非恢复模式先清理旧快照避免混淆
                // Resume 模式下保留所有数据，非 Resume 清除旧数据
                if (!_config.Resume)
                {
                    try { if (_fs.FileExists(_snapshotPath)) _fs.DeleteFile(_snapshotPath); } catch { }
                    try { if (_fs.FileExists(_journalPath)) _fs.DeleteFile(_journalPath); } catch { }
                }
                InitJournal();

                var results = await ProcessInitialBatchAsync(files);
                results = await RetryFailuresAsync(results);

                // 退出前合并旧已完成 + 新完成 → 保存最终快照
                if (_config.Resume)
                {
                    // 合并快照中已有的完成列表
                    var (oldCompleted, _, _) = LoadSnapshot();
                    var newCompleted = results.Where(r => r != null && (r.Success || r.Skipped))
                        .Select(r => r!.InputPath);
                    SaveSnapshot(oldCompleted.Union(newCompleted));
                    int totalNonSkipped = results.Count(r => r != null && !r.Skipped);
                    if (newCompleted.Count() == totalNonSkipped)
                    {
                        CloseJournal();
                        try { if (_fs.FileExists(_journalPath)) _fs.DeleteFile(_journalPath); } catch { }
                    }
                }

                await PrintSummaryAndExport(results);
            }
            finally
            {
                FinalCleanup();   // 无论成功、失败、异常都会执行
            }
        }

        #endregion

        #region 启动与编排

        /// <summary> 打印启动信息，包括编码器检测 </summary>
        private async Task PrintStartupInfoAsync()
        {
            SafeWriteLine("===== AVIF 全自动编码流水线 =====");
            SafeWriteLine($"输入文件夹: {_inputDir}");
            SafeWriteLine($"输出文件夹: {_outputDir}");

            string crfInfo;
            if (_config.UseCRFSearch)
                crfInfo = $"基础CRF: {_config.BaseCRF}, 搜索范围: {_config.MinCRF}-{_config.MaxCRF}";
            else
                crfInfo = $"CRF: {_config.BaseCRF}";

            // 根据 MetricMode 动态生成标签和原生数值
            string metricMode = (_config.MetricMode ?? "vmaf").ToUpper();
            string targetDisplay = GetTargetDisplayString(_config);

            SafeWriteLine($"编码器: {_config.Encoder}");
            SafeWriteLine($"同时调用ffmpeg编码数: {_maxFfmpegConcurrency}");
            SafeWriteLine($"{crfInfo}  {metricMode}目标: {targetDisplay}  搜索: {_config.UseCRFSearch}  像素格式: {(_config.AutoSource ? "自适应" : (_config.PixelFormat ?? "动态"))}");
            SafeWriteLine($"文件名模板: {_config.OutputNameFormat}");
        }

        // 辅助方法：获取当前配置的目标值显示字符串（优先原生值）
        private static string GetTargetDisplayString(PresetConfig config)
        {
            string metricMode = config.MetricMode ?? "vmaf";
            double target = config.GetEffectiveTarget();

            if (metricMode.StartsWith("xpsnr", StringComparison.OrdinalIgnoreCase))
            {
                return $"{target:F1} dB ({(config.XpsnrTargetChannel ?? "W").ToUpper()})";
            }

            return metricMode.ToLower() switch
            {
                "vmaf" => target.ToString("F0"),
                "psnr" => target.ToString("F1") + " dB",
                "ssim" => target.ToString("F4"),
                "msssim" => target.ToString("F4"),
                "mix" => target.ToString("F4"),
                "ssimu2" => target.ToString("F4") + " (SSIMU2)",
                "butter3" => target.ToString("F4") + " (Butter3)",
                "gmsd" => target.ToString("F4") + " (GMSD)",
                _ => target.ToString("F4")
            };
        }

        /// <summary> 扫描输入目录，返回按文件大小降序排列的文件列表 </summary>
        private async Task<List<(string path, int index)>?> ScanAndPrepareFilesAsync()
        {
            if (!_fs.DirectoryExists(_inputDir))
            {
                SafeWriteLine("输入文件夹不存在。");
                return null;
            }
            _fs.CreateDirectory(_outputDir);

            // 根据配置构建扩展名列表：用户未指定则使用 12 种默认全部格式
            string[] extensions;
            if (!string.IsNullOrWhiteSpace(_config.InputExtensions))
            {
                extensions = _config.InputExtensions
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => x.StartsWith('.') ? x.ToLower() : $".{x}".ToLower())
                    .ToArray();
            }
            else
            {
                extensions = PresetConfig.DefaultInputExtensions;
            }

            var searchOption = _config.RecurseSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            // ★ 修复：去除可能的 \\?\ 长路径前缀，否则 Directory.EnumerateFiles 无法递归子目录
            string scanDir = NormalizePathForExternalTool(_inputDir);
            var sortedFiles = _fs.EnumerateFiles(scanDir, "*.*", searchOption)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                .OrderBy(f => f, new NaturalComparer())
                .Select((path, idx) => (path, index: idx + 1))
                .ToList();

            if (sortedFiles.Count == 0)
            {
                SafeWriteLine("未找到图片。");
                return null;
            }

            _progress.SetTotalFiles(sortedFiles.Count);
            SafeWriteLine($"待处理: {_progress.TotalFiles} 张\n");

            // 防呆：检测超大分辨率图片
            try
            {
                var probe = await GetProbeInfoAsync(sortedFiles[0].path);
                if (probe != null && Math.Max(probe.Width, probe.Height) > 3840)
                {
                    SafeWriteLine(
                        $"[INFO] 检测到高分辨率图片 " +
                        $"({probe.Width}x{probe.Height})，" +
                        "AV1 编码可能较慢，建议使用 --max-resolution 限制分辨率。");
                }
            }
            catch { }

            var processingOrder = sortedFiles
                .OrderByDescending(t => _fs.GetFileLength(t.path))
                .ToList();
            return processingOrder;
        }

        /// <summary> 首次批量处理所有文件 </summary>
        private async Task<List<EncodeResult?>> ProcessInitialBatchAsync(List<(string path, int index)> files)
        {
            var result = await ProcessFilesAsync(files, _config, isRetry: false);
            return [.. result.Select(r => (EncodeResult?)r)];
        }

        /// <summary> 重试失败的文件，并返回合并后的结果列表 </summary>
        /// <summary> 重试失败的文件，并返回合并后的结果列表 </summary>
        private async Task<List<EncodeResult?>> RetryFailuresAsync(List<EncodeResult?> results)
        {
            var failures = results.Where(r => r != null && !r.Success && !r.Skipped).ToList();
            if (failures.Count == 0) return results;

            SafeWriteLine($"\n[RETRY] 开始重试 {failures.Count} 个失败文件...");

            // 调整总数避免进度超过 100%
            _progress.SetTotalFiles(_progress.TotalFiles + failures.Count);

            // 使用 Result 中保存的完整输入路径，不再拼接
            var retryFiles = failures.Select(f => (filePath: f!.InputPath, index: f.Index)).ToList();

            // 删除已有的输出文件，避免干扰
            foreach (var (filePath, index) in retryFiles)
            {
                string outPath = GetOutputPath(filePath, index);
                if (_fs.FileExists(outPath))
                    try { _fs.DeleteFile(outPath); } catch { }
            }

            var retryResults = await ProcessFilesAsync(retryFiles, _config, isRetry: true);
            var resultList = results.ToList();
            foreach (var r in retryResults)
            {
                if (r == null) continue;
                int idx = resultList.FindIndex(existing => existing != null && existing.Index == r.Index);
                if (idx >= 0)
                    resultList[idx] = r;
            }
            return resultList;
        }

        /// <summary> 统计并打印最终总结，导出 CSV </summary>
        /// <summary> 统计并打印最终总结，导出 CSV </summary>
        private async Task PrintSummaryAndExport(List<EncodeResult?> results)
        {
            // ★ 等待所有后台高级指标计算完成
            if (!_advancedMetricTasks.IsEmpty)
            {
                SafeWriteLine("?? 等待后台高级指标计算完成...");
                try { await Task.WhenAll([.. _advancedMetricTasks]); }
                catch (Exception ex) { _logger.LogError($"后台高级任务异常: {ex.Message}"); }
            }

            // ★ 等待所有后台 XPSNR 计算完成并回填
            if (!_xpsnrTasks.IsEmpty)
            {
                try { await Task.WhenAll([.. _xpsnrTasks]); }
                catch (Exception ex) { _logger.LogInfo($"XPSNR 后台异常: {ex.Message}"); }
            }

            var totalTime = DateTime.Now - _progress.StartTime;
            var allResults = results.Where(r => r != null).Cast<EncodeResult>().ToList();
            int successCount = allResults.Count(r => !r.Skipped && r.Success);
            int failCount = allResults.Count(r => !r.Skipped && !r.Success);
            int skipCount = allResults.Count(r => r.Skipped);

            long totalOriginal = allResults.Where(r => !r.Skipped && r.Success).Sum(r => r.OriginalSize);
            long totalOutput = allResults.Where(r => !r.Skipped && r.Success).Sum(r => r.OutputSize);
            double overallRatio = totalOriginal == 0 ? 0 : 1.0 - (double)totalOutput / totalOriginal;

            SafeWriteLine("\n================ 转换完成 ================");
            SafeWriteLine($"总文件数: {_progress.TotalFiles}  成功: {successCount}  失败: {failCount}  跳过: {skipCount}");
            SafeWriteLine($"原始大小: {FormatSize(totalOriginal)}  输出大小: {FormatSize(totalOutput)}");
            SafeWriteLine($"整体压缩率: {overallRatio:P1}  总耗时: {FormatTimeSpan(totalTime)}");
            // 移除旧的缓存计数输出，因为 ICacheManager 未暴露计数属性
            _logger.LogInfo(
                $"Finished. 成功: {successCount}, 失败: {failCount}, " +
                $"跳过: {skipCount}, 耗时: {FormatTimeSpan(totalTime)}");
            if (successCount > 0)
            {
                double avgEncode = allResults
                    .Where(r => r.Success)
                    .Select(r => r.EncodeTime.TotalSeconds)
                    .DefaultIfEmpty(0).Average();
                _logger.LogInfo(
                    $"平均编码耗时: {avgEncode:F1}s, " +
                    $"整体压缩率: {overallRatio:P1}, " +
                    $"总输出: {FormatSize(totalOutput)}");
            }


            // 从缓存回填高级指标
            foreach (var r in allResults)
            {
                if (!string.IsNullOrEmpty(r.AdvancedMetricsCacheKey) && _cache.TryGetMetrics(r.AdvancedMetricsCacheKey, out var updated))
                {
                    r.FinalSSIMULACRA2 = updated?.SSIMULACRA2;
                    r.FinalButteraugli_Raw = updated?.Butteraugli_Raw;
                    r.FinalButteraugli_3norm = updated?.Butteraugli_3norm;
                    r.FinalGMSD = updated?.GMSD;
                    r.FinalXPSNR_Y = updated?.XPSNR_Y;
                    r.FinalXPSNR_U = updated?.XPSNR_U;
                    r.FinalXPSNR_V = updated?.XPSNR_V;
                    r.FinalWXPSNR = updated?.W_XPSNR;
                    // r.FinalCAMBI = updated?.CAMBI;   // 暂不可用
                    // r.FinalADM = updated?.ADM;       // 暂不可用
                }
            }

            // ★ 进度由指标回调推进，此处不再强制 100%

            // 标注外部工具缺失导致的高级指标空缺
            bool hasSsimu2 = EncoderUtils.FindExecutable("ssimulacra2") != null;
            bool hasButter = EncoderUtils.FindExecutable("butteraugli_main") != null;
            if (!hasSsimu2 || !hasButter)
            {
                var missingTools = new List<string>();
                if (!hasSsimu2) missingTools.Add("SSIMULACRA2(ssimulacra2.exe)");
                if (!hasButter) missingTools.Add("Butteraugli(butteraugli_main.exe)");
                string note = $"外部工具缺失: {string.Join(", ", missingTools)}";

                foreach (var r in allResults)
                {
                    bool advancedEmpty = !r.FinalSSIMULACRA2.HasValue &&
                        !r.FinalButteraugli_Raw.HasValue &&
                        !r.FinalButteraugli_3norm.HasValue;
                    if (r.Success && advancedEmpty)
                    {
                        r.ErrorMessage = string.IsNullOrEmpty(r.ErrorMessage)
                            ? note
                            : r.ErrorMessage + " | " + note;
                    }
                }
                SafeWriteLine(
                    $"[INFO] 外部工具缺失，高级指标单元格留空: {string.Join(", ", missingTools)}");
            }

            ExportCsv(allResults);
        }

        /// <summary> 清理编码缓存及临时文件 </summary>
        private void FinalCleanup()
        {
            try { foreach (var p in Process.GetProcessesByName("ffmpeg")) try { p.Kill(true); } catch { } } catch { }
            try { foreach (var p in Process.GetProcessesByName("ffprobe")) try { p.Kill(true); } catch { } } catch { }

            // ★ 兜底：强制杀掉所有曾启动的 ffmpeg 子进程（Job Object 失败时保底）
            foreach (var p in _spawnedProcesses)
            {
                try
                {
                    if (!p.HasExited)
                    {
                        p.Kill(entireProcessTree: true);
                        _logger.LogInfo($"强制终止残留进程 PID={p.Id}");
                    }
                }
                catch { }
            }
            // 释放所有 Process 对象
            foreach (var p in _spawnedProcesses)
            {
                try { if (p.HasExited) p.Dispose(); } catch { }
            }
            _spawnedProcesses.Clear();

            // 清理编码缓存目录
            CleanDirectory(Path.Combine(_outputDir, "_enc_cache"));

            // 清理缩放后的临时图片目录
            string scaledDir = Path.Combine(_outputDir, "_scaled");
            if (_fs.DirectoryExists(scaledDir))
            {
                try { _fs.DeleteDirectory(scaledDir, true); } catch { }
            }

            // 清理带 _p_ 前缀的临时 AVIF 文件
            foreach (var f in _fs.GetFiles(_outputDir, "_p_*.avif"))
                try { _fs.DeleteFile(f); } catch { }
            foreach (var f in _fs.GetFiles(_outputDir, "_tmp_*.avif"))
                try { _fs.DeleteFile(f); } catch { }

            // ★ 清理残留的指标临时目录
            try
            {
                foreach (var dir in Directory.GetDirectories(_outputDir, "_search_advanced_*"))
                    try { Directory.Delete(dir, true); } catch { }
                foreach (var dir in Directory.GetDirectories(_outputDir, "_advanced_metrics_*"))
                    try { Directory.Delete(dir, true); } catch { }
            }
            catch { }

            // 清理本实例生成的 ComputeAllMetrics 临时 JSON 目录
            string metricsDir = Path.Combine(Environment.CurrentDirectory, $"avif_metrics_tmp_{_instanceId}");
            if (Directory.Exists(metricsDir))
            {
                try { Directory.Delete(metricsDir, true); } catch { }
            }

            // 兼容旧版：清理无实例后缀的遗留目录（过渡期后移除）
            string legacyMetricsDir = Path.Combine(Environment.CurrentDirectory, "avif_metrics_tmp");
            if (Directory.Exists(legacyMetricsDir))
            {
                try { Directory.Delete(legacyMetricsDir, true); } catch { }
            }
        }

        private void CleanDirectory(string dir)
        {
            if (_fs.DirectoryExists(dir))
            {
                try
                {
                    _fs.DeleteDirectory(dir, true);
                    _logger.LogInfo($"缓存已清理: {dir}");
                }
                catch (Exception ex) { _logger.LogInfo($"清理失败: {dir} - {ex.Message}"); }
            }
        }

        // ========== 修复后的 PrintProgress（区分跳过） ==========
        private void PrintProgress(EncodeResult? r)
        {
            SafeWriteLine(_progress.GetProgressLine(r));
        }



        /// <summary>
        /// 确保路径在 Windows 上使用长路径格式（添加 \\?\ 前缀），
        /// 从而突破 260 字符的 MAX_PATH 限制。
        /// </summary>
        private static string EnsureLongPath(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                // 已添加过长路径前缀，直接返回
                if (path.StartsWith(@"\\?\"))
                    return path;

                string full = Path.GetFullPath(path);

                // 处理 UNC 路径：\\server\share\... → \\?\UNC\server\share\...
                if (full.StartsWith(@"\\") && !full.StartsWith(@"\\?\"))
                {
                    // UNC 路径有两个开头的反斜杠，将第一个反斜杠替换为 \\?\UNC
                    return @"\\?\UNC" + full.Substring(1);
                }
                else
                {
                    // 普通盘符路径（如 C:\...）
                    return @"\\?\" + full;
                }
            }
            // 非 Windows 系统原样返回（Linux/macOS 无需处理）
            return path;
        }

        /// <summary>
        /// 检查源文件是否包含 Alpha 通道，优先从统一 Probe 缓存获取。
        /// </summary>
        private async Task<bool> SourceHasAlpha(string filePath)
        {
            // ★ 优先从统一 Probe 缓存获取
            var info = await GetProbeInfoAsync(filePath);
            if (info != null)
            {
                // 同步更新旧缓存
                string normalizedPath = GetNormalizedPathForCache(filePath);
                _srcAlphaCache[normalizedPath] = info.HasAlpha;
                return info.HasAlpha;
            }

            // 兜底：单独探测
            string args = $"-v error -select_streams v:0 -show_entries stream=pix_fmt -of csv=p=0 \"{filePath}\"";
            string raw = await RunProbeAsync(_ffprobePath, args);
            string fmt = raw.Trim().ToLower();
            bool hasAlpha = fmt switch
            {
                "rgba" or "bgra" or "argb" or "abgr" => true,
                "rgba64le" or "bgra64le" => true,
                _ => false
            };
            _srcAlphaCache[GetNormalizedPathForCache(filePath)] = hasAlpha;
            return hasAlpha;
        }



        private readonly ConcurrentDictionary<string, string> _srcPixFmtCache = new();

        /// <summary>
        /// 获取源文件的标准化像素格式（例如 yuv420p、yuv444p10le）
        /// </summary>
        /// <summary>
        /// 获取源文件的标准化像素格式（例如 yuv420p、yuv444p10le）
        /// </summary>
        /// <summary>
        /// 获取源文件的标准化像素格式，高位深 RGB 会保留对应位深（10?bit），灰度映射为 yuv420p
        /// </summary>
        /// <summary>
        /// 获取源文件的标准化像素格式，高位深 RGB 会保留对应位深（10?bit），灰度映射为 yuv420p
        /// </summary>
        /// <summary>
        /// 获取源文件的标准化像素格式，优先使用统一 Probe 缓存，消除重复 ffprobe。
        /// 高位深 RGB 会保留对应位深（10?bit），灰度映射为 yuv420p。
        /// </summary>
        /// <summary>
        /// 获取源文件的标准化像素格式，优先使用统一 Probe 缓存，消除重复 ffprobe。
        /// 高位深 RGB 会保留对应位深（10?bit），灰度映射为 yuv420p。
        /// </summary>
        private async Task<string> GetSourcePixelFormat(string filePath)
        {
            // ★ 优先从统一 Probe 缓存获取
            var info = await GetProbeInfoAsync(filePath);
            if (info != null)
            {
                string fmt = info.PixFmt; // 已经是小写，如 rgba、gray16le 等

                // 填充旧的 Alpha 缓存（如果未填充）
                string normalizedPath = GetNormalizedPathForCache(filePath);
                if (!_srcAlphaCache.ContainsKey(normalizedPath))
                    _srcAlphaCache[normalizedPath] = info.HasAlpha;

                // 像素格式标准化（复用原有逻辑）
                if (fmt == "gray" || fmt.StartsWith("gray"))
                {
                    bool is10bit = fmt.Contains("16") || fmt.Contains("10");
                    fmt = is10bit ? "yuv420p10le" : "yuv420p";
                }
                else if (fmt.Contains("yuvj"))
                {
                    fmt = fmt.Replace("yuvj", "yuv");
                }
                // ★ 修改处：扩展 RGB 格式前缀判断，涵盖 argb、abgr、rgba、bgra 等
                else if (fmt.StartsWith("rgb") || fmt.StartsWith("bgr") || fmt.StartsWith("gbr") ||
                         fmt.StartsWith("argb") || fmt.StartsWith("abgr") || fmt.StartsWith("rgba") || fmt.StartsWith("bgra"))
                {
                    bool is4Comp = fmt.Contains('a') || fmt.Contains('0') || fmt.Contains('x') ||
                                   fmt == "argb" || fmt == "abgr";
                    if (fmt.Contains("64") && !is4Comp) is4Comp = true;

                    int components = is4Comp ? 4 : 3;
                    var match = Regex.Match(fmt, @"(\d+)");
                    int totalBits = 0;
                    if (match.Success && int.TryParse(match.Groups[1].Value, out totalBits))
                    {
                    }
                    if (totalBits == 0) totalBits = components * 8;
                    int perCompBits = totalBits / components;
                    int targetBitDepth = Math.Clamp(perCompBits, 8, 10);

                    string chromaFmt = targetBitDepth >= 10 ? "yuv444p10le" : "yuv444p";
                    if (info.HasAlpha)
                        chromaFmt = chromaFmt.Replace("yuv", "yuva");
                    fmt = chromaFmt;
                }

                if (string.IsNullOrEmpty(fmt)) fmt = "yuv420p";

                // 更新旧的像素格式缓存
                _srcPixFmtCache[normalizedPath] = fmt;
                return fmt;
            }

            // ---- 回退到原有单独探测（理论上不应到达，但作为兜底） ----
            string raw = await RunProbeAsync(_ffprobePath,
                $"-v error -select_streams v:0 -show_entries stream=pix_fmt -of csv=p=0 \"{filePath}\"");
            string fmtFallback = raw.Trim().ToLower();

            // 简单标准化（略去复杂部分以保证程序不崩溃，但建议 probe 正常提供）
            if (fmtFallback == "gray" || fmtFallback.StartsWith("gray"))
                fmtFallback = fmtFallback.Contains("16") || fmtFallback.Contains("10") ? "yuv420p10le" : "yuv420p";
            else if (fmtFallback.Contains("yuvj"))
                fmtFallback = fmtFallback.Replace("yuvj", "yuv");
            else if (fmtFallback.Contains("rgb") || fmtFallback.Contains("bgr"))
                fmtFallback = fmtFallback.Contains("64") ? "yuva444p10le" : "yuva444p"; // 保守假设有 alpha

            if (string.IsNullOrEmpty(fmtFallback)) fmtFallback = "yuv420p";
            _srcPixFmtCache[GetNormalizedPathForCache(filePath)] = fmtFallback;
            return fmtFallback;
        }


        private async Task<string> GetPixelFormatForFileAsync(string filePath, bool isLosslessMode, bool hasAlpha)
        {
            if (isLosslessMode)
            {
                // 无损模式使用 YUV444（数学无损），若源文件有 Alpha 通道则携带 Alpha
                string baseFmt = hasAlpha ? "yuva444p" : "yuv444p";
                return _config.BitDepth >= 10 ? baseFmt + "10le" : baseFmt;
            }

            if (_config.AutoSource)
            {
                string srcFmt = await GetSourcePixelFormat(filePath);
                bool srcIs10bit = srcFmt.EndsWith("10le");
                string baseFmt = srcIs10bit ? srcFmt.Substring(0, srcFmt.Length - 4) : srcFmt;

                // 提取色度采样 (444/422/420)
                string chroma = "420";
                if (baseFmt.Contains("444")) chroma = "444";
                else if (baseFmt.Contains("422")) chroma = "422";

                int targetBitDepth = _config.UserSetBitDepth ? _config.BitDepth : (srcIs10bit ? 10 : 8);

                // 正确生成 yuva / yuv 格式
                string depthSuffix = targetBitDepth >= 10 ? "10le" : "";
                return hasAlpha ? $"yuva{chroma}p{depthSuffix}" : $"yuv{chroma}p{depthSuffix}";
            }
            else
            {
                // 非自适应模式，手动构造
                string baseFmt = _config.PixelFormat ?? "yuv444p10le";
                string depthSuffix = "";
                if (baseFmt.EndsWith("10le"))
                {
                    depthSuffix = "10le";
                    baseFmt = baseFmt.Substring(0, baseFmt.Length - 4);
                }

                string cleanChroma = baseFmt.Replace("a", "");
                string chroma = "420";
                if (cleanChroma.Contains("444")) chroma = "444";
                else if (cleanChroma.Contains("422")) chroma = "422";

                if (_config.UserSetBitDepth)
                {
                    depthSuffix = _config.BitDepth >= 10 ? "10le" : "";
                }

                // 正确生成 yuva / yuv 格式
                return hasAlpha ? $"yuva{chroma}p{depthSuffix}" : $"yuv{chroma}p{depthSuffix}";
            }
        }

        // ========== 无损验证报告 ==========

        /// <summary> 追加一条失败记录到 _failed_verification/failed_verification.csv（线程安全） </summary>
        private void AppendFailedVerificationCsv(FailedVerificationInfo info)
        {
            lock (_failedCsvLock)
            {
                bool writeHeader = !_fs.FileExists(_failedCsvPath);
                if (writeHeader)
                {
                    string header =
                        "SourceFile,FailedOutput,Encoder,EncoderVersion," +
                        "PixelFormat,BitDepth,Width,Height," +
                        "FailureType,MismatchCount,MaxDelta," +
                        "FirstMismatchX,FirstMismatchY,FirstMismatchChannel," +
                        "RefValue,OutValue," +
                        "RMismatches,GMismatches,BMismatches,AMismatches," +
                        "EncodeCommand,Timestamp";
                    _fs.WriteAllText(_failedCsvPath, header + "\n", System.Text.Encoding.UTF8);
                }

                string csvEscape(string? s) =>
                    "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

                string line = string.Join(",",
                    csvEscape(info.SourceFile),
                    csvEscape(info.FailedOutput),
                    csvEscape(info.Encoder),
                    csvEscape(info.EncoderVersion),
                    csvEscape(info.PixelFormat),
                    info.BitDepth,
                    info.Width,
                    info.Height,
                    info.FailureType,
                    info.MismatchCount,
                    info.MaxDelta,
                    info.FirstMismatchX,
                    info.FirstMismatchY,
                    info.FirstMismatchChannel,
                    info.RefValue,
                    info.OutValue,
                    info.RMismatches,
                    info.GMismatches,
                    info.BMismatches,
                    info.AMismatches,
                    csvEscape(info.EncodeCommand),
                    info.Timestamp
                );
                _fs.AppendAllText(_failedCsvPath, line + "\n");
            }
        }

        /// <summary> 写入单文件 JSON 验证报告 </summary>
        private async Task WriteVerificationReportJsonAsync(FailedVerificationInfo info)
        {
            string jsonPath = Path.Combine(
                _failedVerificationDir,
                Path.GetFileNameWithoutExtension(info.FailedOutput) + ".report.json");
            string json = System.Text.Json.JsonSerializer.Serialize(
                info, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
            await _fs.WriteAllTextAsync(jsonPath, json);
        }

        /// <summary>
        /// 检测 ffmpeg 及编码器库版本。
        /// 返回 (ffmpegVersion, encoderVersions) 其中 encoderVersions 的 key 为编码器名。
        /// </summary>
        private static async Task<(string ffmpegVersion, Dictionary<string, string> encoderVersions)>
    GetEncoderVersionsAsync(string ffmpegPath)
        {
            string ffmpegVersion = "";
            var encoderVersions = new Dictionary<string, string>();

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo(ffmpegPath, "-version")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                process.Start();
                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                string output = stdout + stderr;

                // 提取 ffmpeg 版本（第一行）
                var ffmpegMatch = System.Text.RegularExpressions.Regex.Match(
                    output, @"^ffmpeg\s+version\s+([^\s]+)");
                if (ffmpegMatch.Success)
                {
                    ffmpegVersion = ffmpegMatch.Groups[1].Value;
                }

                // 提取各编码器库版本
                var libPatterns = new (string key, string pattern)[]
                {
                    ("libaom-av1", @"libaom-av1\s+([^\s]+)"),
                    ("libsvtav1",  @"libsvtav1\s+([^\s]+)"),
                    ("librav1e",   @"librav1e\s+([^\s]+)"),
                };

                foreach (var (key, pattern) in libPatterns)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(
                        output, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        encoderVersions[key] = m.Groups[1].Value;
                    }
                }
            }
            catch
            {
                // 静默失败，版本信息非关键路径
            }

            return (ffmpegVersion, encoderVersions);
        }





        #endregion
    }

}
