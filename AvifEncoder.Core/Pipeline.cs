using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
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

        /// <summary>
        /// 根据目标 VMAF 返回先验中位数及大致搜索范围（基于真实图片统计）
        /// 使用分段线性插值，整数点完全准确，外推采用局部斜率
        /// </summary>
        private static readonly List<(double TargetVmaf, int Median, int Lo, int Hi)> VmafPriorTable = new()
    {
        // 数据使用实际中位数，Lo/Hi 为真实 min/max
        (90, 38, 26, 58),
        (91, 36, 24, 57),
        (92, 34, 20, 56),
        (93, 32, 16, 54),
        (94, 29, 13, 52),
        (95, 25,  9, 49),
        (96, 19,  5, 43),
    };

        public static (int median, int lo, int hi) GetPriorFromVmaf(double targetVmaf)
        {
            // 找到 targetVmaf 所在的区间
            int idx = 0;
            while (idx < VmafPriorTable.Count && VmafPriorTable[idx].TargetVmaf < targetVmaf)
                idx++;

            double median, lo, hi;

            if (idx == 0)
            {
                // 目标小于最小表项，用第1段外推 (0,1)
                median = Extrapolate(targetVmaf, VmafPriorTable[0], VmafPriorTable[1], e => e.Median);
                lo = Extrapolate(targetVmaf, VmafPriorTable[0], VmafPriorTable[1], e => e.Lo);
                hi = Extrapolate(targetVmaf, VmafPriorTable[0], VmafPriorTable[1], e => e.Hi);
            }
            else if (idx == VmafPriorTable.Count)
            {
                // 目标大于最大表项，用最后1段外推 (n-2, n-1)
                var left = VmafPriorTable[^2];
                var right = VmafPriorTable[^1];
                median = Extrapolate(targetVmaf, left, right, e => e.Median);
                lo = Extrapolate(targetVmaf, left, right, e => e.Lo);
                hi = Extrapolate(targetVmaf, left, right, e => e.Hi);
            }
            else
            {
                // 线性插值
                var left = VmafPriorTable[idx - 1];
                var right = VmafPriorTable[idx];
                double t = (targetVmaf - left.TargetVmaf) / (right.TargetVmaf - left.TargetVmaf);
                median = left.Median + t * (right.Median - left.Median);
                lo = left.Lo + t * (right.Lo - left.Lo);
                hi = left.Hi + t * (right.Hi - left.Hi);
            }

            int medianInt = ClampCrf((int)Math.Round(median));
            int loInt = ClampCrf((int)Math.Round(lo));
            int hiInt = ClampCrf((int)Math.Round(hi));

            // 确保 lo ≤ median ≤ hi
            if (loInt > medianInt) loInt = medianInt - 1;
            if (hiInt < medianInt) hiInt = medianInt + 1;

            return (medianInt, loInt, hiInt);
        }

        /// <summary> 使用局部两点斜率进行外推 </summary>
        private static double Extrapolate(double targetVmaf,
    (double TargetVmaf, int Median, int Lo, int Hi) left,
    (double TargetVmaf, int Median, int Lo, int Hi) right,
    Func<(double TargetVmaf, int Median, int Lo, int Hi), int> selector)  // 修正此处类型
        {
            double slope = (selector(right) - selector(left)) / (right.TargetVmaf - left.TargetVmaf);
            double delta = targetVmaf - left.TargetVmaf;
            return selector(left) + slope * delta;
        }

        // ClampCrf 保持不变
        private static int ClampCrf(int value) => Math.Clamp(value, 0, 63);







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







        /// <summary> 每次开始新的搜索时重置失败跟踪状态 </summary>

        private static string GetRowMtArg(PresetConfig cfg)
        {
            if (!EncoderUtils.IsLibAom(cfg.Encoder))
                return "";
            return cfg.SerialEncode ? "-row-mt 0" : "-row-mt 1";
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

        private sealed class FileScopedFailTracker
        {
            public HashSet<int> KnownBadCrfs { get; } = new();
            public Dictionary<int, int> CrfFailCount { get; } = new();
            public const int HardFailThreshold = 2;
            public const int AvoidRadius = 2;

            public void Reset()
            {
                KnownBadCrfs.Clear();
                CrfFailCount.Clear();
            }

            public bool IsBlacklisted(int crf)
            {
                for (int offset = -AvoidRadius; offset <= AvoidRadius; offset++)
                    if (KnownBadCrfs.Contains(crf + offset))
                        return true;
                return false;
            }

            public void RecordFailedAttempt(int crf)
            {
                CrfFailCount.TryGetValue(crf, out int count);
                count++;
                CrfFailCount[crf] = count;
                if (count >= HardFailThreshold)
                    KnownBadCrfs.Add(crf);
            }

            public void ClearCrf(int crf) => CrfFailCount.Remove(crf);

            public int FindSafeCrfInInterval(int center, int xMin, int xMax)
            {
                for (int offset = 0; offset <= xMax - xMin; offset++)
                {
                    int tryCrf = center + offset;
                    if (tryCrf >= xMin && tryCrf <= xMax && !IsBlacklisted(tryCrf))
                        return tryCrf;
                    tryCrf = center - offset;
                    if (tryCrf >= xMin && tryCrf <= xMax && !IsBlacklisted(tryCrf))
                        return tryCrf;
                }
                return -1;
            }
        }

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

            bool isHardwareEncoder = !EncoderUtils.IsSoftwareEncoder(config.Encoder);
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

        }

        /// <summary> 判断编码器是否支持 -still-picture 1 参数（AVIF 单帧静止图像标志） </summary>
        private static bool EncoderSupportsStillPicture(string encoderName) => EncoderUtils.SupportsStillPicture(encoderName);

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

        /// <summary>
        /// 根据编码器名称返回专用的命令行参数片段（速度控制、分块等），
        /// 替代原先固定的 -cpu-used / -row-mt。
        /// </summary>
        private static string BuildEncoderSpecificArgs(PresetConfig cfg, int cpuUsed, string tilePart, string rowMt)
        {
            string enc = cfg.Encoder;

            if (EncoderUtils.IsLibAom(enc))
            {
                return $"-cpu-used {cpuUsed} {tilePart} {rowMt}";
            }

            if (EncoderUtils.IsSvtAv1(enc))
            {
                // SVT-AV1 的 preset 范围 0-13，0 最快、13 最慢
                // cpuUsed 语义统一为“数值越大越快，0 最慢”，因此反转
                int maxSvtPreset = 13;
                int svtPreset = Math.Clamp(maxSvtPreset - cpuUsed, 0, maxSvtPreset);
                if (cfg.Lossless)
                {
                    return $"-preset {svtPreset} {tilePart}";
                }
                else
                {
                    string svtParams = "tune=3:keyint=1:avif=1:film-grain=0:enable-qm=1:qm-min=0:qm-max=8";
                    return $"-preset {svtPreset} -svtav1-params \"{svtParams}\" {tilePart}";
                }
            }

            if (EncoderUtils.IsRav1e(enc))
            {
                return $"-speed {cpuUsed} {tilePart}";
            }


            // 硬件编码器：无统一速度参数，保留空字符串使用 ffmpeg 默认行为
            return "";
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




        private class NaturalComparer : IComparer<string>
        {
            public int Compare(string? x, string? y)
            {
                if (x == y) return 0;
                if (x == null) return -1;
                if (y == null) return 1;
                int xi = 0, yi = 0;
                while (xi < x.Length && yi < y.Length)
                {
                    if (char.IsDigit(x[xi]) && char.IsDigit(y[yi]))
                    {
                        int xn = 0, yn = 0;
                        while (xi < x.Length && char.IsDigit(x[xi])) { xn = xn * 10 + (x[xi] - '0'); xi++; }
                        while (yi < y.Length && char.IsDigit(y[yi])) { yn = yn * 10 + (y[yi] - '0'); yi++; }
                        if (xn != yn) return xn.CompareTo(yn);
                    }
                    else
                    {
                        if (x[xi] != y[yi]) return x[xi].CompareTo(y[yi]);
                        xi++; yi++;
                    }
                }
                return (x.Length - xi).CompareTo(y.Length - yi);
            }
        }






        public static PresetConfig CreateFromPreset(CliPreset preset)
        {
            return preset switch
            {
                CliPreset.Fast => new PresetConfig { BaseCRF = 38, TargetSSIM = 0.91, UseCRFSearch = false },
                CliPreset.Balanced => new PresetConfig { BaseCRF = 36, TargetSSIM = 0.97, UseCRFSearch = true },
                CliPreset.Best => new PresetConfig { BaseCRF = 34, TargetSSIM = 0.97, UseCRFSearch = true },
                CliPreset.Extreme => new PresetConfig { BaseCRF = 35, TargetSSIM = 0.99, UseCRFSearch = true },
                _ => throw new ArgumentOutOfRangeException(nameof(preset))
            };
        }


        private static string Sha256(string text)
        {
            using var sha = SHA256.Create();
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            return Convert.ToHexString(hash)[..16];
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

        // ==================== 辅助方法 ====================

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

        private async Task<IEnumerable<EncodeResult>> ProcessFilesAsync(
    List<(string filePath, int index)> files, PresetConfig config, bool isRetry)
        {
            if (config.SweepMode)
            {
                int crfCount = config.MaxCRF - config.MinCRF + 1;
                if (crfCount <= 0) crfCount = 1;
                int totalTasks = files.Count * crfCount;
                _progress.SetTotalFiles(totalTasks);   // 遍历模式下总进度 = 图片数 × CRF 范围
                return await ProcessFilesSweepAsync(files, config);
            }
            var results = new ConcurrentDictionary<int, EncodeResult>();
            var semaphore = new SemaphoreSlim(config.MaxJobs);
            var tasks = files.Select(async file =>
                {
                    try
                    {
                        bool acquired = await semaphore.WaitAsync(TimeSpan.FromMinutes(720), _globalCts?.Token ?? default);
                        if (!acquired)
                        {
                            _logger.LogInfo($"任务信号量获取超时，跳过文件: {Path.GetFileName(file.filePath)}");
                            // 信号量超时
                            var failResult = new EncodeResult
                            {
                                Index = file.index,
                                FileName = GetOutputFileName(file.filePath, file.index),
                                OriginalFileName = Path.GetFileName(file.filePath),
                                InputPath = file.filePath,                     // ★ 新增
                                Success = false,
                                Skipped = false,
                                ErrorMessage = "任务信号量获取超时",
                                TotalTime = TimeSpan.Zero
                            };
                            results[file.index] = failResult;
                            MarkProcessed(failResult);
                            return;
                        }
                        try
                        {
                            var r = await ProcessSingleFileAsync(file.filePath, file.index, config, isRetry);
                            if (r != null) results[r.Index] = r;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogInfo($"文件处理异常: {file.filePath} - {ex.Message}");
                            // 异常处理中的 failResult
                            var failResult = new EncodeResult
                            {
                                Index = file.index,
                                FileName = GetOutputFileName(file.filePath, file.index),
                                OriginalFileName = Path.GetFileName(file.filePath),
                                InputPath = file.filePath,
                                Success = false,
                                Skipped = false,
                                ErrorMessage = $"异常: {ex.Message}",
                                TotalTime = TimeSpan.Zero
                            };
                            results[file.index] = failResult;
                            MarkProcessed(failResult);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // ★ 修复 Bug3：全局取消时优雅退出，记录取消结果
                        _logger.LogInfo($"操作取消，跳过文件: {Path.GetFileName(file.filePath)}");
                        // 取消操作
                        var cancelResult = new EncodeResult
                        {
                            Index = file.index,
                            FileName = GetOutputFileName(file.filePath, file.index),
                            OriginalFileName = Path.GetFileName(file.filePath),
                            InputPath = file.filePath,
                            Success = false,
                            Skipped = false,
                            ErrorMessage = "用户取消操作",
                            TotalTime = TimeSpan.Zero
                        };
                        results[file.index] = cancelResult;
                        MarkProcessed(cancelResult);
                    }
                });
            await Task.WhenAll(tasks);
            return results.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value);
        }


        /// <summary> 遍历模式：对每个输入文件在 MinCRF～MaxCRF 范围内生成多个 AVIF 并保存完整指标 </summary>
        /// <summary> 遍历模式：对每个输入文件在 MinCRF～MaxCRF 范围内生成多个 AVIF 并保存完整指标（文件按顺序串行） </summary>
        private async Task<IEnumerable<EncodeResult>> ProcessFilesSweepAsync(
    List<(string filePath, int index)> files, PresetConfig config)
        {
            var results = new ConcurrentBag<EncodeResult>();

            // 文件级串行：依次处理每个文件
            foreach (var file in files)
            {
                string inputPath = file.filePath;
                string name = Path.GetFileName(inputPath);

                // 1. 准备编码基础信息（复用缓存）
                var encInfo = await PrepareEncodingInfoAsync(inputPath, config);
                if (encInfo == null)
                {
                    _logger.LogInfo($"跳过 {name}：无法获取编码信息");
                    continue;
                }

                // 2. 预缩放（如果需要）
                var scaling = await HandlePreScalingAsync(inputPath, config, name);
                string workingInput = scaling.WorkingPath;

                // 3. 为当前文件创建所有 CRF 任务，用信号量控制文件内并发
                var semaphore = new SemaphoreSlim(config.MaxJobs);
                var crfTasks = new List<Task>();

                for (int crf = config.MinCRF; crf <= config.MaxCRF; crf++)
                {
                    int capturedCrf = crf;
                    var task = Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            var startTime = DateTime.Now;

                            // 生成输出路径
                            string baseOutput = GetOutputPath(inputPath, file.index);
                            string dir = Path.GetDirectoryName(baseOutput)!;
                            string baseName = Path.GetFileNameWithoutExtension(baseOutput);
                            string outputPath = Path.Combine(dir, $"{baseName}_CRF{capturedCrf}.avif");

                            // 编码（内部会等待 _ffmpegSlots）
                            (bool ok, TimeSpan t, int retries, string error, bool fromCache,
                             string? actualAom, string? cmd) =
                                await EncodeToFileExAsync(workingInput, outputPath, capturedCrf,
                                    encInfo.TileCols, config.FinalCpuUsed, config,
                                    IsJpeg(workingInput), encInfo.ActualPixFmt, encInfo.IsTrulyLossless,
                                    config.EncodeTimeoutMinutes > 0 ? config.EncodeTimeoutMinutes : 30,
                                    allowParamDegrade: true);

                            if (!ok)
                            {
                                var failResult = new EncodeResult
                                {
                                    Index = file.index * 1000 + capturedCrf,
                                    FileName = Path.GetFileName(outputPath),
                                    OriginalFileName = name,
                                    InputPath = inputPath,
                                    UsedCRF = capturedCrf,
                                    Success = false,
                                    ErrorMessage = error,
                                    TotalTime = DateTime.Now - startTime,
                                    PixelFormat = encInfo.ActualPixFmt,
                                };
                                MarkProcessed(failResult);
                                results.Add(failResult);
                                return;
                            }

                            // 质量指标计算（忽略第三个返回值 cacheKey）
                            (double ssim, QualityMetrics? metrics, _) = await EvaluateFinalQualityAsync(
                                workingInput, outputPath,
                                new FinalEncodeResult
                                {
                                    Success = true,
                                    Crf = capturedCrf,
                                    ActualPixFmt = encInfo.ActualPixFmt,
                                    EncodeTime = t,
                                    Retries = retries,
                                    FromCache = fromCache,
                                    FinalCommand = cmd,
                                    UseSafeMode = false,
                                    ActualAom = actualAom ?? config.GetEffectiveAomParams()
                                },
                                encInfo,
                                new CRFSearchResult
                                {
                                    Crf = capturedCrf,
                                    ActualPixFmt = encInfo.ActualPixFmt,
                                    SearchBasedCRF = false,
                                    UseSafeModeFinalEncode = false,
                                    SearchEvalCount = 0
                                },
                                config
                            );

                            var result = new EncodeResult
                            {
                                Index = file.index * 1000 + capturedCrf,
                                FileName = Path.GetFileName(outputPath),
                                OriginalFileName = name,
                                InputPath = inputPath,
                                OriginalSize = _fs.FileExists(inputPath) ? _fs.GetFileLength(inputPath) : 0,
                                OutputSize = _fs.FileExists(outputPath) ? _fs.GetFileLength(outputPath) : 0,
                                UsedCRF = capturedCrf,
                                FinalSSIM = ssim,
                                EncodeTime = t,
                                SearchTime = TimeSpan.Zero,
                                TotalTime = DateTime.Now - startTime,
                                Retries = retries,
                                Success = true,
                                PixelFormat = encInfo.ActualPixFmt,
                                SourcePixelFormat = encInfo.SourcePixFmt,
                                Mode = config.AutoSource ? "自适应" : "手动",
                                IsSafeMode = false,
                                CacheReused = fromCache,
                                CommandLine = cmd,
                                FinalVMAF = metrics?.VMAF,
                                FinalPSNR_Y = metrics?.PSNR_Y,
                                FinalMSSSIM = metrics?.MS_SSIM,
                                FinalMixScore = metrics != null ? ComputeMixScore(metrics) : null,
                                FinalXPSNR_Y = metrics?.XPSNR_Y,
                                FinalXPSNR_U = metrics?.XPSNR_U,
                                FinalXPSNR_V = metrics?.XPSNR_V,
                                FinalWXPSNR = metrics?.W_XPSNR,
                                FinalSSIMULACRA2 = metrics?.SSIMULACRA2,
                                FinalButteraugli_Raw = metrics?.Butteraugli_Raw,
                                FinalButteraugli_3norm = metrics?.Butteraugli_3norm,
                                FinalGMSD = metrics?.GMSD,
                                SearchEvaluations = 0
                            };
                            MarkProcessed(result);
                            results.Add(result);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, _globalCts?.Token ?? CancellationToken.None);

                    crfTasks.Add(task);
                }

                // 4. 等待当前文件的所有 CRF 任务完成后，清理临时缩放文件，再处理下一个文件
                await Task.WhenAll(crfTasks);
                semaphore.Dispose();
                if (scaling.TempFilePath != null)
                    try { _fs.DeleteFile(scaling.TempFilePath); } catch { }
            }

            return results.OrderBy(r => r.Index).ToList();
        }














        private static bool IsJpeg(string path) =>
                Path.GetExtension(path).ToLower() is ".jpg" or ".jpeg";


        public static void ApplyBitDepth(PresetConfig cfg)
        {
            if (cfg.PixelFormat == null) return;
            if (cfg.BitDepth == 8)
            {
                if (cfg.PixelFormat.EndsWith("10le"))
                    cfg.PixelFormat = cfg.PixelFormat.Substring(0, cfg.PixelFormat.Length - 4);
            }
            else // 10
            {
                // ★ 移除无意义的 rgb24 判断，仅根据后缀决定是否添加 10le
                if (!cfg.PixelFormat.EndsWith("10le"))
                    cfg.PixelFormat += "10le";
            }
        }


        // ==================== 主调度方法 ====================
        // ==================== 主调度方法 ====================
        private async Task<EncodeResult?> ProcessSingleFileAsync(string inputPath, int index, PresetConfig config, bool isRetry)
        {
            string name = Path.GetFileName(inputPath);
            string outputPath = GetOutputPath(inputPath, index);   // ★ 使用新方法保持子目录结构
            var fileStartTime = DateTime.Now;

            // ---- 预缩放 ----
            var scaling = await HandlePreScalingAsync(inputPath, config, name);
            try
            {
                string workingInputPath = scaling.WorkingPath;
                if (scaling.WasScaled)
                    _logger.LogInfo($"预缩放: {name} {scaling.OriginalWidth}x{scaling.OriginalHeight} -> {scaling.ScaledWidth}x{scaling.ScaledHeight}");

                // 跳过已存在
                var skipResult = await TrySkipExistingOutputAsync(inputPath, index, config, isRetry);
                if (skipResult != null) return skipResult;

                _logger.LogInfo($"开始: {name}");

                // 准备编码信息
                var encInfo = await PrepareEncodingInfoAsync(workingInputPath, config);
                if (encInfo == null)
                    return FailResult(index, Path.GetFileName(outputPath), name,
                                      inputPath, "无法获取分辨率", fileStartTime);

                SafeWriteLine($"[START] {name} [{encInfo.PixInfo}]");

                // 搜索 + 最终编码
                var searchResult = await RunCRFSearchAsync(workingInputPath, config, encInfo, name);
                string finalEncodeInput = (scaling.WasScaled && !config.ApplyScalingToOutput) ? inputPath : workingInputPath;
                var encodeResult = await PerformFinalEncodeAsync(finalEncodeInput, outputPath, config, encInfo, searchResult);

                // 计算最终质量
                (double ssim, QualityMetrics? metrics, string _) = await EvaluateFinalQualityAsync(
                 workingInputPath, outputPath, encodeResult, encInfo, searchResult, config);

                // 组装结果（第三个参数是纯文件名，用于显示）
                return BuildResult(index, Path.GetFileName(outputPath), name,
                                       inputPath, outputPath,
                                       encodeResult, searchResult, encInfo, ssim, metrics, fileStartTime);
            }
            finally
            {
                if (scaling.TempFilePath != null)
                    try { _fs.DeleteFile(scaling.TempFilePath); } catch { }
            }
        }

        private record PreScalingResult(
    string WorkingPath, bool WasScaled, string? TempFilePath,
    int OriginalWidth, int OriginalHeight, int ScaledWidth, int ScaledHeight);

        private async Task<PreScalingResult> HandlePreScalingAsync(string inputPath, PresetConfig config, string name)
        {
            if (config.MaxResolution <= 0)
                return new PreScalingResult(inputPath, false, null, 0, 0, 0, 0);

            var (srcW, srcH) = await GetResolutionAsync(inputPath);
            int longSide = Math.Max(srcW, srcH);
            if (longSide <= config.MaxResolution)
                return new PreScalingResult(inputPath, false, null, srcW, srcH, srcW, srcH);

            string scaledDir = Path.Combine(_outputDir, "_scaled");
            _fs.CreateDirectory(scaledDir);
            string scaledFile = Path.Combine(scaledDir, $"_scaled_{Guid.NewGuid():N}.png");
            await ScaleImageAsync(inputPath, scaledFile, config.MaxResolution);
            var (sw, sh) = await GetResolutionAsync(scaledFile);
            return new PreScalingResult(scaledFile, true, scaledFile, srcW, srcH, sw, sh);
        }


        /// <summary>
        /// 评估最终编码质量：先从缓存取，若无则计算 VMAF/XPSNR/高级指标，
        /// 并自动清洗被尾部污染的 PNG 源文件以保证 SSIMULACRA2/Butteraugli 正常。
        /// </summary>
        /// <summary>
        /// 评估最终编码质量：先从缓存取，若无则计算 VMAF/XPSNR/高级指标，
        /// 并自动清洗被尾部污染的 PNG 源文件以保证 SSIMULACRA2/Butteraugli 正常。
        /// </summary>
        private async Task<(double ssim, QualityMetrics? metrics, string cacheKey)> EvaluateFinalQualityAsync(
            string workingInputPath, string outputPath, FinalEncodeResult encodeResult,
            EncodingInfo encInfo, CRFSearchResult searchResult, PresetConfig config)
        {
            if (!encodeResult.Success)
                return (0, null, "");

            string normalizedInput = GetNormalizedPathForCache(workingInputPath);
            string cleanPixFmt = encodeResult.ActualPixFmt?.Replace("a", "") ?? "";
            int actualDepth = encodeResult.ActualPixFmt?.Contains("10le") == true ? 10 : 8;
            string aomParams = config.GetEffectiveAomParams();
            bool jpeg = IsJpeg(workingInputPath);
            int tileCols = encInfo.TileCols;
            int cpuUsed = searchResult.UseSafeModeFinalEncode ? 0 : config.FinalCpuUsed;
            var (keyW, keyH) = await GetResolutionAsync(workingInputPath);
            string rowMtArg = GetRowMtArg(config);
            string cacheKey = GetSsimCacheKey(normalizedInput, encodeResult.Crf, cleanPixFmt, tileCols,
                                              cpuUsed, jpeg, aomParams, actualDepth, keyW, keyH, rowMtArg);

            // ---------- 缓存命中 ----------
            // ---------- 缓存命中 ----------
            // ---------- 缓存命中 ----------
            if (_cache.TryGetMetrics(cacheKey, out QualityMetrics? cachedMetrics))
            {
                _logger.LogSearch($"最终指标复用缓存: CRF={encodeResult.Crf} VMAF={cachedMetrics!.VMAF:F4}");

                bool needUpdate = false;

                // 补算缺失的 XPSNR
                if (!cachedMetrics.XPSNR_Y.HasValue || !cachedMetrics.XPSNR_U.HasValue ||
                    !cachedMetrics.XPSNR_V.HasValue || !cachedMetrics.W_XPSNR.HasValue)
                {
                    try
                    {
                        var (y, u, v, weighted) = await ComputeXPSNRAsync(workingInputPath, outputPath, "yuv444p");
                        cachedMetrics.XPSNR_Y = y;
                        cachedMetrics.XPSNR_U = u;
                        cachedMetrics.XPSNR_V = v;
                        cachedMetrics.W_XPSNR = weighted;
                        needUpdate = true;
                        _logger.LogInfo($"XPSNR 补算完成: Y={y?.ToString("F4")}, U={u?.ToString("F4")}, V={v?.ToString("F4")}, W={weighted?.ToString("F4")}");
                    }
                    catch (Exception ex) { _logger.LogInfo($"XPSNR 补算异常，将留空: {ex.Message}"); }
                }

                // 补算缺失的高级指标（异步后台执行）
                bool advancedUpdated = false;
                {
                    bool needSsimu2 = !cachedMetrics.SSIMULACRA2.HasValue;
                    bool needButter = !cachedMetrics.Butteraugli_3norm.HasValue || !cachedMetrics.Butteraugli_Raw.HasValue;
                    bool needGmsd = !cachedMetrics.GMSD.HasValue;

                    if (needSsimu2 || needButter || needGmsd)
                    {
                        var bgTask = ComputeAdvancedMetricsInBackgroundAsync(
                            workingInputPath, outputPath, _outputDir, cacheKey,
                            needSsimu2, needButter, needGmsd,
                            _globalCts?.Token ?? CancellationToken.None);
                        _advancedMetricTasks.Enqueue(bgTask);
                        advancedUpdated = true;
                    }
                }

                if (needUpdate || advancedUpdated)
                {
                    _cache.SetMetrics(cacheKey, cachedMetrics);
                    _logger.LogInfo(
                        $"缓存指标补充: " +
                        $"SSIMULACRA2={cachedMetrics.SSIMULACRA2?.ToString("F4")}, " +
                        $"Butteraugli={cachedMetrics.Butteraugli_Raw?.ToString("F4")}/{cachedMetrics.Butteraugli_3norm?.ToString("F4")}, " +
                        $"GMSD={cachedMetrics.GMSD?.ToString("F4")}, " +
                        $"XPSNR Y={cachedMetrics.XPSNR_Y?.ToString("F4")}, W={cachedMetrics.W_XPSNR?.ToString("F4")}");
                }

                return (cachedMetrics.SSIM, cachedMetrics, cacheKey);

            }

            // ---------- 全新计算 ----------
            QualityMetrics? metrics = null;
            try
            {
                metrics = await ComputeAllMetricsAsync(workingInputPath, outputPath);
            }
            catch (Exception ex) { _logger.LogError($"多指标计算异常: {ex.Message}"); }

            if (metrics != null)
            {
                // XPSNR
                try
                {
                    var (y, u, v, weighted) = await ComputeXPSNRAsync(workingInputPath, outputPath, "yuv444p");
                    metrics.XPSNR_Y = y;
                    metrics.XPSNR_U = u;
                    metrics.XPSNR_V = v;
                    metrics.W_XPSNR = weighted;
                    _logger.LogInfo($"XPSNR 计算完成: Y={y?.ToString("F4")}, U={u?.ToString("F4")}, V={v?.ToString("F4")}, W={weighted?.ToString("F4")}");
                }
                catch (Exception ex) { _logger.LogInfo($"XPSNR 计算异常，将留空: {ex.Message}"); }

                // 高级指标（改为异步后台执行）
                bool advancedUpdated = false;
                {
                    bool needSsimu2 = !metrics.SSIMULACRA2.HasValue;
                    bool needButter = !metrics.Butteraugli_3norm.HasValue || !metrics.Butteraugli_Raw.HasValue;
                    bool needGmsd = !metrics.GMSD.HasValue;

                    if (needSsimu2 || needButter || needGmsd)
                    {
                        var bgTask = ComputeAdvancedMetricsInBackgroundAsync(
                            workingInputPath, outputPath, _outputDir, cacheKey,
                            needSsimu2, needButter, needGmsd,
                            _globalCts?.Token ?? CancellationToken.None);
                        _advancedMetricTasks.Enqueue(bgTask);
                    }
                }

                _cache.SetMetrics(cacheKey, metrics);
                if (advancedUpdated)
                    _logger.LogInfo($"高级指标补充: SSIMULACRA2={metrics.SSIMULACRA2?.ToString("F4")}, Butteraugli={metrics.Butteraugli_Raw?.ToString("F4")}/{metrics.Butteraugli_3norm?.ToString("F4")}, GMSD={metrics.GMSD?.ToString("F4")}");

                return (metrics.SSIM, metrics, cacheKey);
            }

            // 回退 SSIM 单一缓存
            if (_cache.TryGetSSIM(cacheKey, out double cachedSsim) && cachedSsim >= 0)
                return (cachedSsim, null, cacheKey);

            double ssim = await CalcSSIMAsync(workingInputPath, outputPath, encodeResult.ActualPixFmt);
            if (ssim >= 0) _cache.SetSSIM(cacheKey, ssim);

            return (ssim, null, cacheKey);
        }


        private EncodeResult FailResult(int index, string outputFileName, string name,
                                    string inputPath, string error, DateTime fileStartTime)
        {
            var result = new EncodeResult
            {
                Index = index,
                FileName = outputFileName,
                OriginalFileName = name,
                InputPath = inputPath,                  // ★ 记录原始输入路径
                Success = false,
                ErrorMessage = error,
                TotalTime = DateTime.Now - fileStartTime
            };
            MarkProcessed(result);
            return result;
        }

        private EncodeResult BuildResult(int index, string outputFileName, string name,
string inputPath, string outputPath,
FinalEncodeResult encodeResult, CRFSearchResult searchResult,
EncodingInfo encInfo, double ssim, QualityMetrics? metrics, DateTime fileStartTime, string? advancedCacheKey = null)
        {
            // ★ 检测是否是“搜索已失败且最终被迫使用 CRF=0（MinCRF=0）”的情景
            bool crf0Unreachable = encodeResult.Success
                                   && !searchResult.SearchBasedCRF
                                   && searchResult.Crf == 0
                                   && _config.MinCRF == 0;

            var result = new EncodeResult
            {
                Index = index,
                FileName = outputFileName,
                OriginalFileName = name,
                InputPath = inputPath,
                OriginalSize = encodeResult.Success ? _fs.GetFileLength(inputPath) : 0,
                OutputSize = encodeResult.Success ? _fs.GetFileLength(outputPath) : 0,
                UsedCRF = encodeResult.Success ? encodeResult.Crf : -1,
                FinalSSIM = ssim,
                EncodeTime = encodeResult.EncodeTime,
                SearchTime = searchResult.SearchTime,
                TotalTime = DateTime.Now - fileStartTime,
                Retries = encodeResult.Retries,
                // 若 CRF=0 仍不达标，即便编码过程成功了也标记为失败
                Success = encodeResult.Success && !crf0Unreachable,
                ErrorMessage = crf0Unreachable
                    ? "CRF=0 仍无法达到质量目标，编码已用最佳质量但未达标"
                    : encodeResult.FailReason,
                PixelFormat = encodeResult.Success ? encodeResult.ActualPixFmt : "",
                SourcePixelFormat = encInfo.SourcePixFmt,
                Mode = _config.AutoSource ? "自适应" : "手动",
                IsSafeMode = encodeResult.UseSafeMode,
                AomParamsUsed = encodeResult.ActualAom ?? "",
                CacheReused = encodeResult.FromCache,
                CommandLine = encodeResult.FinalCommand ?? "",
                FinalVMAF = metrics?.VMAF,
                FinalPSNR_Y = metrics?.PSNR_Y,
                FinalMSSSIM = metrics?.MS_SSIM,
                FinalMixScore = metrics == null ? null : ComputeMixScore(metrics),

                // ---- 新增：XPSNR 分数 ----
                FinalXPSNR_Y = metrics?.XPSNR_Y,
                FinalXPSNR_U = metrics?.XPSNR_U,
                FinalXPSNR_V = metrics?.XPSNR_V,
                FinalWXPSNR = metrics?.W_XPSNR,

                FinalSSIMULACRA2 = metrics?.SSIMULACRA2,
                FinalButteraugli_Raw = metrics?.Butteraugli_Raw,
                FinalButteraugli_3norm = metrics?.Butteraugli_3norm,
                FinalGMSD = metrics?.GMSD,

                SearchEvaluations = searchResult.SearchEvalCount
            };
            result.AdvancedMetricsCacheKey = advancedCacheKey;
            MarkProcessed(result);
            return result;
        }

        // ==================== 辅助数据类 ====================
        private class EncodingInfo
        {
            public string SourcePixFmt { get; set; } = "";
            public string ActualPixFmt { get; set; } = "";
            public string PixInfo { get; set; } = "";
            public int Width { get; set; }
            public int Height { get; set; }
            public bool IsTrulyLossless { get; set; }
            public bool IsLosslessMode { get; set; }
            public int TileCols { get; set; }
            public int BaseCrf { get; set; }

            // ★ 新增
            public bool HasAlpha { get; set; } = false;
        }

        private class CRFSearchResult
        {
            public int Crf;
            public string ActualPixFmt = "";
            public TimeSpan SearchTime;
            public bool SearchBasedCRF;
            public bool UseSafeModeFinalEncode;
            public int SearchEvalCount;    // ★ 新增：搜索评估次数
        }

        private class FinalEncodeResult
        {
            public bool Success;
            public int Crf;
            public string ActualPixFmt = "";
            public TimeSpan EncodeTime;
            public int Retries;
            public string FailReason = "";
            public bool FromCache;
            public string? ActualAom;
            public string? FinalCommand;
            public bool UseSafeMode;
            public DateTime StartTime;
        }

        // ==================== 辅助方法 ====================

        // 1. 跳过已存在文件
        private async Task<EncodeResult?> TrySkipExistingOutputAsync(string inputPath, int index, PresetConfig config, bool isRetry)
        {
            if (isRetry) return null;

            string outputPath = GetOutputPath(inputPath, index);
            if (_fs.FileExists(outputPath))
            {
                // 覆盖模式：不跳过，继续编码（覆盖旧文件）
                if (config.FileConflictStrategy == PresetConfig.ConflictStrategy.Overwrite)
                    return null;

                // 跳过模式：直接返回已存在的文件信息
                string name = Path.GetFileName(inputPath);
                SafeWriteLine($"[SKIP] {name} (已存在，跳过)");
                _logger.LogInfo($"跳过: {name}");
                var skipResult = new EncodeResult
                {
                    Index = index,
                    FileName = Path.GetFileName(outputPath),
                    OriginalFileName = name,
                    InputPath = inputPath,
                    OriginalSize = _fs.GetFileLength(inputPath),
                    OutputSize = _fs.GetFileLength(outputPath),
                    Skipped = true
                };
                MarkProcessed(skipResult);
                return skipResult;
            }
            return null;
        }

        // 2. 准备编码基础信息
        // 2. 准备编码基础信息
        private async Task<EncodingInfo?> PrepareEncodingInfoAsync(string inputPath, PresetConfig config)
        {
            string name = Path.GetFileName(inputPath);
            bool isLosslessMode = config.Lossless;
            bool isTrulyLossless = isLosslessMode;   // ★ 已修改
            string srcFmt = await GetSourcePixelFormat(inputPath);
            bool hasAlpha = await SourceHasAlpha(inputPath);
            string actualPixFmt = await GetPixelFormatForFileAsync(inputPath, isLosslessMode, hasAlpha);
            // ===== 补全缺失的 pixInfo、w、h =====
            string pixInfo;
            if (config.AutoSource && !isLosslessMode)
                pixInfo = $"源: {srcFmt} -> 输出: {actualPixFmt}";
            else
                pixInfo = actualPixFmt;
            var (w, h) = await GetResolutionAsync(inputPath);
            if (w == 0 || h == 0) return null;

            // 硬件编码器 Alpha 警告等保持原有逻辑
            bool alphaDropped = false;
            if (hasAlpha && !config.Encoder.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
            {
                hasAlpha = false;
                alphaDropped = true;
                actualPixFmt = actualPixFmt.Replace("a", "");
                SafeWriteLine($" [WARN] [{name}] 硬件编码器不支持 Alpha 通道，透明度将被丢弃");
                _logger.LogInfo($"Alpha 通道丢弃: {name}，编码器 {config.Encoder} 不支持 yuva 格式");
            }

            // 硬件编码器色度采样警告（原有逻辑）
            if (!config.Encoder.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
            {
                bool is420 = actualPixFmt.Contains("420");
                if (!is420)
                {
                    SafeWriteLine($" [WARN] [{name}] 硬件编码器通常只支持 4:2:0，程序将自动尝试降级。");
                }
            }

            // ─── 计算合法的 tileCols ───
            // 在 PrepareEncodingInfoAsync 中，替换 tileCols 计算部分：
            int tileCols = 0;
            if (!isTrulyLossless)
            {
                // 基础性能推荐值（单核时设为 0，避免强制分块）
                tileCols = Environment.ProcessorCount > 1
                           ? Math.Clamp((int)Math.Log2(Environment.ProcessorCount), 1, 4)
                           : 0;
                // 小图保护（任何一边小于256）
                if (tileCols > 0 && (w < 256 || h < 256))
                    tileCols = 0;
                // 合法性强制约束：tile 宽度不能超过 4096
                int minLegalCols = GetMinLegalTileCols(w);
                // ★ 合法性强制约束：tile 宽度不能小于 256（libaom 实现限制）
                int maxLegalCols = GetMaxLegalTileCols(w);

                if (minLegalCols > maxLegalCols)   // 图像太小，无法满足任何 tile 要求
                    tileCols = 0;
                else
                    tileCols = Math.Clamp(tileCols, minLegalCols, maxLegalCols);
            }
            // ★ 新增：强制关闭分块并行
            //对于大分辨率图片无法转换，已修改
            // 极限压缩模式：只保留 AV1 规范允许的必要瓦片分割，关闭额外并行
            if (config.SerialEncode)
            {
                // 宽度 ≤ 4096 时无需瓦片，tileCols = 0；
                // 宽度 > 4096 时取最小合法列数，确保每个 tile 宽度 ≤ 4096。
                tileCols = GetMinLegalTileCols(w);
            }

            int crf = config.BaseCRF;
            if (isLosslessMode && !isTrulyLossless) crf = 0;

            return new EncodingInfo
            {
                SourcePixFmt = srcFmt,
                ActualPixFmt = actualPixFmt,
                PixInfo = pixInfo + (alphaDropped ? " (Alpha 已丢弃)" : ""),
                Width = w,
                Height = h,
                IsTrulyLossless = isTrulyLossless,
                IsLosslessMode = isLosslessMode,
                TileCols = tileCols,
                BaseCrf = crf,
                HasAlpha = hasAlpha
            };
        }

        // 3. CRF 搜索
        // ---------- 主调度 ----------
        // 修改签名，增加 originalFileName 参数
        private async Task<CRFSearchResult> RunCRFSearchAsync(string inputPath, PresetConfig config, EncodingInfo encInfo, string originalFileName)
        {
            // 使用 originalFileName 作为所有显示的基准名称
            string displayName = originalFileName;

            int crf = encInfo.BaseCrf;
            string actualPixFmt = encInfo.ActualPixFmt;
            var searchTime = TimeSpan.Zero;
            bool searchBasedCRF = false, useSafeModeFinalEncode = false;
            int totalEvaluations = 0;
            string name = displayName;   // 用于日志和输出

            if (!encInfo.IsLosslessMode && config.UseCRFSearch)
            {
                string metricModeLabel = (config.MetricMode ?? "vmaf").ToUpper();
                string targetDisplay = GetTargetDisplayString(config.TargetSSIM, metricModeLabel, config);
                SafeWriteLine($"  [SEARCH] [{name}] 开始 CRF 搜索 (目标 {metricModeLabel}={targetDisplay})，请耐心等待...");

                try
                {
                    var swSearch = Stopwatch.StartNew();
                    bool searchOk;
                    int finalCrf;
                    string usedPixFmt;

                    // ★ 传递 displayName 给搜索核心
                    (searchOk, finalCrf, usedPixFmt, totalEvaluations) = await TrySearchWithFormatAttempts(
                        inputPath, config, encInfo, actualPixFmt, name);   // 传入 name

                    if (!searchOk)
                    {
                        // 当 MinCRF=0 时，CRF=0 已是质量上限，跳过耗时的安全扫描
                        if (config.MinCRF == 0)
                        {
                            SafeWriteLine($"  [WARN] [{name}] 搜索未达目标，MinCRF=0，跳过安全扫描，将直接使用 CRF=0 最终编码");
                            // searchOk 保持 false，后续 else 分支会处理
                        }
                        else
                        {
                            SafeWriteLine($" [RETRY] [{name}] 普通搜索失败，开始安全模式全扫描 (yuv420p, cpu?used 0)...");
                            (searchOk, finalCrf, usedPixFmt, useSafeModeFinalEncode) = await RunSafeModeScan(
                                inputPath, config, name, config.MinCRF, config.MaxCRF);
                        }
                    }

                    swSearch.Stop();

                    searchTime = swSearch.Elapsed;

                    if (searchOk)
                    {
                        crf = Math.Clamp(finalCrf, config.MinCRF, config.MaxCRF);
                        searchBasedCRF = true;
                        if (!useSafeModeFinalEncode) actualPixFmt = usedPixFmt;
                    }
                    else
                    {
                        // ★ 新逻辑：若搜索无解且 MinCRF=0，则直接使用 CRF=0 进行最终编码
                        if (config.MinCRF == 0)
                        {
                            crf = 0;
                            SafeWriteLine($"  [WARN] [{name}] 搜索未达目标，CRF=0 也无法满足，将使用 CRF=0 进行最终编码");
                            _logger.LogInfo($"搜索失败但 MinCRF=0，强制使用 CRF=0 编码: {name}");
                        }
                        else
                        {
                            crf = config.BaseCRF;
                            SafeWriteLine($"  [WARN] [{name}] 所有搜索失败，使用 BaseCRF ({crf}) 直接编码");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 若最小CRF为0，异常时也尝试使用0编码（目标无法达成，但0是质量上限）
                    if (config.MinCRF == 0)
                    {
                        crf = 0;
                        _logger.LogInfo($"搜索异常但 MinCRF=0，强制使用 CRF=0 编码: {name} - {ex.Message}");
                        SafeWriteLine($" [WARN] [{name}] 搜索异常，使用 CRF=0 进行最终编码");
                    }
                    else
                    {
                        crf = config.BaseCRF;
                        _logger.LogInfo($"搜索异常，回退直接编码: {name} - {ex.Message}");
                        SafeWriteLine($" [WARN] [{name}] CRF搜索异常，使用 BaseCRF ({crf}) 直接编码");
                    }
                }
            }

            return new CRFSearchResult
            {
                Crf = crf,
                ActualPixFmt = actualPixFmt,
                SearchTime = searchTime,
                SearchBasedCRF = searchBasedCRF,
                UseSafeModeFinalEncode = useSafeModeFinalEncode,
                SearchEvalCount = totalEvaluations
            };
        }

        // ---------- 尝试目标格式列表搜索 ----------
        // ---------- 尝试目标格式列表搜索 ----------
        // 修改签名，增加 displayName 参数
        private async Task<(bool ok, int crf, string pixFmt, int totalEvalCount)> TrySearchWithFormatAttempts(
            string inputPath, PresetConfig config, EncodingInfo encInfo,
            string actualPixFmt, string displayName)
        {
            var attempts = BuildPixFmtAttempts(config, actualPixFmt, encInfo.HasAlpha);
            int totalEvalCount = 0;

            foreach (var fmt in attempts)
            {
                if (fmt != actualPixFmt && !config.AutoSource)
                {
                    string desc = fmt.Contains("422") ? "422" :
                                  (fmt.Contains("420") && !actualPixFmt.Contains("420") ? "420" : "");
                    if (!string.IsNullOrEmpty(desc))
                        SafeWriteLine($"  [RETRY] [{displayName}] 尝试 {desc} {fmt} ...");
                }

                // ★ 将 displayName 传给混合搜索
                (int crfResult, bool failed, bool qualityInsufficient, int evalCount) =
                    await HybridSearchCRFAsync(inputPath, encInfo.TileCols, config, fmt, IsJpeg(inputPath), displayName);
                totalEvalCount += evalCount;

                if (qualityInsufficient)
                    break;

                if (!failed)
                    return (true, crfResult, fmt, totalEvalCount);
            }
            return (false, config.BaseCRF, actualPixFmt, totalEvalCount);
        }

        // ---------- 安全模式全扫描 ----------
        private async Task<(bool ok, int crf, string pixFmt, bool safeMode)>
RunSafeModeScan(string inputPath, PresetConfig config, string name, int scanLow, int scanHigh)
        {
            using var safeCts = new CancellationTokenSource(TimeSpan.FromMinutes(config.SafeTimeoutMinutes));
            var safeToken = CancellationTokenSource.CreateLinkedTokenSource(
                safeCts.Token, _globalCts?.Token ?? default).Token;

            double target = config.TargetSSIM + SSIMMargin;
            int bestSafeCRF = -1;
            // ★ 使用传入的区间，而非全局 MinCRF/MaxCRF
            int start = scanHigh;
            int end = scanLow;
            int totalSteps = start - end + 1;
            int step = 0;
            int consecutiveFailures = 0;

            for (int testCrf = start; testCrf >= end; testCrf--)
            {
                step++;
                if (safeToken.IsCancellationRequested) break;

                if (step == 1 || step == totalSteps || step % 5 == 0)
                    SafeWriteLine($"  [{name}] 安全扫描 {step}/{totalSteps} (CRF={testCrf})...");

                double curSSIM = await SafeModeSSIM(inputPath, config, testCrf, safeToken);
                if (curSSIM >= target)
                {
                    bestSafeCRF = testCrf;
                    break;
                }

                if (curSSIM < 0)
                {
                    consecutiveFailures++;
                    if (consecutiveFailures >= 5)
                    {
                        SafeWriteLine($"  [{name}] 安全扫描连续失败 {consecutiveFailures} 次，终止扫描");
                        break;
                    }
                }
                else
                {
                    consecutiveFailures = 0;
                }
            }

            if (bestSafeCRF > 0)
            {
                SafeWriteLine($"  -> [{name}] 安全模式扫描成功，最佳 CRF = {bestSafeCRF}");
                return (true, bestSafeCRF, "yuv420p", true);
            }
            return (false, config.BaseCRF, "yuv420p", false);
        }

        // ---------- 安全模式单次 SSIM ----------
        private async Task<double> SafeModeSSIM(string inputPath, PresetConfig config, int testCrf,
                                        CancellationToken token)
        {
            if (token.IsCancellationRequested) return -1;

            bool acquired = false;
            try
            {
                await _ssimConcurrency.WaitAsync(token);
                acquired = true;

                string tmpAvif = Path.Combine(_outputDir, $"_safe_{Guid.NewGuid():N}.avif");
                string effectiveAom = config.GetEffectiveAomParams();
                string aomPart = string.IsNullOrEmpty(effectiveAom) ? "" : $"-aom-params {effectiveAom}";
                try
                {
                    // 提前获取分辨率，用于合法 tile 计算
                    var (w, h) = await GetResolutionAsync(inputPath);

                    string args = BuildSafeModeArgs(inputPath, tmpAvif, config, testCrf, aomPart, w);
                    (bool ok, string _) = await RunFfmpegExAsync(_ffmpegPath, args,
                        TimeSpan.FromMinutes(config.SafeEncodeTimeoutMinutes));
                    if (!ok || !_fs.FileExists(tmpAvif) || _fs.GetFileLength(tmpAvif) < 100) return -1;

                    QualityMetrics? metrics = await ComputeAllMetricsAsync(inputPath, tmpAvif);
                    if (metrics != null)
                    {
                        string normalizedInput = GetNormalizedPathForCache(inputPath);
                        string rowMtSafe = GetRowMtArg(config);
                        string cacheKey = GetSsimCacheKey(
                            normalizedInput, testCrf, "yuv420p", 0, 0,
                            IsJpeg(inputPath), effectiveAom, 8, w, h, rowMtSafe);
                        _cache.SetMetrics(cacheKey, metrics);

                        return GetSearchScore(metrics, config.MetricMode ?? "ssim");
                    }

                    // 回退传统 SSIM
                    _logger.LogInfo($"安全模式回退传统 SSIM: [{Path.GetFileName(inputPath)}] CRF={testCrf}");
                    return await SSIMDirect(inputPath, tmpAvif, "yuv420p");
                }
                finally
                {
                    if (_fs.FileExists(tmpAvif)) try { _fs.DeleteFile(tmpAvif); } catch { }
                }
            }
            finally
            {
                if (acquired) _ssimConcurrency.Release();
            }
        }

        // 4. 构建像素格式尝试列表（从原搜索代码中提取）
        private static List<string> BuildPixFmtAttempts(PresetConfig config, string actualPixFmt, bool hasAlpha)
        {
            // 提取位深后缀和基础格式
            string depthSuffix = actualPixFmt.EndsWith("10le") ? "10le" : "";
            string baseFmt = depthSuffix.Length > 0 ? actualPixFmt[..^4] : actualPixFmt;

            bool effectiveAlpha = hasAlpha;
            string cleanBase = effectiveAlpha ? baseFmt.Replace("a", "") : baseFmt;

            if (config.AutoSource)
                return new List<string> { actualPixFmt };

            var attempts = new List<string>();
            int startChroma = cleanBase.Contains("444") ? 0 :
                              (cleanBase.Contains("422") ? 1 : 2);

            for (int level = startChroma; level <= 2; level++)
            {
                string chroma = level switch { 0 => "444", 1 => "422", _ => "420" };

                // ★ 修复：正确生成 yuva / yuv 格式
                string fmt8 = effectiveAlpha ? $"yuva{chroma}p" : $"yuv{chroma}p";
                string fmt10 = $"{fmt8}10le";

                if (level == startChroma)
                {
                    if (depthSuffix == "10le")
                    {
                        if (config.BitDepth >= 10) attempts.Add(fmt10);
                        if (config.BitDepth <= 10) attempts.Add(fmt8);
                    }
                    else
                    {
                        if (config.BitDepth <= 10) attempts.Add(fmt8);
                        if (config.BitDepth >= 10) attempts.Add(fmt10);
                    }
                }
                else
                {
                    if (config.BitDepth >= 10) attempts.Add(fmt10);
                    if (config.BitDepth <= 10) attempts.Add(fmt8);
                }
            }

            return attempts.Distinct().ToList();
        }

        // 5. 执行最终编码
        private async Task<FinalEncodeResult> PerformFinalEncodeAsync(
    string inputPath, string outputPath, PresetConfig config,
    EncodingInfo encInfo, CRFSearchResult searchResult)
        {
            int timeoutMinutes = config.EncodeTimeoutMinutes > 0
                ? config.EncodeTimeoutMinutes
                : Math.Clamp((int)(((double)encInfo.Width * encInfo.Height) / (1920.0 * 1080.0) * 10), 5, 180);

            string effectiveAom = config.GetEffectiveAomParams();
            string aomPart = string.IsNullOrEmpty(effectiveAom) ? "" : $"-aom-params {effectiveAom}";
            string name = Path.GetFileName(inputPath);

            if (searchResult.UseSafeModeFinalEncode)
            {
                // 安全模式最终编码：传入图像宽度，确保 tile 合法
                return await EncodeSafeMode(inputPath, outputPath, config, searchResult,
                                            timeoutMinutes, aomPart, encInfo.Width);
            }
            else
            {
                var result = await EncodeWithFallback(inputPath, outputPath, config, encInfo, searchResult,
                                                      timeoutMinutes, aomPart, name, effectiveAom);
                if (!result.Success && encInfo.IsTrulyLossless)
                {
                    result = await TryLosslessFallback(inputPath, outputPath, config, encInfo, searchResult,
                                                       timeoutMinutes, aomPart, effectiveAom);
                }

                if (!result.Success && string.IsNullOrEmpty(result.FailReason))
                    result.FailReason = $"编码失败（多次重试后）: {result.FailReason}";

                return result;
            }
        }

        /// <summary>
        /// 构造安全模式（yuv420p + 单tile + 全色域）的 ffmpeg 参数字符串
        /// </summary>
        /// <summary>
        /// 构造安全模式（yuv420p + 单 tile + 全色域）的 ffmpeg 参数字符串。
        /// 仅对 libaom?av1 启用 tile 与 row?mt 参数，其他编码器忽略，避免无效参数造成失败。
        /// </summary>
        /// <summary>
        /// 构造安全模式（yuv420p + 单 tile + 全色域）的 ffmpeg 参数字符串。
        /// 仅对 libaom?av1 启用 tile 与 row?mt 参数，其他编码器忽略，避免无效参数造成失败。
        /// 若启用了 DisableTileParallel，则强制 tile=0 且关闭 row?mt。
        /// </summary>
        /// <summary>
        /// 构造安全模式（yuv420p + 单 tile + 全色域）的 ffmpeg 参数字符串。
        /// 若启用了 SerialEncode，则强制 tile=0 且关闭 row?mt。
        /// </summary>
        private static string BuildSafeModeArgs(string inputPath, string outputPath, PresetConfig config,
                                 int crf, string aomPart, int imageWidth)
        {
            bool useStillPic = EncoderSupportsStillPicture(config.Encoder);
            string stillPic = useStillPic ? "-still-picture 1" : "";

            int minCols = GetMinLegalTileCols(imageWidth);
            int maxCols = GetMaxLegalTileCols(imageWidth);
            int safeTileCols;
            if (imageWidth < 256 || minCols > maxCols)
                safeTileCols = 0;
            else
                safeTileCols = minCols;   // minCols 已确保 tile 宽度 ≤4096，无需额外强制 ≥2

            string safeRowMt;
            // ===== 极限压缩模式（关闭所有并行）=====
            if (config.SerialEncode)
            {
                safeTileCols = GetMinLegalTileCols(imageWidth);
                if (EncoderUtils.IsLibAom(config.Encoder))
                {
                    safeRowMt = "-row-mt 0";
                }
                else
                {
                    safeRowMt = "";
                }
            }
            else
            {
                safeRowMt = (EncoderUtils.IsLibAom(config.Encoder)) ? "-row-mt 1" : "";
            }
            // =====================

            string safeTile = EncoderUtils.IsLibAom(config.Encoder)
                ? $"-tile-columns {safeTileCols} -tile-rows 0" : "";
            string encArgs = BuildEncoderSpecificArgs(config, 0, safeTile, safeRowMt);
            string threadsArg = config.SerialEncode ? "-threads 1" : "";  // 新属性名

            return $"-loglevel error -hide_banner -i \"{inputPath}\" " +
                   $"-c:v {config.Encoder} -pix_fmt yuv420p " +
                   $"-crf {crf} {encArgs} " +
                   $"-color_range pc {stillPic} -frames:v 1 {aomPart} {threadsArg} -y \"{outputPath}\"";
        }


        private async Task<FinalEncodeResult> EncodeWithFallback(
    string inputPath, string outputPath, PresetConfig config,
    EncodingInfo encInfo, CRFSearchResult searchResult,
    int timeoutMinutes, string aomPart, string name, string effectiveAom)
        {
            var startTime = DateTime.Now;
            int crf = searchResult.Crf;
            string actualPixFmt = searchResult.ActualPixFmt;
            bool success;
            TimeSpan encodeTime;
            int retries;
            string failReason;
            bool fromCache;
            string? actualAom;
            string? finalCommand;
            bool usedSafeModeFallback = false;

            // 1. 尝试常规编码
            (success, encodeTime, retries, failReason, fromCache, actualAom, finalCommand) =
                await EncodeToFileExAsync(inputPath, outputPath, crf, encInfo.TileCols, config.FinalCpuUsed,
                    config, IsJpeg(inputPath), actualPixFmt, encInfo.IsTrulyLossless, timeoutMinutes);

            // 2. CRF 递增重试
            if (!success && searchResult.SearchBasedCRF && crf < config.MaxCRF)
            {
                for (int attemptCRF = crf + 1; attemptCRF <= config.MaxCRF; attemptCRF++)
                {
                    SafeWriteLine($" [WARN] [{name}] CRF={crf} 编码失败，尝试 CRF={attemptCRF}...");
                    (success, encodeTime, retries, failReason, fromCache, actualAom, finalCommand) =
                        await EncodeToFileExAsync(inputPath, outputPath, attemptCRF, encInfo.TileCols, config.FinalCpuUsed,
                            config, IsJpeg(inputPath), actualPixFmt, encInfo.IsTrulyLossless, timeoutMinutes);
                    if (success) { crf = attemptCRF; break; }
                }
            }

            // 3. 最终安全模式回退（使用合法 tile 尺寸）
            if (!success)
            {
                SafeWriteLine($" [WARN] [{name}] 常规/降级均失败，尝试最终安全模式（yuv420p）...");
                // 构建安全模式参数，传入图像宽度以满足 tile 合法性
                string safeArgs = BuildSafeModeArgs(inputPath, outputPath, config, crf, aomPart, encInfo.Width);
                var swSafe = Stopwatch.StartNew();
                (bool safeOk, string safeErr) = await RunFfmpegExAsync(_ffmpegPath, safeArgs, TimeSpan.FromMinutes(timeoutMinutes * 2));
                swSafe.Stop();

                if (safeOk)
                {
                    success = true;
                    encodeTime = swSafe.Elapsed;
                    retries = 0;
                    failReason = "";
                    actualPixFmt = "yuv420p";
                    fromCache = false;
                    usedSafeModeFallback = true;

                    string encoderDesc = config.Encoder.StartsWith("libsvtav1") ? "preset 0" :
                                         config.Encoder.StartsWith("libaom-av1") ? "cpu-used 0" :
                                         config.Encoder.StartsWith("librav1e") ? "speed 0" : "hardware";
                    bool useStillPic = EncoderSupportsStillPicture(config.Encoder);
                    string stillDesc = useStillPic ? ", still-picture" : "";
                    actualAom = $"safe (yuv420p, {encoderDesc}{stillDesc}, full-range)";
                    finalCommand = safeArgs;
                }
                else
                {
                    failReason = safeErr;
                }
            }

            return new FinalEncodeResult
            {
                Success = success,
                Crf = crf,
                ActualPixFmt = actualPixFmt,
                EncodeTime = encodeTime,
                Retries = retries,
                FailReason = failReason,
                FromCache = fromCache,
                ActualAom = actualAom,
                FinalCommand = finalCommand,
                UseSafeMode = searchResult.UseSafeModeFinalEncode || usedSafeModeFallback,
                StartTime = startTime
            };
        }


        private async Task<FinalEncodeResult> EncodeSafeMode(
    string inputPath, string outputPath, PresetConfig config,
    CRFSearchResult searchResult, int timeoutMinutes, string aomPart, int imageWidth)
        {
            var startTime = DateTime.Now;
            int crf = searchResult.Crf;

            string safeArgs = BuildSafeModeArgs(inputPath, outputPath, config, crf, aomPart, imageWidth);

            var swSafe = Stopwatch.StartNew();
            (bool success, string failReason) = await RunFfmpegExAsync(_ffmpegPath, safeArgs, TimeSpan.FromMinutes(timeoutMinutes));
            swSafe.Stop();

            string encoderDesc = config.Encoder.StartsWith("libsvtav1", StringComparison.OrdinalIgnoreCase) ? "preset 0" :
                                 config.Encoder.StartsWith("libaom-av1", StringComparison.OrdinalIgnoreCase) ? "cpu-used 0" :
                                 config.Encoder.StartsWith("librav1e", StringComparison.OrdinalIgnoreCase) ? "speed 0" :
                                 "hardware";
            bool useStillPic = EncoderSupportsStillPicture(config.Encoder);
            string stillDesc = useStillPic ? ", still-picture" : "";
            string safeDesc = $"safe (yuv420p, {encoderDesc}{stillDesc}, full-range)";

            return new FinalEncodeResult
            {
                Success = success,
                Crf = crf,
                ActualPixFmt = "yuv420p",
                EncodeTime = swSafe.Elapsed,
                Retries = 0,
                FailReason = success ? "" : $"安全模式编码失败: {failReason}",
                FromCache = false,
                ActualAom = safeDesc,
                FinalCommand = success ? safeArgs : null,
                UseSafeMode = true,
                StartTime = startTime
            };
        }




        private async Task<FinalEncodeResult> TryLosslessFallback(
    string inputPath, string outputPath, PresetConfig config,
    EncodingInfo encInfo, CRFSearchResult searchResult,
    int timeoutMinutes, string aomPart, string effectiveAom)
        {
            SafeWriteLine($" [WARN] [{Path.GetFileName(inputPath)}] 真无损失败，尝试 -crf 0 ...");

            var fallbackConfig = new PresetConfig
            {
                BaseCRF = 0,
                Lossless = false,                           // 使用 -crf 0 而非真无损模式
                PixelFormat = searchResult.ActualPixFmt,
                AomParams = effectiveAom,
                FinalCpuUsed = 0,
                SearchCpuUsed = 0,
                UseCRFSearch = false,
                MaxJobs = config.MaxJobs,
                BitDepth = config.BitDepth,
                Encoder = config.Encoder
            };

            (bool success, TimeSpan encodeTime, int retries, string failReason, bool fromCache,
                string? actualAom, string? finalCommand) =
                await EncodeToFileExAsync(inputPath, outputPath, 0, 0, 0, fallbackConfig,
                    IsJpeg(inputPath), searchResult.ActualPixFmt, false, timeoutMinutes * 2);

            return new FinalEncodeResult
            {
                Success = success,
                Crf = 0,
                ActualPixFmt = searchResult.ActualPixFmt,
                EncodeTime = encodeTime,
                Retries = retries,
                FailReason = success ? "" : "真无损回退仍然失败",
                FromCache = fromCache,
                ActualAom = actualAom,
                FinalCommand = finalCommand,
                UseSafeMode = false,
                StartTime = DateTime.Now
            };
        }





        private void MarkProcessed(EncodeResult? r)
        {
            _progress.MarkFileProcessed();
            PrintProgress(r);

            // ★ 向 GUI 报告进度（0 ~ 100）
            if (_progress.TotalFiles > 0)
            {
                int pct = _progress.ProcessedCount * 100 / _progress.TotalFiles;
                _guiProgress?.Report(Math.Min(pct, 100));
            }
        }


        /// <summary>
        /// 生成用于编码缓存的一致键，确保所有缓存访问使用相同格式。
        /// </summary>
        /// <summary>
        /// 生成用于编码缓存的一致键，确保所有缓存访问使用相同格式。
        /// </summary>
        private static string GetEncodeCacheKey(
            string normalizedPath, int crf, string pixFmt,
            string tilePart, int actualCpu, bool isTrueLossless,
            string aomParams, bool jpeg, int bitDepth,
            int width = 0, int height = 0, string rowMt = "")   // ★ 新增 rowMt 参数
        {
            string res = (width > 0 && height > 0) ? $"|res={width}x{height}" : "";
            return $"{normalizedPath}|crf={crf}|pix={pixFmt}" +
                   $"|tile={tilePart}|cpu={actualCpu}|lossless={isTrueLossless}" +
                   $"|aom={aomParams}|jpeg={jpeg}|depth={bitDepth}{res}|rowmt={rowMt}";
        }







        // ── 编码方法（带错误详情，并自动降级像素格式） ──
        // ========== 修复后的 EncodeToFileExAsync（信号量超时） ==========
        // ========== EncodeToFileExAsync 主体 ==========
        private async Task<(bool ok, TimeSpan t, int retries, string error, bool fromCache,
                    string? actualAomParams, string? commandLine)>
EncodeToFileExAsync(string input, string output, int crf, int tileCols, int cpuUsed, PresetConfig cfg,
                    bool jpeg, string pixFmt, bool isTrueLossless, int timeoutMinutes,
                    bool allowParamDegrade = true)
        {
            string[] pixFmtsToTry = GetPixelFormatFallbackList(pixFmt, isTrueLossless);
            string lastError = "所有像素格式尝试均失败";
            string fileName = Path.GetFileName(input);
            string normalizedKey = GetNormalizedPathForCache(input);
            var fatalSet = _fatalFmts.GetOrAdd(normalizedKey, _ => new ConcurrentDictionary<string, byte>());
            foreach (var currentPixFmt in pixFmtsToTry)
            {
                // 若该格式之前已被标记为“无法生成任何输出”，直接跳过
                if (fatalSet.ContainsKey(currentPixFmt))
                {
                    _logger.LogInfo($"致命格式 {currentPixFmt} 已禁用，跳过 [{fileName}]");
                    continue;
                }

                var result = await TryEncodeWithPixelFormatFallback(
                        input, output, crf, tileCols, cpuUsed, cfg, jpeg, currentPixFmt, isTrueLossless,
                        timeoutMinutes, allowParamDegrade, fileName);

                if (result.ok)
                    return result;

                lastError = result.error ?? "未知错误";

                // 在 EncodeToFileExAsync 的循环内，替换原有的致命标记逻辑：
                // 在编码结果处理中：
                if (result.error?.StartsWith("FATAL_NOTHING:", StringComparison.OrdinalIgnoreCase) == true)
                {
                    // 只有所有参数集都 Nothing 才标记
                    fatalSet.TryAdd(currentPixFmt, 0);
                    _logger.LogInfo($"致命格式 {currentPixFmt} 已记录 [{fileName}]，将不再重试");
                }
                // 原有的降级日志保留

                // 仅当还有后续格式时才输出降级日志
                if (currentPixFmt != pixFmtsToTry.Last())
                {
                    string nextFmt = pixFmtsToTry[Array.IndexOf(pixFmtsToTry, currentPixFmt) + 1];
                    if (!fatalSet.ContainsKey(nextFmt))
                        _logger.LogInfo($"像素格式 {currentPixFmt} 编码失败，降级尝试 {nextFmt} ...");
                }
            }

            string chainDesc = string.Join(" → ", pixFmtsToTry);
            _logger.LogInfo($"编码失败 [CRF={crf}] [{fileName}] 尝试序列: {chainDesc}。最后错误: {lastError}");
            return (false, TimeSpan.Zero, _maxRetries, $"编码失败 [序列: {chainDesc}] {lastError}", false, null, null);
        }

        private async Task<(bool ok, TimeSpan t, int retries, string error, bool fromCache,
    string? actualAomParams, string? commandLine)>
TryEncodeWithPixelFormatFallback(string input, string output, int crf, int tileCols, int cpuUsed,
    PresetConfig cfg, bool jpeg, string currentPixFmt, bool isTrueLossless, int timeoutMinutes,
    bool allowParamDegrade, string fileName)
        {
            // 获取图片宽度（只取宽度，高度不需要）
            var (w, _) = await GetResolutionAsync(input);
            var paramSets = BuildParamSets(cfg, currentPixFmt, isTrueLossless, tileCols, cpuUsed,
                                           allowParamDegrade, w);   // 传入宽度

            string lastError = "";
            bool allNothingWritten = true;

            foreach (var param in paramSets)
            {
                var result = await TryEncodeWithParamSet(input, output, crf, currentPixFmt, param, cfg,
                                                         isTrueLossless, timeoutMinutes, fileName);
                if (result.ok)
                    return result;

                lastError = result.error ?? "未知错误";
                if (!lastError.Contains("Nothing was written", StringComparison.OrdinalIgnoreCase))
                    allNothingWritten = false;
            }

            if (allNothingWritten)
                return (false, TimeSpan.Zero, _maxRetries, $"FATAL_NOTHING:{lastError}", false, null, null);

            return (false, TimeSpan.Zero, _maxRetries, $"像素格式 {currentPixFmt} 所有参数均失败", false, null, null);
        }

        // ---------- 辅助函数 ----------

        /// <summary> 获取像素格式降级顺序列表 </summary>
        private static string[] GetPixelFormatFallbackList(string pixFmt, bool isTrueLossless)
        {
            // 提取 Alpha 标记和位深后缀
            bool hasAlpha = pixFmt.Contains('a');
            string depthSuffix = pixFmt.EndsWith("10le") ? "10le" : "";

            // 去掉后缀，得到纯净的基础格式（如 yuv444p 或 yuva444p）
            string baseFmt = depthSuffix.Length > 0 ? pixFmt.Substring(0, pixFmt.Length - 4) : pixFmt;

            // 分离出色彩采样部分
            if (baseFmt.Contains("444") && !isTrueLossless)
            {
                return hasAlpha
                    ? new[] { $"yuva444p{depthSuffix}", $"yuva422p{depthSuffix}", $"yuva420p{depthSuffix}" }
                    : new[] { $"yuv444p{depthSuffix}", $"yuv422p{depthSuffix}", $"yuv420p{depthSuffix}" };
            }
            if (baseFmt.Contains("422") && !isTrueLossless)
            {
                return hasAlpha
                    ? new[] { $"yuva422p{depthSuffix}", $"yuva420p{depthSuffix}" }
                    : new[] { $"yuv422p{depthSuffix}", $"yuv420p{depthSuffix}" };
            }
            return new[] { pixFmt };
        }

        /// <summary> 构建参数集尝试列表 </summary>
        /// <summary> 构建参数集尝试列表 </summary>
        /// <summary> 构建参数集尝试列表（已优化降级顺序，优先保留 AOM 参数） </summary>
        /// <summary> 构建参数集尝试列表（已优化降级顺序，优先保留 AOM 参数） </summary>
        private static List<(string aomParams, string tilePart, int actualCpu, string rowMt)> BuildParamSets(
    PresetConfig cfg, string currentPixFmt, bool isTrueLossless, int tileCols, int cpuUsed,
    bool allowParamDegrade, int imageWidth)
        {
            string effectiveAom = cfg.GetEffectiveAomParams();
            var sets = new List<(string, string, int, string)>();
            bool isHighChroma = currentPixFmt.Contains("444") || currentPixFmt.Contains("422");
            string rowMt;

            // ===== 极限压缩：强制关闭所有并行 =====
            if (cfg.SerialEncode)
            {
                // 宽度 ≤ 4096 时 tileCols = 0；否则采用最小合法列数，保证每个 tile ≤ 4096 像素
                tileCols = GetMinLegalTileCols(imageWidth);
                if (EncoderUtils.IsLibAom(cfg.Encoder))
                {
                    rowMt = "-row-mt 0";
                    // 全局 -threads 1 已控制线程数，无需在 -aom-params 中注入 threads
                }
                else
                {
                    rowMt = "";
                }
            }
            else
            {
                rowMt = EncoderUtils.IsLibAom(cfg.Encoder) ? "-row-mt 1" : "";
            }
            // =====================================

            // 合法性约束（图像宽度限制）
            int minLegal = GetMinLegalTileCols(imageWidth);
            int maxLegal = GetMaxLegalTileCols(imageWidth);
            int legalTileCols = Math.Clamp(tileCols, minLegal, maxLegal);

            if (!isTrueLossless && isHighChroma)
            {
                sets.Add((effectiveAom, TilePart(legalTileCols, false), cpuUsed, rowMt));
                if (allowParamDegrade)
                {
                    sets.Add((effectiveAom, TilePart(legalTileCols, false), 0, rowMt));

                    bool supportsTile = EncoderUtils.IsLibAom(cfg.Encoder) || EncoderUtils.IsSvtAv1(cfg.Encoder);
                    string tilePart = supportsTile ? $"-tile-columns {legalTileCols} -tile-rows 0" : "";
                    sets.Add(("", tilePart, 0, rowMt));

                    // 安全 tile（强制单线程时同样归零）
                    // 安全 tile（强制单线程时同样归零）
                    int safeTileCols;
                    if (imageWidth > 0 && imageWidth >= 256 && minLegal <= maxLegal)
                        safeTileCols = minLegal;   // minLegal 已确保 tile 宽度 ≤ 4096，无需额外强制 ≥2
                    else
                        safeTileCols = 0;
                    if (cfg.SerialEncode) safeTileCols = 0;   // ← 新属性名

                    string safeTilePart = safeTileCols > 0
                            ? $"-tile-columns {safeTileCols} -tile-rows 0"
                            : "-tile-columns 0 -tile-rows 0";
                    sets.Add(("", safeTilePart, 0, rowMt));
                }
            }
            else
            {
                sets.Add((effectiveAom, TilePart(legalTileCols, isTrueLossless), cpuUsed, rowMt));
            }

            return sets;
        }

        private async Task<(bool ok, TimeSpan t, int retries, string error, bool fromCache,
    string? actualAomParams, string? commandLine)>
TryEncodeWithParamSet(string input, string output, int crf, string currentPixFmt,
                      (string aomParams, string tilePart, int actualCpu, string rowMt) param,
                      PresetConfig cfg, bool isTrueLossless, int timeoutMinutes, string fileName)
        {
            string normalizedInput = GetNormalizedPathForCache(input);
            var (encW, encH) = await GetResolutionAsync(input);

            string cacheKey = GetEncodeCacheKey(normalizedInput, crf, currentPixFmt, param.tilePart,
                                                param.actualCpu, isTrueLossless, param.aomParams,
                                                IsJpeg(input), currentPixFmt.Contains("10le") ? 10 : 8,
                                                encW, encH, param.rowMt);   // ★ 新增 param.rowMt

            string cacheFile = Path.Combine(_outputDir, "_enc_cache", $"{Sha256(cacheKey)}.avif");

            // 缓存命中
            if (_cache.TryGetEncode(cacheKey, out var cached) && File.Exists(cached.file))
            {
                _fs.CreateDirectory(Path.GetDirectoryName(output)!);
                _fs.CopyFile(cached.file!, output, true);
                _logger.LogInfo($"复用编码缓存: {input} CRF={crf} pix={currentPixFmt} 原耗时={cached.encodeTime.TotalSeconds:F4}s");
                return (true, cached.encodeTime, 0, "", true, param.aomParams, cached.commandLine);
            }

            // 执行编码重试
            return await ExecuteEncodingWithRetries(input, output, crf, currentPixFmt, param, cfg,
                                                    isTrueLossless, timeoutMinutes, fileName, cacheKey, cacheFile);
        }


        private async Task<(bool ok, TimeSpan t, int retries, string error, bool fromCache,
    string? actualAomParams, string? commandLine)>
    ExecuteEncodingWithRetries(string input, string output, int crf, string currentPixFmt,
                           (string aomParams, string tilePart, int actualCpu, string rowMt) param,
                           PresetConfig cfg, bool isTrueLossless, int timeoutMinutes, string fileName,
                           string cacheKey, string cacheFile)
        {
            _logger.LogSearch($"  ? [{fileName}] 等待编码资源 (CRF={crf})...");
            bool slotTaken = false;
            try
            {
                if (!await _ffmpegSlots.WaitAsync(TimeSpan.FromSeconds(300), _globalCts?.Token ?? default))
                {
                    _logger.LogSearch($"? 编码信号量获取超时: {input} CRF={crf}");
                    return (false, TimeSpan.Zero, 0, "编码信号量获取超时", false, null, null);
                }
                slotTaken = true;

                // ★ 随机错开启动时间，避免任务同时开始/同时结束造成 CPU 波峰波谷
                int jitterMs = Random.Shared.Next(0, 2000);          // 0 ~ 2000 毫秒随机抖动
                if (jitterMs > 0)
                    await Task.Delay(jitterMs, _globalCts?.Token ?? default);

                _logger.LogSearch($"  ? [{fileName}] 开始编码 (CRF={crf}, pix={currentPixFmt})");

                for (int attempt = 0; attempt <= _maxRetries; attempt++)
                {
                    string ffArgs = await BuildFfmpegArgsAsync(input, output, crf, currentPixFmt, param, cfg, isTrueLossless);
                    // ... 后续编码逻辑保持不变 ...
                    var sw = Stopwatch.StartNew();
                    (bool success, string stderrLastLine) = await RunFfmpegExAsync(_ffmpegPath, ffArgs,
                        TimeSpan.FromMinutes(timeoutMinutes));
                    sw.Stop();

                    if (success)
                    {
                        if (_fs.GetFileLength(output) < 100)
                        {
                            _logger.LogSearch($"编码输出文件过小 ({_fs.GetFileLength(output)} 字节)，丢弃并重试");
                            if (_fs.FileExists(output)) _fs.DeleteFile(output);
                            if (attempt < _maxRetries) { await Task.Delay(1000); continue; }
                            return (false, TimeSpan.Zero, _maxRetries, "编码输出文件过小", false, null, null);
                        }

                        // 成功，保存缓存
                        _fs.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
                        _fs.CopyFile(output, cacheFile, true);
                        _cache.SetEncode(cacheKey, cacheFile, sw.Elapsed, ffArgs);
                        _logger.LogSearch($"? 编码成功: {input} CRF={crf} 耗时={sw.Elapsed.TotalSeconds:F4}s");
                        return (true, sw.Elapsed, attempt, "", false, param.aomParams, ffArgs);
                    }

                    string error = $"CRF={crf}, {stderrLastLine}";
                    _logger.LogSearch($"? 编码失败: {input} 尝试{attempt + 1}/{_maxRetries + 1} - {error}");

                    // 清理失败输出
                    if (_fs.FileExists(output)) _fs.DeleteFile(output);

                    // 致命错误：立即停止重试
                    if (stderrLastLine.Contains("Nothing was written", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogSearch($"检测到致命错误，放弃重试: {input} CRF={crf}");
                        return (false, TimeSpan.Zero, attempt, error, false, null, null);
                    }

                    if (attempt < _maxRetries) await Task.Delay(1000);
                }

                return (false, TimeSpan.Zero, _maxRetries, $"CRF={crf}, 重试耗尽", false, null, null);
            }
            catch (Exception ex)
            {
                _logger.LogError($"编码异常: {input} - {ex.Message}");
                return (false, TimeSpan.Zero, _maxRetries, $"异常: {ex.Message}", false, null, null);
            }
            finally
            {
                if (slotTaken) _ffmpegSlots.Release();
            }
        }



        /// <summary>
        /// 通过 ffprobe (JSON 模式) 探测输入文件的色彩元数据。
        /// 如果任何核心字段缺失、为 unknown/reserved，则返回 null，避免半继承。
        /// </summary>
        private async Task<(string primaries, string trc, string space, string? range)?>
        GetSourceColorInfoAsync(string inputPath)
        {
            var probe = await GetProbeInfoAsync(inputPath);
            if (probe == null) return null;

            // 任何核心色彩字段缺失则返回 null
            if (probe.ColorPrimaries == null || probe.ColorTransfer == null || probe.ColorSpace == null)
                return null;

            return (probe.ColorPrimaries, probe.ColorTransfer, probe.ColorSpace, probe.ColorRange);
        }



        /// <summary> 构建 ffmpeg 参数字符串 </summary>
        /// <summary> 构建 ffmpeg 参数字符串 </summary>
        private async Task<string> BuildFfmpegArgsAsync(string input, string output, int crf, string pixFmt,
                   (string aomParams, string tilePart, int actualCpu, string rowMt) param,
                   PresetConfig cfg, bool isTrueLossless)
        {
            string logLevel = "-loglevel info -hide_banner";
            string aom = string.IsNullOrEmpty(param.aomParams) ? "" : $"-aom-params {param.aomParams}";

            string crfPart = isTrueLossless
            ? cfg.Encoder switch
            {
                _ when EncoderUtils.IsRav1e(cfg.Encoder) => "-rav1e-params lossless=1",
                _ when EncoderUtils.IsSvtAv1(cfg.Encoder) => "-svtav1-params lossless=1",
                _ => "-lossless 1"
            }
            : $"-crf {crf}";

            string stillPic = EncoderSupportsStillPicture(cfg.Encoder) ? "-still-picture 1" : "";
            string encoderSpecific = BuildEncoderSpecificArgs(cfg, param.actualCpu, param.tilePart, param.rowMt);
            string threadsArg = cfg.SerialEncode ? "-threads 1" : "";

            // ---------- 默认 SDR sRGB（全范围），根据像素格式选择矩阵 ----------
            string primaries = "bt709";
            string trc = "iec61966-2-1";
            // 仅当像素格式为 gbr*（planar RGB）时使用 identity matrix，
            // 其他 YUV 格式（yuv420p, yuv444p 等）使用 bt709。
            string space = pixFmt.StartsWith("gbr", StringComparison.OrdinalIgnoreCase)
                                   ? "gbr"
                                   : "bt709";
            string rangeVal = "pc";

            // ---------- 探测源文件色彩元数据 ----------
            var srcColor = await GetSourceColorInfoAsync(input);
            if (srcColor != null)
            {
                var p = srcColor.Value.primaries;
                var t = srcColor.Value.trc;

                bool isHdrPq = p == "bt2020" && t == "smpte2084";   // 仅允许 PQ HDR

                if (isHdrPq)
                {
                    primaries = "bt2020";
                    trc = "smpte2084";
                    space = "bt2020nc";          // HDR10 标准矩阵
                }
                // 其他组合保留默认 space（已按像素格式选择 bt709 或 gbr）

                // range 始终允许继承
                if (!string.IsNullOrWhiteSpace(srcColor.Value.range))
                    rangeVal = srcColor.Value.range;
            }

            // range 映射
            string rangeArg = rangeVal.ToLowerInvariant() switch
            {
                "tv" or "mpeg" => "-color_range tv",
                _ => "-color_range pc"
            };

            string colorMeta = $"-color_primaries {primaries} -color_trc {trc} -colorspace {space}";

            return $"{logLevel} -i \"{input}\" " +
                   $"-c:v {cfg.Encoder} -pix_fmt {pixFmt} {rangeArg} {colorMeta} " +
                   $"{crfPart} {encoderSpecific} " +
                   $"{stillPic} -frames:v 1 {aom} {threadsArg} -y \"{output}\"";
        }

        private static string CsvEscape(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
                return $"\"{field.Replace("\"", "\"\"")}\"";
            return field;
        }

        private async Task<(bool success, string stderrLastLine)> RunFfmpegExAsync(string file, string args, TimeSpan timeout)
        {
            var (exitCode, stdout, stderr) = await _processRunner.RunAsync(
                file, args, timeout, _globalCts?.Token ?? default);

            // 记录完整 stderr
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                _logger.LogInfo($"ffmpeg stderr:\n{stderr.Trim()}");
            }

            string lastLine = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "";

            if (exitCode != 0)
            {
                _logger.LogError($"ffmpeg 错误(退出码 {exitCode}): {lastLine}");
                return (false, lastLine);
            }
            return (true, "");
        }

    }

}

