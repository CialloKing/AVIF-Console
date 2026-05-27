using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
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

        private readonly ProgressTracker _progress = new ProgressTracker();

        private readonly IProgress<int>? _guiProgress;   // ★ 新增字段，不与 _progress 冲突

        private readonly ConcurrentDictionary<string, Task<double>> _ssimTasks = new();

        private readonly ICacheManager _cache;


        private readonly SemaphoreSlim _ssimConcurrency;
        private readonly SemaphoreSlim _ffmpegSlots;

        private static readonly object _consoleLock = new();
        private CancellationTokenSource? _globalCts;

        private readonly ConcurrentDictionary<string, Task<QualityMetrics?>> _metricsTasks = new();

        private static void SafeWriteLine(string msg) { lock (_consoleLock) Console.WriteLine(msg); }

        private readonly ConcurrentDictionary<string, bool> _srcAlphaCache = new();

        private readonly int _maxFfmpegConcurrency;

        private int _disposed;

        private readonly IProcessRunner _processRunner;

        private readonly ILogger _logger;



        private readonly PresetConfig.IFileSystem _fs;   // 改为完整限定名

        // 删除原有字段：
        // private HashSet<int> _knownBadCrfs = new HashSet<int>();
        // private Dictionary<int, int> _crfFailCount = new Dictionary<int, int>();

        // 新增文件级隔离字典
        private readonly ConcurrentDictionary<string, FileScopedFailTracker> _failTrackers = new();


        // 记录某文件的某像素格式是否已发生“完全无法写入”的致命错误，用于跳过后续尝试
        // 记录某文件的某像素格式是否已发生“完全无法写入”的致命错误，用于跳过后续尝试
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _fatalFmts = new();


        private readonly ConcurrentQueue<Task> _advancedMetricTasks = new();
        private readonly SemaphoreSlim _advancedMetricSemaphore;

        // 无损验证报告相关
        private readonly object _failedCsvLock = new();
        private string _failedCsvPath = "";
        private string _failedVerificationDir = "";








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
        CancellationToken cancellationToken)
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
            string exe = EncoderUtils.FindExecutable("ssimulacra2.exe") ?? "ssimulacra2.exe";
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
            string exe = EncoderUtils.FindExecutable("butteraugli_main.exe") ?? "butteraugli_main.exe";
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
                    // 直接返回原路径，允许覆盖
                    return candidate;
                case PresetConfig.ConflictStrategy.Skip:
                    // 跳过模式：不重命名，直接返回候选路径（后续会判断跳过）
                    return candidate;
                default: // Rename
                    // 自动追加序号以避免同名冲突
                    if (_fs.FileExists(candidate))
                    {
                        string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
                        string ext = Path.GetExtension(fileName);
                        int counter = 1;
                        do
                        {
                            fileName = $"{nameNoExt}_{counter}{ext}";
                            candidate = Path.Combine(targetDir, fileName);
                            counter++;
                        } while (_fs.FileExists(candidate));
                    }
                    return candidate;
            }
        }

        /// <summary> 外部工具（ffmpeg 等）不接受 \\?\ 长路径，需要剥离 </summary>
        private static string NormalizePathForExternalTool(string path)
        {
            if (OperatingSystem.IsWindows() && path.StartsWith(@"\\?\"))
                return path.Substring(4);
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

            _config = config;
            _ffmpegPath = EncoderUtils.FindExecutable("ffmpeg") ?? throw new Exception("ffmpeg 未找到");
            _ffprobePath = EncoderUtils.FindExecutable("ffprobe") ?? throw new Exception("ffprobe 未找到");
            _processRunner = processRunner ?? new RealProcessRunner();
            _logger = logger;
            _cache = cacheManager ?? new CacheManager();

            bool isHardwareEncoder = !Av1EncoderFactory.Get(config.Encoder).SupportsLossless;
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
            // 归一化各原生指标
            double vmafNorm = m.VMAF / 100.0;
            double psnrNorm = Math.Clamp((m.PSNR_Y - 30) / 20.0, 0, 1);

            // 如果 W?XPSNR 有效，采用五指标加权模型
            if (m.W_XPSNR.HasValue)
            {
                // XPSNR 常见范围 40~60 dB，映射到 0~1
                double xpsnrNorm = Math.Clamp((m.W_XPSNR.Value - 40) / 20.0, 0, 1);
                // 权重分配：VMAF 0.50 + SSIM 0.05 + MS?SSIM 0.08 + PSNR?Y 0.05 + W?XPSNR 0.32 = 1.00
                return 0.50 * vmafNorm + 0.05 * m.SSIM + 0.08 * m.MS_SSIM + 0.05 * psnrNorm + 0.32 * xpsnrNorm;
            }
            // 否则沿用原来的四项指标公式
            return 0.80 * vmafNorm + 0.05 * m.SSIM + 0.10 * m.MS_SSIM + 0.05 * psnrNorm;
        }

        private async Task<string> RunProbeAsync(string file, string args)
        {
            var (exitCode, stdout, stderr) = await _processRunner.RunAsync(
                file, args, TimeSpan.FromSeconds(30), _globalCts?.Token ?? default);
            return stdout;
        }



        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            FinalCleanup();
            _globalCts?.Cancel();
            _globalCts?.Dispose();
            _globalCts = null;
            _ssimConcurrency?.Dispose();
            _ffmpegSlots?.Dispose();
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
            string metricsDir = Path.Combine(workDir, "avif_metrics_tmp");
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
                             $"feature=name=psnr|name=float_ssim|name=float_ms_ssim:model='version=vmaf_float_v0.6.1':log_path={logPathSafe}:log_fmt=json:n_threads=4";
                }
                else
                {
                    filter = $"[0:v][1:v]libvmaf=feature=name=psnr|name=float_ssim|name=float_ms_ssim:" +
                             $"model='version=vmaf_float_v0.6.1':log_path={logPathSafe}:log_fmt=json:n_threads=4";
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

                return new QualityMetrics
                {
                    SSIM = ssim,
                    PSNR_Y = psnr_y,
                    MS_SSIM = ms_ssim,
                    VMAF = vmaf
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
            // 清洗模板，去除可能意外带入的引号、末尾空白
            string template = _config.OutputNameFormat.Trim('"', '\'').Trim();
            string name = Path.GetFileNameWithoutExtension(inputFile);

            string result = template
                .Replace("{name}", name)
                .Replace("{filename}", name)        // 别名
                .Replace("{index}", index.ToString("D2"));

            // 确保扩展名为 .avif，但避免重复追加（例如模板已含 .avif 的情况）
            if (!result.EndsWith(".avif", StringComparison.OrdinalIgnoreCase))
                result += ".avif";

            // 替换所有非法文件名字符（此时引号已经移除，不会有下划线）
            foreach (char c in Path.GetInvalidFileNameChars())
                result = result.Replace(c, '_');

            // 最后再 trim 一下，防止首尾出现空格或控制符
            return result.Trim();
        }



        // ========== 修复后的 RunAsync（统计准确） ==========
        // ==================== 主入口 ====================
        public async Task RunAsync()
        {
            try
            {
                _globalCts = new CancellationTokenSource();
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    SafeWriteLine("\n[WARN] 正在安全停止，请稍候...");
                    _globalCts.Cancel();
                };

                Console.OutputEncoding = Encoding.UTF8;
                _progress.Start(DateTime.Now);

                _logger.LogInfo($"Pipeline started: CRF={_config.BaseCRF} SSIM={_config.TargetSSIM}");

                await PrintStartupInfoAsync();

                var files = await ScanAndPrepareFilesAsync();
                if (files == null || files.Count == 0) return;

                var results = await ProcessInitialBatchAsync(files);
                results = await RetryFailuresAsync(results);

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
            string targetDisplay = GetTargetDisplayString(_config.TargetSSIM, metricMode, _config);

            SafeWriteLine($"编码器: {_config.Encoder}");
            SafeWriteLine($"同时调用ffmpeg编码数: {_maxFfmpegConcurrency}");
            SafeWriteLine($"{crfInfo}  {metricMode}目标: {targetDisplay}  搜索: {_config.UseCRFSearch}  像素格式: {(_config.AutoSource ? "自适应" : (_config.PixelFormat ?? "动态"))}");
            SafeWriteLine($"文件名模板: {_config.OutputNameFormat}");
        }

        // 辅助方法：将内部 0~1 目标值转换为对应模式的原生显示字符串
        private static string GetTargetDisplayString(double targetSSIM, string metricMode, PresetConfig? config = null)
        {
            if (config != null)
            {
                if (metricMode.StartsWith("xpsnr", StringComparison.OrdinalIgnoreCase) && config.XpsnrTargetValue.HasValue)
                    return $"{config.XpsnrTargetValue.Value:F1} dB ({config.XpsnrTargetChannel?.ToUpper() ?? "W"})";

                switch (metricMode.ToLower())
                {
                    case "ssimu2":
                        if (config.Ssimu2TargetValue.HasValue)
                            return config.Ssimu2TargetValue.Value.ToString("F4") + " (SSIMU2)";
                        break;
                    case "butter3":
                        if (config.Butteraugli3TargetValue.HasValue)
                            return config.Butteraugli3TargetValue.Value.ToString("F4") + " (Butter3)";
                        break;
                    case "gmsd":
                        if (config.GmsdTargetValue.HasValue)
                            return config.GmsdTargetValue.Value.ToString("F4") + " (GMSD)";
                        break;
                }
            }

            // 回退到基于 0?1 目标的显示
            switch (metricMode.ToLower())
            {
                case "vmaf":
                    double vmafTarget = targetSSIM * 100;
                    if (Math.Abs(vmafTarget - Math.Round(vmafTarget)) < 0.001)
                        return vmafTarget.ToString("F0");
                    else
                        return vmafTarget.ToString("F1");
                case "psnr":
                    double rawPsnr = targetSSIM * 20 + 30;
                    return rawPsnr.ToString("F1") + " dB";
                case "ssim":
                case "msssim":
                    return targetSSIM.ToString("F4");
                case "mix":
                    return targetSSIM.ToString("F4");
                default:
                    return targetSSIM.ToString("F4");
            }
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

            var extensions = new[]
{
    ".jpg", ".jpeg", ".png", ".webp",
    ".bmp", ".tif", ".tiff", ".gif",
    ".jp2", ".j2k", ".jpx"
};

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

            var processingOrder = sortedFiles
                .OrderByDescending(t => _fs.GetFileLength(t.path))
                .ToList();
            return processingOrder;
        }

        /// <summary> 首次批量处理所有文件 </summary>
        private async Task<List<EncodeResult?>> ProcessInitialBatchAsync(List<(string path, int index)> files)
        {
            var result = await ProcessFilesAsync(files, _config, isRetry: false);
            return result.Select(r => (EncodeResult?)r).ToList();
        }

        /// <summary> 重试失败的文件，并返回合并后的结果列表 </summary>
        /// <summary> 重试失败的文件，并返回合并后的结果列表 </summary>
        private async Task<List<EncodeResult?>> RetryFailuresAsync(List<EncodeResult?> results)
        {
            var failures = results.Where(r => r != null && !r.Success && !r.Skipped).ToList();
            if (failures.Count == 0) return results;

            SafeWriteLine($"\n[RETRY] 开始重试 {failures.Count} 个失败文件...");

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
                SafeWriteLine("? 等待后台高级指标计算完成...");
                try { await Task.WhenAll(_advancedMetricTasks.ToArray()); }
                catch (Exception ex) { _logger.LogError($"后台高级任务异常: {ex.Message}"); }
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
            _logger.LogInfo($"Finished. 成功: {successCount}, 失败: {failCount}, 跳过: {skipCount}, 耗时: {FormatTimeSpan(totalTime)}");


            // 从缓存回填高级指标
            foreach (var r in allResults)
            {
                if (!string.IsNullOrEmpty(r.AdvancedMetricsCacheKey) && _cache.TryGetMetrics(r.AdvancedMetricsCacheKey, out var updated))
                {
                    r.FinalSSIMULACRA2 = updated?.SSIMULACRA2;
                    r.FinalButteraugli_Raw = updated?.Butteraugli_Raw;
                    r.FinalButteraugli_3norm = updated?.Butteraugli_3norm;
                    r.FinalGMSD = updated?.GMSD;
                }
            }

            ExportCsv(allResults);
        }

        /// <summary> 清理编码缓存及临时文件 </summary>
        private void FinalCleanup()
        {
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

            // ★ 新增：清理 ComputeAllMetrics 生成的临时 JSON 目录
            string metricsDir = Path.Combine(Environment.CurrentDirectory, "avif_metrics_tmp");
            if (Directory.Exists(metricsDir))
            {
                try { Directory.Delete(metricsDir, true); } catch { }
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
