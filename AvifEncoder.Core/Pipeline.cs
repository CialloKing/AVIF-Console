using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
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

        // 动图相关
        public bool IsAnimated { get; set; }
        public int FrameCount { get; set; } = 1;
        public double Duration { get; set; }
        public double Fps { get; set; }
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

        /// <summary>获取指标缓存引用。注意：调用方不应修改返回对象，修改请用 UpdateMetrics。</summary>
        public bool TryGetMetrics(string key, out QualityMetrics? metrics)
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

        private readonly ICacheManager _cache;


        private readonly DynamicConcurrencyLimiter _ssimConcurrency;
        private readonly DynamicConcurrencyLimiter _ffmpegSlots;
        private readonly string _instanceId = Guid.NewGuid().ToString("N");

        // ── 长驻 Worker 池 ──
        private System.Threading.Channels.Channel<FileWorkItem> _fileChannel = null!;
        private readonly CancellationTokenSource _fileWorkerCts = new();
        private readonly List<Task> _fileWorkers = new();
        private int _targetFileWorkers;

        private record struct FileWorkItem(string FilePath, int Index, PresetConfig Config, bool IsRetry,
            ConcurrentDictionary<int, EncodeResult> Results, SemaphoreSlim DoneSignal);
        private ConsoleCancelEventHandler? _cancelKeyHandler;

        private static readonly object _consoleLock = new();
        private CancellationTokenSource? _globalCts;

        private readonly ConcurrentDictionary<string, Task<QualityMetrics?>> _metricsTasks = new();

        private static void SafeWriteLine(string msg) { lock (_consoleLock) Console.WriteLine(msg); }

        private readonly ConcurrentDictionary<string, bool> _srcAlphaCache = new();
        private readonly ConcurrentDictionary<string, string> _pngCache = new();  // ★ PNG 转换 LRU：同输入避免重复 ffmpeg

        /// <summary>路径 → 内容指纹映射，用于跨会话稳定标识文件（目录重命名/移动后仍可匹配）</summary>
        private readonly ConcurrentDictionary<string, string> _fileIdCache = new(StringComparer.OrdinalIgnoreCase);

        private int _maxFfmpegConcurrency;

        private int _disposed;

        private FileStream? _lockStream;

        private readonly IProcessRunner _processRunner;

        private readonly ILogger _logger;



        private readonly PresetConfig.IFileSystem _fs;   // 改为完整限定名




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
        private long _journalEventCount;   // 累计事件数，用于增量回放定位
        private bool _saveSnapshotPending; // ★ 快照标记：锁内设true，锁外执行I/O，避免阻塞高并发写入
        private int _snapshotInProgress;   // ★ CAS 哨兵，防止并发快照写入
        private Dictionary<string, QualityMetrics>? _resumeMetricsForExport;  // Resume 恢复的指标，供 ExportCsv 修补旧行








        // ===== 工具：将任意图片转为 PNG（SSIMULACRA2/Butteraugli 需要） =====
        private async Task<string?> ConvertToPngAsync(string inputPath, string tempDir,
            int? frameIndex = null, string? inputStreamSpec = null)
        {
            // ★ LRU 缓存：同一图像的 PNG 转换结果复用，避免 SSIMULACRA2/Butteraugli/XPSNR 各自 ffmpeg
            string cacheKey = frameIndex.HasValue
                ? $"{inputPath}|{frameIndex.Value}|{inputStreamSpec}"
                : inputPath;
            if (_pngCache.TryGetValue(cacheKey, out var cachedPath) && _fs.FileExists(cachedPath))
                return cachedPath;

            string tempPng = Path.Combine(tempDir, $"_tool_{Guid.NewGuid():N}.png");
            string cleanInput = NormalizePathForExternalTool(inputPath);
            string cleanOutput = NormalizePathForExternalTool(tempPng);
            string streamPart = inputStreamSpec != null ? $"-map {inputStreamSpec} " : "";
            string vfPart = frameIndex.HasValue
                ? $"-vf \"select='eq(n,{frameIndex.Value})'\" "
                : "";
            string args = $"-y -loglevel error -i \"{cleanInput}\" {streamPart}{vfPart}-pix_fmt rgb24 -frames:v 1 \"{cleanOutput}\"";
            var (ok, _) = await RunFfmpegExAsync(_ffmpegPath, args, TimeSpan.FromMinutes(1));
            if (ok && _fs.FileExists(tempPng))
            {
                _pngCache[cacheKey] = tempPng;
                return tempPng;
            }
            return null;
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
            // ★ 反向扫描：PNG 规范保证 IEND 是最后一个 chunk，从末尾向前找可避免
            //    像素数据中误匹配 0x00 0x00 0x00 0x00 "IEND" 字节序列
            int limit = bytes.Length - 12;

            // IEND CRC32 恒为 0xAE426082（大端序: AE 42 60 82）
            for (int i = limit; i >= 0; i--)
            {
                if (bytes[i] == 0x49 && bytes[i + 1] == 0x45 && bytes[i + 2] == 0x4E && bytes[i + 3] == 0x44)
                {
                    // 验证 length=0 且 CRC 正确
                    if (i >= 4 && bytes[i - 4] == 0 && bytes[i - 3] == 0 && bytes[i - 2] == 0 && bytes[i - 1] == 0
                        && i + 4 + 3 < bytes.Length
                        && bytes[i + 4] == 0xAE && bytes[i + 5] == 0x42 && bytes[i + 6] == 0x60 && bytes[i + 7] == 0x82)
                    {
                        return i + 8;
                    }
                }
            }

            return -1;
        }


        // ===== 批量帧提取（优化动图逐帧计算） =====
        private async Task<List<string>?> ExtractAllPngFramesAsync(
            string inputPath, string outputDir, string prefix,
            string? inputStreamSpec, int expectedCount,
            string pngPixFmt = "rgb24")
        {
            string cleanInput = NormalizePathForExternalTool(inputPath);
            string streamPart = inputStreamSpec != null ? $"-map {inputStreamSpec} " : "";
            string pattern = NormalizePathForExternalTool(Path.Combine(outputDir, $"{prefix}_%04d.png"));
            // -vsync 0: 保留原始帧，不丢帧不重复；-start_number 0: 从 0 开始编号
            string args = $"-y -loglevel error -i \"{cleanInput}\" {streamPart}-vsync 0 -start_number 0 -pix_fmt {pngPixFmt} -frames:v {expectedCount} \"{pattern}\"";
            var (ok, _) = await RunFfmpegExAsync(_ffmpegPath, args, TimeSpan.FromMinutes(3));
            if (!ok)
            {
                _logger.LogInfo($"[ADV] 批量 PNG 提取失败: {Path.GetFileName(inputPath)}");
                return null;
            }

            var files = new List<string>();
            for (int i = 0; i < expectedCount; i++)
            {
                string f = Path.Combine(outputDir, $"{prefix}_{i:D4}.png");
                if (_fs.FileExists(f)) files.Add(f);
                else break;
            }
            _logger.LogInfo($"[ADV] 批量 PNG 提取: {files.Count} 帧 ← {Path.GetFileName(inputPath)}");
            return files.Count > 0 ? files : null;
        }

        private async Task<(int w, int h, byte[] data)?> ExtractAllGrayFramesAsync(
            string inputPath, string? inputStreamSpec, int expectedFrames)
        {
            string cleanPath = NormalizePathForExternalTool(inputPath);
            string streamPart = inputStreamSpec != null ? $"-map {inputStreamSpec} " : "";

            var (w, h) = await GetResolutionAsync(inputPath);
            if (w <= 0 || h <= 0) return null;

            string args = $"-loglevel error -hide_banner -i \"{cleanPath}\" {streamPart}-vf format=gray -f rawvideo -pix_fmt gray pipe:1";
            Process? process = null;
            Task? copyTask = null;
            Task<string>? stderrTask = null;
            try
            {
                process = new Process
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
                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                _spawnedProcesses.Add(process);
                if (OperatingSystem.IsWindows()) { JobObjectHelper.AssignProcess(process); }

                // ★ 用 long 乘法避免 int 溢出（4K×260帧≈21.5亿 > int.MaxValue）
                long capacity = (long)w * h * expectedFrames;
                using var ms = capacity <= int.MaxValue ? new MemoryStream((int)capacity) : new MemoryStream();
                copyTask = process.StandardOutput.BaseStream.CopyToAsync(ms);
                stderrTask = process.StandardError.ReadToEndAsync();
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    _globalCts?.Token ?? CancellationToken.None, timeoutCts.Token);
                await process.WaitForExitAsync(linkedCts.Token);
                await Task.WhenAll(copyTask, stderrTask);

                if (process.ExitCode != 0) return null;
                byte[] data = ms.ToArray();
                if (data.Length != (long)w * h * expectedFrames)
                {
                    _logger.LogInfo($"[ADV] 灰度帧数据尺寸异常: 期望={(long)w * h * expectedFrames} 实际={data.Length}");
                    return null;
                }
                return (w, h, data);
            }
            catch (OperationCanceledException)
            {
                if (process != null && !process.HasExited)
                    try { process.Kill(entireProcessTree: true); } catch { }
                if (process != null)
                {
                    try { process.StandardOutput.BaseStream.Dispose(); } catch { }
                    try { process.StandardError.BaseStream.Dispose(); } catch { }
                    try
                    {
                        if (copyTask != null && stderrTask != null)
                            await Task.WhenAll(copyTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    catch { }
                }
                return null;
            }
            catch (Exception ex)
            {
                if (process != null && !process.HasExited)
                    try { process.Kill(entireProcessTree: true); } catch { }
                _logger.LogInfo($"[ADV] 批量灰度提取异常: {ex.Message}");
                return null;
            }
            finally
            {
                process?.Dispose();
            }
        }

        private async Task ComputeAdvancedMetricsInBackgroundAsync(
            string refPath, string distPath, string outputDir, string cacheKey,
            bool needSsimu2, bool needButter, bool needGmsd,
            CancellationToken cancellationToken, bool needXpsnr = false,
            string? inputPath = null, string? outputFileName = null,
            int frameCount = 1, string encodingPixFmt = "yuv444p",
            bool markSuccessWhenComplete = false)
        {
            string advName = inputPath != null ? Path.GetFileName(inputPath) : (outputFileName ?? "?");
            bool isAnimated = frameCount > 1;
            // ★ 根据编码像素格式推导 PNG 提取格式和 XPSNR 比较格式
            bool bit10 = encodingPixFmt.Contains("10le");
            bool bit12 = encodingPixFmt.Contains("12le");
            int bitDepth = bit12 ? 12 : bit10 ? 10 : 8;
            string pngPixFmt = (bit10 || bit12) ? "rgb48le" : "rgb24";
            string xpsnrPixFmt = encodingPixFmt.Replace("a", ""); // yuv444p / yuv444p10le / yuv420p 等
            // 保留后缀中的位深标记（10le/12le），只替换基础后缀 "p" → "p10le"/"p12le"
            if (bit10 && !xpsnrPixFmt.Contains("10le"))
                xpsnrPixFmt = Regex.Replace(xpsnrPixFmt, @"p(?=($|[^0-9]))", "p10le");
            else if (bit12 && !xpsnrPixFmt.Contains("12le"))
                xpsnrPixFmt = Regex.Replace(xpsnrPixFmt, @"p(?=($|[^0-9]))", "p12le");
            _logger.LogInfo($"[ADV] START {advName} ssimu2={needSsimu2} butter={needButter} gmsd={needGmsd} xpsnr={needXpsnr} frames={frameCount} pixFmt={encodingPixFmt} png={pngPixFmt} xpsnrFmt={xpsnrPixFmt}");
            await _advancedMetricSemaphore.WaitAsync(cancellationToken);
            try
            {
                string advancedTempDir = Path.Combine(outputDir, $"_advanced_metrics_{Guid.NewGuid():N}");
                try
                {
                    _fs.CreateDirectory(advancedTempDir);
                    string? cleanRef = await SanitizePngIfNeededAsync(refPath, advancedTempDir);

                    // ★ 动图逐帧平均 + per-frame CSV（批量提取优化）
                    if (isAnimated)
                    {
                        // ★ 根据编码像素格式判断是否有 Alpha：有Alpha→v:2(alpha)，无→v:1(动画)
                        bool hasAlpha = encodingPixFmt.Contains("yuva") || encodingPixFmt.Contains("rgba") ||
                                         encodingPixFmt.Contains("bgra") || encodingPixFmt.StartsWith("gbra", StringComparison.OrdinalIgnoreCase);
                        string distStream = hasAlpha ? "0:v:2" : "0:v:1";

                        // per-frame CSV
                        string csvPath = Path.Combine(outputDir, $"{advName}_perframe.csv");

                        // ★ 批量提取：ref 全帧 PNG + dist 全帧 PNG + GMSD 灰度 raw（仅 3 次 ffmpeg 调用）
                        var swBatch = Stopwatch.StartNew();
                        List<string>? refFrames = null, distFrames = null;
                        (int w, int h, byte[] data)? refGrayAll = null, distGrayAll = null;

                        var batchTasks = new List<Task>();
                        // XPSNR 逐帧也需要 ref/dist PNG 帧，包含 needXpsnr 避免帧提取被跳过
                        if (needSsimu2 || needButter || needXpsnr)
                        {
                            batchTasks.Add(Task.Run(async () =>
                                refFrames = await ExtractAllPngFramesAsync(cleanRef, advancedTempDir, "_ref", null, frameCount, pngPixFmt)));
                            batchTasks.Add(Task.Run(async () =>
                                distFrames = await ExtractAllPngFramesAsync(distPath, advancedTempDir, "_dist", distStream, frameCount, pngPixFmt)));
                        }
                        if (needGmsd)
                        {
                            batchTasks.Add(Task.Run(async () =>
                                refGrayAll = await ExtractAllGrayFramesAsync(cleanRef, null, frameCount)));
                            batchTasks.Add(Task.Run(async () =>
                                distGrayAll = await ExtractAllGrayFramesAsync(distPath, distStream, frameCount)));
                        }
                        await Task.WhenAll(batchTasks);
                        _logger.LogInfo($"[ADV] 批量帧提取完成: {swBatch.Elapsed.TotalSeconds:F1}s");

                        if (refGrayAll != null && distGrayAll != null)
                        {
                            int gs = refGrayAll.Value.w * refGrayAll.Value.h;
                            if (refGrayAll.Value.data.Length != gs * frameCount ||
                                distGrayAll.Value.data.Length != gs * frameCount)
                            {
                                _logger.LogInfo($"[ADV] GMSD 灰度数据尺寸异常，跳过");
                                refGrayAll = null; distGrayAll = null;
                            }
                        }

                        // ★ 并行逐帧处理（SSIMULACRA2/Butteraugli/XPSNR 外部工具和 GMSD 内存切片全并行）
                        var swFrames = Stopwatch.StartNew();
                        var csvLines = new string[frameCount + 1];  // 索引写入，无需锁
                        csvLines[0] = "Frame,SSIMULACRA2,Butteraugli_3norm,GMSD,XPSNR_Y,XPSNR_U,XPSNR_V,W_XPSNR";

                        var bagSsimu2 = new ConcurrentBag<double>();
                        var bagButterRaw = new ConcurrentBag<double>();
                        var bagButter3 = new ConcurrentBag<double>();
                        var bagGmsd = new ConcurrentBag<double>();
                        var bagXpsnrY = new ConcurrentBag<double>();
                        var bagXpsnrU = new ConcurrentBag<double>();
                        var bagXpsnrV = new ConcurrentBag<double>();
                        var bagXpsnrW = new ConcurrentBag<double>();

                        var frameTasks = Enumerable.Range(0, frameCount).Select(async i =>
                        {
                            if (cancellationToken.IsCancellationRequested) return;

                            string? refPng = refFrames?.ElementAtOrDefault(i);
                            string? distPng = distFrames?.ElementAtOrDefault(i);

                            double? frameSsimu2 = null, frameButter3 = null, frameGmsd = null;
                            double? frameXpsnrY = null, frameXpsnrU = null, frameXpsnrV = null;

                            if (refPng != null && distPng != null)
                            {
                                if (needSsimu2 && needButter)
                                {
                                    var tSsimu2 = ComputeSSIMULACRA2Async(refPng, distPng);
                                    var tButter = ComputeButteraugliAsync(refPng, distPng, advancedTempDir);
                                    await Task.WhenAll(tSsimu2, tButter);
                                    if (tSsimu2.Result.HasValue) { bagSsimu2.Add(tSsimu2.Result.Value); frameSsimu2 = tSsimu2.Result; }
                                    var br = tButter.Result;
                                    if (br.raw.HasValue && br.p3.HasValue) { bagButterRaw.Add(br.raw.Value); bagButter3.Add(br.p3.Value); frameButter3 = br.p3; }
                                }
                                else if (needSsimu2)
                                {
                                    var s = await ComputeSSIMULACRA2Async(refPng, distPng);
                                    if (s.HasValue) { bagSsimu2.Add(s.Value); frameSsimu2 = s; }
                                }
                                else if (needButter)
                                {
                                    var (raw, p3) = await ComputeButteraugliAsync(refPng, distPng, advancedTempDir);
                                    if (raw.HasValue && p3.HasValue) { bagButterRaw.Add(raw.Value); bagButter3.Add(p3.Value); frameButter3 = p3; }
                                }

                                // --skip-metrics xpsnr: 跳过动图逐帧 XPSNR
                                if (needXpsnr)
                                {
                                    var (y, u, v) = await ComputeXPSNRFrameAsync(refPng, distPng, xpsnrPixFmt);
                                    if (y.HasValue && u.HasValue && v.HasValue)
                                    {
                                        bagXpsnrY.Add(y.Value); bagXpsnrU.Add(u.Value); bagXpsnrV.Add(v.Value);
                                        double? w = ComputeWXPSNR(y.Value, u.Value, v.Value, bitDepth);
                                        if (w.HasValue) bagXpsnrW.Add(w.Value);
                                        frameXpsnrY = y; frameXpsnrU = u; frameXpsnrV = v;
                                    }
                                }
                            }

                            if (needGmsd && refGrayAll != null && distGrayAll != null)
                            {
                                int gs = refGrayAll.Value.w * refGrayAll.Value.h;
                                var refSlice = refGrayAll.Value.data.AsSpan(i * gs, gs);
                                var distSlice = distGrayAll.Value.data.AsSpan(i * gs, gs);
                                double g = ComputeGMSD_C(refSlice.ToArray(), refGrayAll.Value.w,
                                    refGrayAll.Value.h, distSlice.ToArray());
                                if (g >= 0) { bagGmsd.Add(g); frameGmsd = g; }
                            }

                            double? frameWXpsnr = frameXpsnrY.HasValue && frameXpsnrU.HasValue && frameXpsnrV.HasValue
                                ? ComputeWXPSNR(frameXpsnrY, frameXpsnrU, frameXpsnrV, bitDepth) : null;
                            csvLines[i + 1] = $"{i}," +  // ← 按帧索引写入，零竞争
                                $"{(frameSsimu2?.ToString("F6", CultureInfo.InvariantCulture) ?? "")}," +
                                $"{(frameButter3?.ToString("F6", CultureInfo.InvariantCulture) ?? "")}," +
                                $"{(frameGmsd?.ToString("F6", CultureInfo.InvariantCulture) ?? "")}," +
                                $"{(frameXpsnrY?.ToString("F4", CultureInfo.InvariantCulture) ?? "")}," +
                                $"{(frameXpsnrU?.ToString("F4", CultureInfo.InvariantCulture) ?? "")}," +
                                $"{(frameXpsnrV?.ToString("F4", CultureInfo.InvariantCulture) ?? "")}," +
                                $"{(frameWXpsnr?.ToString("F4", CultureInfo.InvariantCulture) ?? "")}";
                        });

                        await Task.WhenAll(frameTasks);
                        swFrames.Stop();
                        _logger.LogInfo($"[ADV] 逐帧计算完成: {swFrames.Elapsed.TotalSeconds:F1}s ({frameCount}帧)");

                        // ★ 保存逐帧 CSV（UTF-8 + BOM）
                        string csvContent = "\uFEFF" + string.Join("\n", csvLines) + "\n";
                        await _fs.WriteAllBytesAsync(csvPath, System.Text.Encoding.UTF8.GetBytes(csvContent));
                        _logger.LogInfo($"[ADV] 逐帧 CSV 已保存: {csvPath} ({frameCount}行)");

                        int ssimu2Count = bagSsimu2.Count, butterCount = bagButter3.Count, gmsdCount = bagGmsd.Count, xpsnrCount = bagXpsnrY.Count;
                        if (ssimu2Count > 0) { UpdateCachedMetrics(cacheKey, m => m.SSIMULACRA2 = bagSsimu2.Sum() / ssimu2Count); _logger.LogInfo($"[ADV] SSIMU2 DONE {advName} ={bagSsimu2.Sum() / ssimu2Count:F2} ({ssimu2Count}帧)"); }
                        if (butterCount > 0)
                        {
                            UpdateCachedMetrics(cacheKey, m => m.Butteraugli_Raw = bagButterRaw.Sum() / butterCount);
                            UpdateCachedMetrics(cacheKey, m => m.Butteraugli_3norm = bagButter3.Sum() / butterCount);
                            _logger.LogInfo($"[ADV] BUTTER DONE {advName} 3norm={bagButter3.Sum() / butterCount:F4} ({butterCount}帧)");
                        }
                        if (gmsdCount > 0) { UpdateCachedMetrics(cacheKey, m => m.GMSD = bagGmsd.Sum() / gmsdCount); _logger.LogInfo($"[ADV] GMSD DONE {advName} ={bagGmsd.Sum() / gmsdCount:F4} ({gmsdCount}帧)"); }
                        if (xpsnrCount > 0)
                        {
                            double avgY = bagXpsnrY.Sum() / xpsnrCount, avgU = bagXpsnrU.Sum() / xpsnrCount, avgV = bagXpsnrV.Sum() / xpsnrCount;
                            double? avgW = bagXpsnrW.Count > 0 ? bagXpsnrW.Sum() / bagXpsnrW.Count : null;
                            UpdateCachedMetrics(cacheKey, m =>
                            {
                                m.XPSNR_Y = avgY; m.XPSNR_U = avgU;
                                m.XPSNR_V = avgV; m.W_XPSNR = avgW;
                            });
                            _logger.LogInfo($"[ADV] XPSNR DONE {advName} Y={avgY:F2} U={avgU:F2} V={avgV:F2} W={avgW:F2} ({xpsnrCount}帧)");
                        }
                    }
                    else
                    {
                        // 静图路径（原有逻辑）
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
                                if (s.HasValue) { UpdateCachedMetrics(cacheKey, m => m.SSIMULACRA2 = s); _logger.LogInfo($"[ADV] SSIMU2 DONE {advName} ={s.Value:F2}"); }
                            }
                            catch (Exception ex) { _logger.LogInfo($"[ADV] SSIMU2 FAIL {advName}: {ex.Message}"); }
                        }

                        if (needButter && refPng != null && distPng != null)
                        {
                            try
                            {
                                var (raw, p3) = await ComputeButteraugliAsync(refPng, distPng, advancedTempDir);
                                if (raw.HasValue) UpdateCachedMetrics(cacheKey, m => m.Butteraugli_Raw = raw);
                                if (p3.HasValue) { UpdateCachedMetrics(cacheKey, m => m.Butteraugli_3norm = p3); _logger.LogInfo($"[ADV] BUTTER DONE {advName} 3norm={p3.Value:F4}"); }
                            }
                            catch (Exception ex) { _logger.LogInfo($"[ADV] BUTTER FAIL {advName}: {ex.Message}"); }
                        }

                        if (needGmsd)
                        {
                            try
                            {
                                var g = await ComputeGMSDAsync(cleanRef, distPath, 1);
                                if (g.HasValue) { UpdateCachedMetrics(cacheKey, m => m.GMSD = g); _logger.LogInfo($"[ADV] GMSD DONE {advName} ={g.Value:F4}"); }
                            }
                            catch (Exception ex) { _logger.LogInfo($"[ADV] GMSD FAIL {advName}: {ex.Message}"); }
                        }
                    }

                    if (cleanRef != refPath && _fs.FileExists(cleanRef))
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
            // ★ 检查所需指标是否就绪；若外部工具缺失导致指标无法计算，则视为"不适用"并放行
            bool allMetricsReady = true;
            if (_cache.TryGetMetrics(cacheKey, out var finalMetrics))
            {
                if (needSsimu2 && !finalMetrics!.SSIMULACRA2.HasValue)
                {
                    if (EncoderUtils.FindExecutable("ssimulacra2") == null)
                        _logger.LogInfo($"[METRICS] SSIMULACRA2 工具未安装，跳过");
                    else
                        allMetricsReady = false;
                }
                if (needButter && (!finalMetrics!.Butteraugli_Raw.HasValue || !finalMetrics.Butteraugli_3norm.HasValue))
                {
                    if (EncoderUtils.FindExecutable("butteraugli_main") == null)
                        _logger.LogInfo($"[METRICS] Butteraugli 工具未安装，跳过");
                    else
                        allMetricsReady = false;
                }
                // GMSD 是内部实现，失败时记录日志并放行，避免单次异常阻塞整个流水线
                if (needGmsd && !finalMetrics!.GMSD.HasValue)
                    _logger.LogInfo($"[METRICS] GMSD 计算失败，跳过");
                if (needXpsnr && NeedsXpsnrMetrics(finalMetrics!))
                {
                    _logger.LogInfo($"[METRICS] XPSNR 璁＄畻鏈畬鎴愶紝绛夊緟 Resume 琛ョ畻");
                    allMetricsReady = false;
                }
            }
            else { allMetricsReady = false; }

            if (allMetricsReady)
            {
                _logger.LogInfo($"[ADV] COMPLETE {advName}");
                // ★ 方案二：高级指标写入 Journal（唯一权威状态源），不再回填 CSV
                if (inputPath != null && _cache.TryGetMetrics(cacheKey, out var m))
                {
                    _logger.LogInfo($"[JOURNAL] metrics: {advName} SSIMU2={m!.SSIMULACRA2?.ToString("F2")} Butter3={m.Butteraugli_3norm?.ToString("F4")} GMSD={m.GMSD?.ToString("F4")} XPSNR={m.W_XPSNR?.ToString("F2")}");
                    AppendJournal(inputPath, JournalEventTypes.Metrics, new
                    {
                        ssimu2 = m!.SSIMULACRA2,
                        butterRaw = m.Butteraugli_Raw,
                        butter3 = m.Butteraugli_3norm,
                        gmsd = m.GMSD,
                        xpsnrY = m.XPSNR_Y,
                        xpsnrU = m.XPSNR_U,
                        xpsnrV = m.XPSNR_V,
                        wxpsnr = m.W_XPSNR
                    });
                }
                if (inputPath != null)
                {
                    _logger.LogInfo($"[JOURNAL] success: {advName}");
                    AppendJournal(inputPath, JournalEventTypes.Success);
                }
                _progress.MarkFileProcessed();
            }
            else
            {
                _logger.LogInfo($"[ADV] INCOMPLETE {advName} ssimu2={needSsimu2} butter={needButter} gmsd={needGmsd}");
            }
            _guiProgress?.Report(Math.Min(100, _progress.ProcessedCount * 100 / Math.Max(1, _progress.TotalFiles)));
        }

        // ── TryFlushCsvRow 已删除（方案二：Journal 是唯一权威源，CSV 仅最终导出） ──

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
            _logger.LogInfo($"[SSIMU2] 调用: {exe} {args}");
            var (exitCode, stdout, stderr) = await _processRunner.RunAsync(
                exe, args, TimeSpan.FromMinutes(2), _globalCts?.Token ?? default);
            _logger.LogInfo($"[SSIMU2] 返回: exit={exitCode}, val={stdout.Trim()}");
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
            _logger.LogInfo($"[BUTTER] 调用: {exe} {args}");
            var (exitCode, stdout, stderr) = await _processRunner.RunAsync(
                exe, args, TimeSpan.FromMinutes(2), _globalCts?.Token ?? default);
            _logger.LogInfo($"[BUTTER] 返回: exit={exitCode}, val={stdout.Trim()}");

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
        private async Task<double?> ComputeGMSDAsync(string refPath, string distPath,
            int frameCount = 1)
        {
            if (frameCount <= 1)
            {
                // 单帧路径
                var refGray = await DecodeGrayRawAsync(refPath);
                if (refGray == null) return null;
                var distGray = await DecodeGrayRawAsync(distPath);
                if (distGray == null) return null;
                if (refGray.Value.w != distGray.Value.w || refGray.Value.h != distGray.Value.h)
                    return null;
                double score = ComputeGMSD_C(refGray.Value.data, refGray.Value.w, refGray.Value.h,
                                              distGray.Value.data);
                return score >= 0 ? score : null;
            }

            // ★ 动图逐帧平均
            double sum = 0;
            int valid = 0;
            // dist 是编码后的 AVIF，默认 v:1（非 Alpha 动画）
            string distStream = "0:v:1";
            for (int i = 0; i < frameCount; i++)
            {
                var refGray = await DecodeGrayRawAsync(refPath, i);
                if (refGray == null) continue;
                var distGray = await DecodeGrayRawAsync(distPath, i, distStream);
                if (distGray == null) continue;
                if (refGray.Value.w != distGray.Value.w || refGray.Value.h != distGray.Value.h)
                    continue;
                double score = ComputeGMSD_C(refGray.Value.data, refGray.Value.w, refGray.Value.h,
                                              distGray.Value.data);
                if (score >= 0)
                {
                    sum += score;
                    valid++;
                }
            }

            return valid > 0 ? sum / valid : null;
        }

        /// <summary> 计算单帧 GMSD（供动图逐帧循环调用）。distStream 为 AVIF 动画流选择器（"0:v:2"）。</summary>
        private async Task<double?> ComputeGMSDFrameAsync(string refPath, string distPath,
            int frameIndex, string distStream)
        {
            var refGray = await DecodeGrayRawAsync(refPath, frameIndex);
            if (refGray == null) return null;
            var distGray = await DecodeGrayRawAsync(distPath, frameIndex, distStream);
            if (distGray == null) return null;
            if (refGray.Value.w != distGray.Value.w || refGray.Value.h != distGray.Value.h)
                return null;
            double score = ComputeGMSD_C(refGray.Value.data, refGray.Value.w, refGray.Value.h,
                                          distGray.Value.data);
            return score >= 0 ? score : null;
        }

        /// <summary> 用 ffmpeg 将任意图片解码为 8 位灰度原始字节数组，并返回宽、高。失败返回 null。 </summary>
        private async Task<(int w, int h, byte[] data)?> DecodeGrayRawAsync(string imagePath,
            int? frameIndex = null, string? inputStreamSpec = null)
        {
            string cleanPath = NormalizePathForExternalTool(imagePath);
            string streamPart = inputStreamSpec != null ? $"-map {inputStreamSpec} " : "";
            string vfPart = frameIndex.HasValue
                ? $"-vf \"select='eq(n,{frameIndex.Value})',format=gray\""
                : "-vf format=gray";
            string args = $"-loglevel error -hide_banner -i \"{cleanPath}\" {streamPart}{vfPart} -frames:v 1 -f rawvideo -pix_fmt gray pipe:1";

            // ★ 预探测分辨率以预分配 MemoryStream 容量
            var (estW, estH) = await GetResolutionAsync(imagePath);
            int estimatedSize = estW > 0 && estH > 0 ? estW * estH : 1920 * 1080;  // 兜底: 1080p
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
                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                // ★ 加入进程追踪列表，确保 FinalCleanup 可终止
                _spawnedProcesses.Add(process);
                if (OperatingSystem.IsWindows()) { JobObjectHelper.AssignProcess(process); }

                using var ms = new MemoryStream(estimatedSize);
                var copyTask = process.StandardOutput.BaseStream.CopyToAsync(ms);
                var stderrTask = process.StandardError.ReadToEndAsync();

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    _globalCts?.Token ?? CancellationToken.None, timeoutCts.Token);
                try
                {
                    await process.WaitForExitAsync(linkedCts.Token);
                    await Task.WhenAll(copyTask, stderrTask);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                    {
                        try { process.Kill(entireProcessTree: true); } catch { }
                    }
                    try { process.StandardOutput.BaseStream.Dispose(); } catch { }
                    try { process.StandardError.BaseStream.Dispose(); } catch { }
                    try { await Task.WhenAll(copyTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
                    return null;
                }

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
            _logger.LogInfo($"[OUTPUT] inputDir={safeInputDir} file={safeInputPath} rel={relPath} dir={relDir ?? "(root)"}");
            string targetDir = string.IsNullOrEmpty(relDir)
                ? _outputDir
                : Path.Combine(_outputDir, relDir);
            _fs.CreateDirectory(targetDir);
            string candidate = Path.Combine(targetDir, fileName);
            switch (_config.FileConflictStrategy)
            {
                case PresetConfig.ConflictStrategy.Overwrite:
                case PresetConfig.ConflictStrategy.Skip:
                    string conflictKey = NormalizePathForExternalTool(candidate).ToLowerInvariant();
                    if (_allocatedOutputs.ContainsKey(conflictKey))
                        _logger.LogInfo($"[NAME-CONFLICT] 输出重名 (策略={_config.FileConflictStrategy}): {candidate}");
                    _allocatedOutputs.TryAdd(conflictKey, 0);
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

        /// <summary> 委托到 EncodeHelpers，消除重复定义 </summary>
        private static string NormalizePathForExternalTool(string path) =>
            EncodeHelpers.NormalizePathForExternalTool(path);

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
                    ? Math.Max(1, cpuCount)                    // 硬件编码器：用物理核心数，现代CPU有超线程
                    : Math.Max(1, (int)Math.Sqrt(cpuCount));   // 软件编码器：核心数平方根
            }
            if (config.MaxJobs < 1) config.MaxJobs = 1;

            int ssimSlots = Math.Max(2, cpuCount);   // 质量评估仍可使用全部核心

            _maxFfmpegConcurrency = config.MaxJobs;
            _ssimConcurrency = new DynamicConcurrencyLimiter(ssimSlots);
            _ffmpegSlots = new DynamicConcurrencyLimiter(config.MaxJobs);

            // ★ 长驻文件 Worker 池
            _targetFileWorkers = config.MaxJobs;
            _fileChannel = System.Threading.Channels.Channel.CreateBounded<FileWorkItem>(
                new System.Threading.Channels.BoundedChannelOptions(config.MaxJobs * 2)
                { FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait });
            for (int i = 0; i < config.MaxJobs; i++)
                lock (_fileWorkers) { _fileWorkers.Add(FileWorkerLoopAsync()); }

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

        /// <summary>运行时动态调整最大并行编码数（不中断正在执行的任务）。</summary>
        public int SetMaxJobs(int maxJobs)
        {
            int result = _ffmpegSlots.SetMax(Math.Max(1, maxJobs));
            _config.MaxJobs = result;
            _maxFfmpegConcurrency = result;
            SetFileWorkerCount(result);
            _logger.LogInfo($"[CONCURRENCY] 并行数更新: {result}");
            return result;
        }

        private void SetFileWorkerCount(int target)
        {
            target = Math.Max(1, target);
            int diff = target - _targetFileWorkers;
            _targetFileWorkers = target;
            for (int i = 0; i < diff; i++)
                lock (_fileWorkers) { _fileWorkers.Add(FileWorkerLoopAsync()); }
            for (int i = 0; i < -diff; i++)
                _ = Task.Run(async () => { try { await _fileChannel.Writer.WriteAsync(default); } catch { } });  // 毒丸：用 WriteAsync 防 Channel 满时丢失
        }

        private async Task FileWorkerLoopAsync()
        {
            try
            {
                await foreach (var item in _fileChannel.Reader.ReadAllAsync(
                    _fileWorkerCts.Token))
                {
                    if (item.Config == null)  // 毒丸
                    {
                        return;
                    }
                    try
                    {
                        var r = await ProcessSingleFileAsync(item.FilePath, item.Index, item.Config, item.IsRetry);
                        if (r != null)
                            item.Results[r.Index] = r;
                    }
                    catch (OperationCanceledException) { return; }  // 用户取消：立即停止Worker
                    catch (Exception ex)
                    {
                        _logger.LogError($"Worker 异常: {item.FilePath} - {ex.Message}");
                        var failResult = new EncodeResult
                        {
                            Index = item.Index,
                            FileName = GetOutputFileName(item.FilePath, item.Index),
                            OriginalFileName = Path.GetFileName(item.FilePath),
                            InputPath = item.FilePath,
                            Success = false,
                            Skipped = false,
                            ErrorMessage = $"异常: {ex.Message}",
                            TotalTime = TimeSpan.Zero,
                            // ★ 重试失败不再计入进度（首次失败已计数）
                            CountFailureInProgress = !item.IsRetry
                        };
                        item.Results[item.Index] = failResult;
                        MarkProcessed(failResult);
                    }
                    finally
                    {
                        // ★ 防竞态：ProcessFilesAsync 取消时 doneSignal 被 using Dispose，
                        //    Worker 的 finally 对已释放 SemaphoreSlim 调 Release 抛 ObjectDisposedException，
                        //    catch 静默吞掉避免未观察任务异常
                        try { item.DoneSignal?.Release(); }
                        catch (ObjectDisposedException) { }
                    }
                }
            }
            catch (OperationCanceledException) { }
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
            string escInput = EncodeHelpers.EscapeArg(input);
            string escOutput = EncodeHelpers.EscapeArg(output);
            string args = $"-loglevel error -hide_banner -i \"{escInput}\" -vf \"{filter}\" -pix_fmt {pixFmt} \"{escOutput}\"";

            (bool ok, string err) = await RunFfmpegExAsync(_ffmpegPath, args, TimeSpan.FromMinutes(2));
            if (!ok)
                throw new Exception($"缩放失败: {err}");
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
                for (int retry = 0; retry < 20; retry++)
                {
                    try
                    {
                        _journalWriter = new StreamWriter(_journalPath, append: true, Encoding.UTF8)
                        { AutoFlush = true };
                        break;
                    }
                    catch (IOException)
                    {
                        if (retry >= 19)
                        {
                            var msg = $"[FATAL] 无法打开 journal 文件（已重试 20 次）: {_journalPath}，可能被其他进程锁定或磁盘故障";
                            _logger?.LogError(msg);
                            throw new IOException(msg);
                        }
                        Thread.Sleep(200);
                    }
                }
                _lastSnapshotTime = DateTime.UtcNow;
                _journalCountSinceSnapshot = 0;

                // ★ Resume 时从已有 journal 恢复事件计数，确保增量回放偏移量正确
                if (_config.Resume && _fs.FileExists(_journalPath))
                {
                    try
                    {
                        _journalEventCount = File.ReadLines(_journalPath).Count();
                        _logger?.LogInfo($"[JOURNAL] Resume 模式：已恢复 {_journalEventCount} 条历史事件");
                    }
                    catch { _journalEventCount = 0; }
                }
            }
        }

        private void AppendJournal(string file, string evt, object? extra = null)
        {
            lock (_journalLock)
            {
                if (_journalWriter == null) return;
                var obj = new Dictionary<string, object>
                {
                    ["schema"] = JournalEventTypes.CurrentSchemaVersion,
                    ["ts"] = DateTime.UtcNow.ToString("o"),
                    ["file"] = file,
                    ["evt"] = evt
                };
                // 附加 FileId：跨会话稳定标识，目录重命名/移动后仍可匹配
                if (_fileIdCache.TryGetValue(file, out var fid))
                    obj["fileId"] = fid;
                if (extra != null)
                {
                    foreach (var prop in extra.GetType().GetProperties())
                        obj[prop.Name.ToLower()] = prop.GetValue(extra) ?? "";
                }
                string line = System.Text.Json.JsonSerializer.Serialize(obj);
                _journalWriter.WriteLine(line);
                _journalWriter.Flush();  // 逐行刷盘
                _journalEventCount++;
                _journalCountSinceSnapshot++;

                // ★ 快照标记（锁内仅标记，锁外执行 I/O，避免阻塞高并发写入）
                if (_journalCountSinceSnapshot >= 500)
                {
                    _journalCountSinceSnapshot = 0;
                    _saveSnapshotPending = true;
                }
            }
            // 锁外异步执行快照 I/O
            if (_saveSnapshotPending)
            {
                if (Interlocked.CompareExchange(ref _snapshotInProgress, 1, 0) == 0)
                {
                    try
                    {
                        var (oldDone, oldMetrics, _, _) = LoadSnapshot();
                        var (newDone, newMetrics, _, _) = ReplayJournalWithMetrics(0);
                        SaveSnapshot(oldDone.Union(newDone),
                            MergeMetrics(oldMetrics, newMetrics));
                    }
                    catch { }
                    Interlocked.Exchange(ref _snapshotInProgress, 0);
                    _saveSnapshotPending = false;  // ★ 在 CAS 块内重置，避免丢失其他线程的并发触发
                }
            }
        }

        /// <summary>
        /// 回放 journal，返回完成文件列表 + 指标状态。
        /// skipLines 用于增量回放：跳过前 N 行（已通过 snapshot 恢复的事件）。
        /// </summary>
        private (HashSet<string> completed, Dictionary<string, QualityMetrics> metrics,
            Dictionary<string, string> fileIdToPath, HashSet<string> encodedOnly)
            ReplayJournalWithMetrics(int skipLines)
        {
            var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var metrics = new Dictionary<string, QualityMetrics>(StringComparer.OrdinalIgnoreCase);
            var fileIdToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var encodedOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ReplayJournalCore(skipLines, null, completed, metrics, fileIdToPath, encodedOnly);
            return (completed, metrics, fileIdToPath, encodedOnly);
        }

        // ReplayJournal(DateTime?, Dictionary?) 已废弃——所有调用者应使用 ReplayJournalWithMetrics
        // 保留此方法仅为向后兼容，但不支持 encoded 事件（传入空 encodedOnly 防止 NRE）
        private HashSet<string> ReplayJournal(DateTime? since, Dictionary<string, QualityMetrics>? metricsOut = null)
        {
            var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var encodedOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ReplayJournalCore(0, since, completed, metricsOut, encodedOnly: encodedOnly);
            return completed;
        }

        /// <summary>共享的 journal 回放核心：解析 NDJSON 行，填充 completed + metrics + fileIdMap。</summary>
        private void ReplayJournalCore(
            int skipLines, DateTime? since,
            HashSet<string> completed, Dictionary<string, QualityMetrics>? metricsOut,
            Dictionary<string, string>? fileIdToPath = null,
            HashSet<string>? encodedOnly = null)
        {
            if (!_fs.FileExists(_journalPath)) return;

            try
            {
                var lines = File.ReadAllLines(_journalPath);
                for (int i = skipLines; i < lines.Length; i++)
                {
                    string line = lines[i];
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

                            string evt = evtEl.GetString() ?? "";
                            string file = fileEl.GetString() ?? "";

                            // 提取 FileId（v2+ 事件），建立 FileId → path 映射用于 Resume 匹配
                            if (fileIdToPath != null &&
                                root.TryGetProperty("fileId", out var fidEl))
                            {
                                string fid = fidEl.GetString() ?? "";
                                if (fid.Length > 0 && !fileIdToPath.ContainsKey(fid))
                                    fileIdToPath[fid] = file;
                            }

                            // ★ "encoded" 仅表示编码完成（可跳过重编），不等于完全完成
                            // 只有 "success" 才表示文件完整（编码+指标），Resume 时跳过
                            if (evt == "success")
                                completed.Add(file);
                            if (evt == "encoded")
                            {
                                encodedOnly!.Add(file);
                                _resumeEncodedFiles[file] = CreateResumeEncodedInfo(file, root);
                            }

                            if (metricsOut != null && evt == "metrics")
                            {
                                var m = new QualityMetrics();
                                // ★ AppendJournal 用 prop.Name.ToLower() 写入，必须用小写 key 读取
                                if (root.TryGetProperty("ssimu2", out var s2) && s2.ValueKind == JsonValueKind.Number) m.SSIMULACRA2 = s2.GetDouble();
                                if (root.TryGetProperty("butterraw", out var br) && br.ValueKind == JsonValueKind.Number) m.Butteraugli_Raw = br.GetDouble();
                                if (root.TryGetProperty("butter3", out var b3) && b3.ValueKind == JsonValueKind.Number) m.Butteraugli_3norm = b3.GetDouble();
                                if (root.TryGetProperty("gmsd", out var gm) && gm.ValueKind == JsonValueKind.Number) m.GMSD = gm.GetDouble();
                                if (root.TryGetProperty("xpsnry", out var xy) && xy.ValueKind == JsonValueKind.Number) m.XPSNR_Y = xy.GetDouble();
                                if (root.TryGetProperty("xpsnru", out var xu) && xu.ValueKind == JsonValueKind.Number) m.XPSNR_U = xu.GetDouble();
                                if (root.TryGetProperty("xpsnrv", out var xv) && xv.ValueKind == JsonValueKind.Number) m.XPSNR_V = xv.GetDouble();
                                if (root.TryGetProperty("wxpsnr", out var wx) && wx.ValueKind == JsonValueKind.Number) m.W_XPSNR = wx.GetDouble();
                                metricsOut[file] = MergeQualityMetrics(
                                    metricsOut.TryGetValue(file, out var existingMetrics) ? existingMetrics : null,
                                    m);
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // ★ 单行 JSON 损坏不应丢弃后续有效事件（崩溃时部分写入可能导致一行损坏）。
                        //    break 会让所有后续事件丢失 → 已完成的文件被重新编码。改为 continue。
                        _logger?.LogInfo($"[JOURNAL] 行 {i + 1} JSON 损坏，跳过该行");
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogInfo($"[JOURNAL] 回放异常: {ex.Message}");
            }
        }

        internal static QualityMetrics MergeQualityMetrics(QualityMetrics? existing, QualityMetrics? incoming)
        {
            if (existing == null)
                return CloneQualityMetrics(incoming);

            var result = CloneQualityMetrics(existing);
            if (incoming == null) return result;

            // Journal/snapshot payloads omit scalar metrics; their default 0 means "not present".
            if (HasPersistedScalarMetric(incoming.SSIM)) result.SSIM = incoming.SSIM;
            if (HasPersistedScalarMetric(incoming.PSNR_Y)) result.PSNR_Y = incoming.PSNR_Y;
            if (HasPersistedScalarMetric(incoming.MS_SSIM)) result.MS_SSIM = incoming.MS_SSIM;
            if (HasPersistedScalarMetric(incoming.VMAF)) result.VMAF = incoming.VMAF;

            if (incoming.XPSNR_Y.HasValue) result.XPSNR_Y = incoming.XPSNR_Y;
            if (incoming.XPSNR_U.HasValue) result.XPSNR_U = incoming.XPSNR_U;
            if (incoming.XPSNR_V.HasValue) result.XPSNR_V = incoming.XPSNR_V;
            if (incoming.W_XPSNR.HasValue) result.W_XPSNR = incoming.W_XPSNR;
            if (incoming.SSIMULACRA2.HasValue) result.SSIMULACRA2 = incoming.SSIMULACRA2;
            if (incoming.Butteraugli_Raw.HasValue) result.Butteraugli_Raw = incoming.Butteraugli_Raw;
            if (incoming.Butteraugli_3norm.HasValue) result.Butteraugli_3norm = incoming.Butteraugli_3norm;
            if (incoming.GMSD.HasValue) result.GMSD = incoming.GMSD;

            return result;
        }

        private static QualityMetrics CloneQualityMetrics(QualityMetrics? metrics)
        {
            if (metrics == null) return new QualityMetrics();

            return new QualityMetrics
            {
                SSIM = metrics.SSIM,
                PSNR_Y = metrics.PSNR_Y,
                MS_SSIM = metrics.MS_SSIM,
                VMAF = metrics.VMAF,
                XPSNR_Y = metrics.XPSNR_Y,
                XPSNR_U = metrics.XPSNR_U,
                XPSNR_V = metrics.XPSNR_V,
                W_XPSNR = metrics.W_XPSNR,
                SSIMULACRA2 = metrics.SSIMULACRA2,
                Butteraugli_Raw = metrics.Butteraugli_Raw,
                Butteraugli_3norm = metrics.Butteraugli_3norm,
                GMSD = metrics.GMSD
            };
        }

        private static bool HasPersistedScalarMetric(double value)
        {
            return value != default && !double.IsNaN(value);
        }

        internal static bool NeedsXpsnrMetrics(QualityMetrics metrics)
        {
            return !metrics.XPSNR_Y.HasValue ||
                   !metrics.XPSNR_U.HasValue ||
                   !metrics.XPSNR_V.HasValue ||
                   !metrics.W_XPSNR.HasValue;
        }

        private static Dictionary<string, QualityMetrics> MergeMetrics(
            Dictionary<string, QualityMetrics>? a, Dictionary<string, QualityMetrics>? b)
        {
            var result = new Dictionary<string, QualityMetrics>(StringComparer.OrdinalIgnoreCase);
            if (a != null)
            {
                foreach (var kv in a)
                    result[kv.Key] = MergeQualityMetrics(
                        result.TryGetValue(kv.Key, out var existingMetrics) ? existingMetrics : null,
                        kv.Value);
            }
            if (b != null)
            {
                foreach (var kv in b)
                    result[kv.Key] = MergeQualityMetrics(
                        result.TryGetValue(kv.Key, out var existingMetrics) ? existingMetrics : null,
                        kv.Value);
            }
            return result;
        }

        private void SaveSnapshot(IEnumerable<string> completed,
            Dictionary<string, QualityMetrics>? completedMetrics = null)
        {
            if (string.IsNullOrEmpty(_snapshotPath)) return;
            try
            {
                // 构建指标快照：路径 → { 指标名: 值 }
                var metricsSnapshot = new Dictionary<string, Dictionary<string, double?>>(
                    StringComparer.OrdinalIgnoreCase);
                if (completedMetrics != null)
                {
                    foreach (var kv in completedMetrics)
                    {
                        var vals = new Dictionary<string, double?>();
                        if (kv.Value.SSIMULACRA2.HasValue) vals["ssimu2"] = kv.Value.SSIMULACRA2;
                        if (kv.Value.Butteraugli_Raw.HasValue) vals["butterRaw"] = kv.Value.Butteraugli_Raw;
                        if (kv.Value.Butteraugli_3norm.HasValue) vals["butter3"] = kv.Value.Butteraugli_3norm;
                        if (kv.Value.GMSD.HasValue) vals["gmsd"] = kv.Value.GMSD;
                        if (kv.Value.XPSNR_Y.HasValue) vals["xpsnrY"] = kv.Value.XPSNR_Y;
                        if (kv.Value.XPSNR_U.HasValue) vals["xpsnrU"] = kv.Value.XPSNR_U;
                        if (kv.Value.XPSNR_V.HasValue) vals["xpsnrV"] = kv.Value.XPSNR_V;
                        if (kv.Value.W_XPSNR.HasValue) vals["wxpsnr"] = kv.Value.W_XPSNR;
                        if (vals.Count > 0) metricsSnapshot[kv.Key] = vals;
                    }
                }

                var snapshot = new
                {
                    v = 4,
                    ts = DateTime.UtcNow.ToString("o"),
                    journalEventCount = _journalEventCount,
                    completed = completed.ToArray(),
                    metrics = metricsSnapshot,
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
                        _config.NativeTargetValue,
                        _config.TargetSSIM,
                        _config.XpsnrTargetValue,
                        _config.Ssimu2TargetValue,
                        _config.Butteraugli3TargetValue,
                        _config.GmsdTargetValue,
                        _config.PixelFormat,
                        _config.BitDepth,
                        _config.AutoSource,
                        _config.UserSetChroma,
                        _config.UserSetBitDepth,
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
                        _config.Verbose,
                        _config.EncoderCustomParams,
                        _config.Denoise,
                        _config.ArNrUseMaxFrames,
                        _config.RgbMode,
                        ApplyScalingToOutput = _config.ApplyScalingToOutput,
                        AnimatedCommand = _config.AnimatedCommand,
                        SafeEncodeTimeoutMinutes = _config.SafeEncodeTimeoutMinutes,
                        SearchEncodeTimeoutMinutes = _config.SearchEncodeTimeoutMinutes,
                        SkippedMetrics = _config.SkippedMetrics?.ToArray()
                    }
                };
                string tmp = _snapshotPath + ".tmp";
                File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(snapshot), Encoding.UTF8);
                // ★ 原子替换：Delete+Move有崩溃窗口，File.Move(overwrite:true)是原子操作
                File.Move(tmp, _snapshotPath, true);
                _journalCountSinceSnapshot = 0;
                _lastSnapshotTime = DateTime.UtcNow;

                // 快照已保存，Journal 保留（不删除，Resume 依赖它）
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[SNAPSHOT] 保存失败: {ex.Message}");
            }
        }

        private (HashSet<string> completed, Dictionary<string, QualityMetrics> metrics,
            string? configJson, string? inputDir) LoadSnapshot()
        {
            var emptyMetrics = new Dictionary<string, QualityMetrics>(StringComparer.OrdinalIgnoreCase);
            if (!_fs.FileExists(_snapshotPath)) return (new HashSet<string>(), emptyMetrics, null, null);
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

                // 提取完成文件列表
                var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("completed", out var arr))
                {
                    foreach (var el in arr.EnumerateArray())
                        completed.Add(el.GetString() ?? "");
                }

                // 提取指标状态（v4+ 格式）
                var metrics = new Dictionary<string, QualityMetrics>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("metrics", out var metricsEl))
                {
                    foreach (var kv in metricsEl.EnumerateObject())
                    {
                        var m = new QualityMetrics();
                        if (kv.Value.TryGetProperty("ssimu2", out var s2) && s2.ValueKind == JsonValueKind.Number) m.SSIMULACRA2 = s2.GetDouble();
                        if (kv.Value.TryGetProperty("butterRaw", out var br) && br.ValueKind == JsonValueKind.Number) m.Butteraugli_Raw = br.GetDouble();
                        if (kv.Value.TryGetProperty("butter3", out var b3) && b3.ValueKind == JsonValueKind.Number) m.Butteraugli_3norm = b3.GetDouble();
                        if (kv.Value.TryGetProperty("gmsd", out var gm) && gm.ValueKind == JsonValueKind.Number) m.GMSD = gm.GetDouble();
                        if (kv.Value.TryGetProperty("xpsnrY", out var xy) && xy.ValueKind == JsonValueKind.Number) m.XPSNR_Y = xy.GetDouble();
                        if (kv.Value.TryGetProperty("xpsnrU", out var xu) && xu.ValueKind == JsonValueKind.Number) m.XPSNR_U = xu.GetDouble();
                        if (kv.Value.TryGetProperty("xpsnrV", out var xv) && xv.ValueKind == JsonValueKind.Number) m.XPSNR_V = xv.GetDouble();
                        if (kv.Value.TryGetProperty("wxpsnr", out var wx) && wx.ValueKind == JsonValueKind.Number) m.W_XPSNR = wx.GetDouble();
                        metrics[kv.Name] = m;
                    }
                }

                return (completed, metrics, cfgJson, inputDir);
            }
            catch { }
            return (new HashSet<string>(), emptyMetrics, null, null);
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
            try { _globalCts?.Cancel(); } catch { }
            try { _fileChannel.Writer.Complete(); } catch { }
            try { _fileWorkerCts.Cancel(); } catch { }
            Task[] workers; lock (_fileWorkers) { workers = _fileWorkers.ToArray(); }
            try { Task.WaitAll(workers, TimeSpan.FromSeconds(10)); } catch { }
            try { FinalCleanup(); } catch { }
            try { _globalCts?.Dispose(); } catch { }
            _globalCts = null;
            try { _ssimConcurrency?.Dispose(); } catch { }
            try { _ffmpegSlots?.Dispose(); } catch { }
            if (_cancelKeyHandler != null)
                Console.CancelKeyPress -= _cancelKeyHandler;
            _advancedMetricSemaphore?.Dispose();
            try { _fileWorkerCts.Dispose(); } catch { }
            _lockStream?.Dispose();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Probe 探测

        private readonly ConcurrentDictionary<string, Task<ProbeInfo?>> _probeCache = new();

        /// <summary>每文件异步流范围内的动图标记（线程安全，替代共享字段）</summary>
        private readonly System.Threading.AsyncLocal<bool> _isAnimatedFile = new();

        private async Task<ProbeInfo?> GetProbeInfoAsync(string filePath)
        {
            string key = EncodeHelpers.GetNormalizedPathForCache(filePath);
            // ★ 使用 GetOrAdd 原子化创建+缓存，避免多个线程对同一文件启动重复 ffprobe
            var task = _probeCache.GetOrAdd(key, _ => ProbeFileCoreAsync(filePath));
            var result = await task;
            if (result == null)
                _probeCache.TryRemove(key, out _);
            return result;
        }

        private async Task<ProbeInfo?> ProbeFileCoreAsync(string filePath)
        {
            // 一次性 ffprobe 获取所有信息（含动图帧数，用 duration×fps 估算，无需 -count_frames）
            string args = $"-v error -select_streams v:0 -show_entries stream=pix_fmt,width,height,nb_frames,color_primaries,color_transfer,color_space,color_range,duration,r_frame_rate -of json {EncodeHelpers.EscapeArg(filePath)}";
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

                // 动图检测：nb_frames → duration×fps 回退
                int frameCount = 1;
                if (stream.TryGetProperty("nb_frames", out var nbf))
                {
                    if (nbf.ValueKind == JsonValueKind.Number)
                        frameCount = Math.Max(1, nbf.GetInt32());
                    else if (nbf.ValueKind == JsonValueKind.String && int.TryParse(nbf.GetString(), out int fc))
                        frameCount = Math.Max(1, fc);
                }
                double duration = 0;
                if (stream.TryGetProperty("duration", out var dur))
                {
                    if (dur.ValueKind == JsonValueKind.Number)
                        duration = dur.GetDouble();
                    else if (dur.ValueKind == JsonValueKind.String && double.TryParse(dur.GetString(), out double d))
                        duration = d;
                }
                double fps = 0;
                if (stream.TryGetProperty("r_frame_rate", out var rfr))
                {
                    string? rfrStr = rfr.GetString();
                    if (!string.IsNullOrEmpty(rfrStr))
                    {
                        var parts = rfrStr.Split('/');
                        if (parts.Length == 2 && double.TryParse(parts[0], out double num) && double.TryParse(parts[1], out double den) && den > 0)
                            fps = num / den;
                    }
                }
                // 容器不提供 nb_frames 时，用 duration × fps 估算
                if (frameCount <= 1 && duration > 0 && fps > 0)
                    frameCount = Math.Max(1, (int)Math.Ceiling(duration * fps));
                bool isAnimated = frameCount > 1 || duration > 0.5;

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
                    ColorRange = colorRange,
                    IsAnimated = isAnimated,
                    FrameCount = frameCount,
                    Duration = duration,
                    Fps = fps
                };
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






        // ★ GetNormalizedPathForCache 已统一至 EncodeHelpers.cs


        /// <summary>
        /// 使用 libvmaf 一次性计算 ref (原图) 与 dist (编码后) 的 SSIM / PSNR?Y / MS?SSIM / VMAF。
        /// 返回 QualityMetrics，失败返回 null。会自动处理分辨率不一致的情况（缩放至相同尺寸）。
        /// </summary>
        private async Task<QualityMetrics?> ComputeAllMetricsAsync(string refPath, string distPath,
            bool isAnimated = false, bool hasAlpha = false)
        {
            if (!EnsureFilesValid(refPath, distPath)) return null;

            string workDir = Environment.CurrentDirectory;
            string metricsDir = Path.Combine(workDir, $"avif_metrics_tmp_{_instanceId}");
            Directory.CreateDirectory(metricsDir);

            string jsonName = $"_metrics_{Guid.NewGuid():N}.json";
            string jsonPath = Path.Combine(metricsDir, jsonName);
            // ★ 相对路径：ffmpeg 进程的 CWD = Environment.CurrentDirectory = workDir
            string logPathSafe = $"avif_metrics_tmp_{_instanceId}/{jsonName}";

            try
            {
                var (w1, h1) = await GetResolutionAsync(refPath).WaitAsync(TimeSpan.FromSeconds(30));
                var (w2, h2) = await GetResolutionAsync(distPath).WaitAsync(TimeSpan.FromSeconds(30));

                string filter;
                if (isAnimated)
                {
                    // ★ 动图 AVIF 流布局因编码模式而异：
                    //    - 非 Alpha 动图：v:0=封面(1fps), v:1=动画帧(全帧率) → 指标应测 v:1
                    //    - Alpha 动图(filter_complex 双流)：v:0=色彩封面, v:1=Alpha封面, v:2=色彩动画, v:3=Alpha动画
                    //      指标应测 v:2（色彩动画帧），v:1 是 Alpha 封面只有 1 帧
                    //    使用错误流会导致 VMAF/SSIM 只计算封面帧，而非全部动画帧。
                    string distStream = hasAlpha ? "[1:v:2]" : "[1:v:1]";
                    if (w1 > 0 && h1 > 0 && w2 > 0 && h2 > 0 && (w1 != w2 || h1 != h2))
                    {
                        int w = Math.Min(w1, w2);
                        int h = Math.Min(h1, h2);
                        filter = $"[0:v]settb=AVTB,setpts=PTS-STARTPTS,scale={w}:{h}[ref];{distStream}settb=AVTB,setpts=PTS-STARTPTS,scale={w}:{h}[dist];[ref][dist]libvmaf=";
                    }
                    else
                    {
                        filter = $"[0:v]settb=AVTB,setpts=PTS-STARTPTS[ref];{distStream}settb=AVTB,setpts=PTS-STARTPTS[dist];[ref][dist]libvmaf=";
                    }
                }
                else if (w1 > 0 && h1 > 0 && w2 > 0 && h2 > 0 && (w1 != w2 || h1 != h2))
                {
                    int w = Math.Min(w1, w2);
                    int h = Math.Min(h1, h2);
                    filter = $"[0:v]scale={w}:{h}[ref];[1:v]scale={w}:{h}[dist];[ref][dist]libvmaf=";
                }
                else
                {
                    filter = $"[0:v][1:v]libvmaf=";
                }
                // 公共后缀
                filter += $"feature=name=psnr|name=float_ssim|name=float_ms_ssim:" +
                          $"model='version=vmaf_float_v0.6.1':" +
                          $"log_path={logPathSafe}:log_fmt=json:n_threads=4";

                string frameLimit = isAnimated ? "" : "-frames:v 1";
                string args = $"-loglevel error -hide_banner -i \"{refPath}\" -i \"{distPath}\" " +
                              $"-filter_complex \"{filter}\" {frameLimit} -f null -";

                var timeout = TimeSpan.FromMinutes(_config.SsimTimeoutMinutes);
                var (exitCode, stdout, stderr) = await _processRunner.RunAsync(
                    _ffmpegPath, args, timeout, _globalCts?.Token ?? default);

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

                // ★ 解析 VMAF JSON 时必须检查 ValueKind：动图/流不匹配时 libvmaf 可能输出 null，
                //    TryGetProperty 对 null 值返回 true，直接 GetDouble() 会抛 InvalidOperationException。
                //    未检查 ValueKind 将导致整个指标计算崩溃，高级指标（SSIMULACRA2/Butter3 等）全部跳过。
                double ssim = pooled.TryGetProperty("float_ssim", out var e) && e.ValueKind == JsonValueKind.Object ? e.GetProperty("mean").GetDouble() : 0;
                double ms_ssim = pooled.TryGetProperty("float_ms_ssim", out e) && e.ValueKind == JsonValueKind.Object ? e.GetProperty("mean").GetDouble() : 0;
                // VMAF 单独处理：Object 包含 Number 型的 mean 才是有效值，null/Object/其他均视为不可用
                double vmaf = double.NaN;
                if (pooled.TryGetProperty("vmaf", out e) && e.ValueKind == JsonValueKind.Object)
                {
                    var meanEl = e.GetProperty("mean");
                    if (meanEl.ValueKind == JsonValueKind.Number)
                        vmaf = meanEl.GetDouble();
                }
                double psnr_y = pooled.TryGetProperty("psnr_y", out e) && e.ValueKind == JsonValueKind.Object ? e.GetProperty("mean").GetDouble() : 0;
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
            // ★ {crf}：搜索模式显示范围，固定CRF模式显示实际值
            string crfDisplay = _config.UseCRFSearch && _config.MinCRF != _config.MaxCRF
                ? $"{_config.MinCRF}-{_config.MaxCRF}"
                : _config.BaseCRF.ToString();
            result = result
                .Replace("{encoder}", _config.Encoder)
                .Replace("{crf}", crfDisplay)
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
                    $"MaxJobs={_config.MaxJobs} " +
                    $"Metric={_config.MetricMode ?? "vmaf"} " +
                    $"Target={_config.GetEffectiveTarget()} " +
                    $"Recursive={_config.RecurseSubdirectories} " +
                    $"AutoSource={_config.AutoSource}");
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

                    // 加载快照（v4：含完成列表 + 指标 + 事件计数）
                    var (snapshotDone, snapshotMetrics, savedConfigJson, savedInputDir) = LoadSnapshot();

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
                            if (cfg.TryGetProperty("NativeTargetValue", out var ntv) && ntv.ValueKind != JsonValueKind.Null)
                                _config.NativeTargetValue = ntv.GetDouble();
                            if (cfg.TryGetProperty("TargetSSIM", out var tssim)) _config.TargetSSIM = tssim.GetDouble();
                            if (cfg.TryGetProperty("XpsnrTargetValue", out var xptv) && xptv.ValueKind != JsonValueKind.Null)
                                _config.XpsnrTargetValue = xptv.GetDouble();
                            if (cfg.TryGetProperty("Ssimu2TargetValue", out var s2tv) && s2tv.ValueKind != JsonValueKind.Null)
                                _config.Ssimu2TargetValue = s2tv.GetDouble();
                            if (cfg.TryGetProperty("Butteraugli3TargetValue", out var b3tv) && b3tv.ValueKind != JsonValueKind.Null)
                                _config.Butteraugli3TargetValue = b3tv.GetDouble();
                            if (cfg.TryGetProperty("GmsdTargetValue", out var gtv) && gtv.ValueKind != JsonValueKind.Null)
                                _config.GmsdTargetValue = gtv.GetDouble();
                            if (cfg.TryGetProperty("PixelFormat", out var pf)) _config.PixelFormat = pf.GetString();
                            if (cfg.TryGetProperty("BitDepth", out var bd)) _config.BitDepth = bd.GetInt32();
                            if (cfg.TryGetProperty("AutoSource", out var asrc)) _config.AutoSource = asrc.GetBoolean();
                            if (cfg.TryGetProperty("UserSetChroma", out var usc)) _config.UserSetChroma = usc.GetBoolean();
                            if (cfg.TryGetProperty("UserSetBitDepth", out var usb)) _config.UserSetBitDepth = usb.GetBoolean();
                            if (cfg.TryGetProperty("OutputNameFormat", out var ot)) _config.OutputNameFormat = ot.GetString()!;
                            if (cfg.TryGetProperty("RecurseSubdirectories", out var rc)) _config.RecurseSubdirectories = rc.GetBoolean();
                            if (cfg.TryGetProperty("SerialEncode", out var se)) _config.SerialEncode = se.GetBoolean();
                            if (cfg.TryGetProperty("UsePriorSearch", out var ps)) _config.UsePriorSearch = ps.GetBoolean();
                            if (cfg.TryGetProperty("UseProxySearch", out var px)) _config.UseProxySearch = px.GetBoolean();
                            if (cfg.TryGetProperty("SearchCpuUsed", out var sc)) _config.SearchCpuUsed = sc.GetInt32();
                            if (cfg.TryGetProperty("FinalCpuUsed", out var fc)) _config.FinalCpuUsed = fc.GetInt32();
                            if (cfg.TryGetProperty("MaxResolution", out var mr)) _config.MaxResolution = mr.GetInt32();
                            if (cfg.TryGetProperty("MaxJobs", out var mj)) _config.MaxJobs = mj.GetInt32();
                            if (cfg.TryGetProperty("FileConflictStrategy", out var fcs) &&
                                Enum.TryParse<PresetConfig.ConflictStrategy>(fcs.GetString(), out var strategy))
                                _config.FileConflictStrategy = strategy;
                            if (cfg.TryGetProperty("InputExtensions", out var ie)) _config.InputExtensions = ie.GetString();
                            if (cfg.TryGetProperty("EncodeTimeoutMinutes", out var et)) _config.EncodeTimeoutMinutes = et.GetInt32();
                            if (cfg.TryGetProperty("SearchTimeoutMinutes", out var st)) _config.SearchTimeoutMinutes = st.GetInt32();
                            if (cfg.TryGetProperty("SafeTimeoutMinutes", out var sf)) _config.SafeTimeoutMinutes = sf.GetInt32();
                            if (cfg.TryGetProperty("SsimTimeoutMinutes", out var ss)) _config.SsimTimeoutMinutes = ss.GetInt32();
                            if (cfg.TryGetProperty("SweepMode", out var sw)) _config.SweepMode = sw.GetBoolean();
                            if (cfg.TryGetProperty("DryRun", out var dr)) _config.DryRun = dr.GetBoolean();
                            if (cfg.TryGetProperty("Verbose", out var vb)) _config.Verbose = vb.GetBoolean();
                            if (cfg.TryGetProperty("EncoderCustomParams", out var ecp) && ecp.ValueKind != JsonValueKind.Null)
                                _config.EncoderCustomParams = ecp.GetString();
                            if (cfg.TryGetProperty("Denoise", out var dn))
                                _config.Denoise = dn.GetInt32();
                            if (cfg.TryGetProperty("ArNrUseMaxFrames", out var auf) && auf.ValueKind != JsonValueKind.Null)
                                _config.ArNrUseMaxFrames = auf.GetBoolean();
                            if (cfg.TryGetProperty("RgbMode", out var rgb) && rgb.ValueKind != JsonValueKind.Null)
                                _config.RgbMode = rgb.GetString();
                            if (cfg.TryGetProperty("SkippedMetrics", out var sm) && sm.ValueKind == JsonValueKind.Array)
                                _config.SkippedMetrics = sm.EnumerateArray()
                                    .Select(x => x.GetString())
                                    .Where(x => !string.IsNullOrWhiteSpace(x))
                                    .ToHashSet(StringComparer.OrdinalIgnoreCase)!;
                            _logger.LogInfo($"[RESUME] 已从快照恢复编码配置: Encoder={_config.Encoder} CRF={_config.BaseCRF}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogInfo($"[RESUME] 配置恢复失败: {ex.Message}，使用当前参数");
                        }
                    }
                    // ★ 方案二 + Snapshot v4：Snapshot 指标 + 增量 journal 回放
                    // 1. 从 Snapshot 恢复已有指标
                    long snapshotEventCount = 0;
                    try
                    {
                        string snapJson = File.ReadAllText(_snapshotPath, Encoding.UTF8);
                        using var snapDoc = JsonDocument.Parse(snapJson);
                        if (snapDoc.RootElement.TryGetProperty("journalEventCount", out var jec))
                            snapshotEventCount = jec.GetInt64();
                    }
                    catch { }

                    // 2. ★ 优化：一次性读取 journal + 单次遍历分拣所有事件类型（减少 I/O 67%）
                    var journalLines = Array.Empty<string>();
                    var resumeMetrics = new Dictionary<string, QualityMetrics>(snapshotMetrics, StringComparer.OrdinalIgnoreCase);
                    var deltaDone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var deltaMetrics = new Dictionary<string, QualityMetrics>(StringComparer.OrdinalIgnoreCase);
                    var fileIdToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var resumeEncodedOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        journalLines = File.ReadAllLines(_journalPath);
                        int startIdx = (int)Math.Min(snapshotEventCount, journalLines.Length);
                        for (int i = 0; i < journalLines.Length; i++)
                        {
                            string line = journalLines[i];
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            try
                            {
                                using var doc = JsonDocument.Parse(line);
                                var root = doc.RootElement;
                                if (root.TryGetProperty("evt", out var evtEl) &&
                                    root.TryGetProperty("file", out var fileEl))
                                {
                                    string evt = evtEl.GetString() ?? "";
                                    string file = fileEl.GetString() ?? "";
                                    bool isDelta = i >= startIdx;
                                    if (evt == "success")
                                    {
                                        if (isDelta) deltaDone.Add(file);
                                    }
                                    else if (evt == "encoded")
                                    {
                                        resumeEncodedOnly.Add(file);
                                        _resumeEncodedFiles[file] = CreateResumeEncodedInfo(file, root);
                                    }
                                    else if (isDelta && evt == "metrics")
                                    {
                                        var m = new QualityMetrics();
                                        if (root.TryGetProperty("ssimu2", out var s2) && s2.ValueKind == JsonValueKind.Number) m.SSIMULACRA2 = s2.GetDouble();
                                        if (root.TryGetProperty("butterraw", out var br) && br.ValueKind == JsonValueKind.Number) m.Butteraugli_Raw = br.GetDouble();
                                        if (root.TryGetProperty("butter3", out var b3) && b3.ValueKind == JsonValueKind.Number) m.Butteraugli_3norm = b3.GetDouble();
                                        if (root.TryGetProperty("gmsd", out var gm) && gm.ValueKind == JsonValueKind.Number) m.GMSD = gm.GetDouble();
                                        if (root.TryGetProperty("xpsnry", out var xy) && xy.ValueKind == JsonValueKind.Number) m.XPSNR_Y = xy.GetDouble();
                                        if (root.TryGetProperty("xpsnru", out var xu) && xu.ValueKind == JsonValueKind.Number) m.XPSNR_U = xu.GetDouble();
                                        if (root.TryGetProperty("xpsnrv", out var xv) && xv.ValueKind == JsonValueKind.Number) m.XPSNR_V = xv.GetDouble();
                                        if (root.TryGetProperty("wxpsnr", out var wx) && wx.ValueKind == JsonValueKind.Number) m.W_XPSNR = wx.GetDouble();
                                        deltaMetrics[file] = MergeQualityMetrics(
                                            deltaMetrics.TryGetValue(file, out var existingMetrics) ? existingMetrics : null,
                                            m);
                                    }
                                }
                                if (root.TryGetProperty("fileId", out var fidEl) &&
                                    root.TryGetProperty("file", out var fEl2))
                                {
                                    string fid = fidEl.GetString() ?? "";
                                    string fp = fEl2.GetString() ?? "";
                                    if (fid.Length > 0 && !fileIdToPath.ContainsKey(fid))
                                        fileIdToPath[fid] = fp;
                                }
                            }
                            catch (JsonException) { continue; }  // 仅跳过坏行，不丢弃后续有效事件
                        }
                    }
                    catch { }

                    // 3. 合并：Snapshot 指标 + 增量指标
                    foreach (var kv in deltaMetrics)
                        resumeMetrics[kv.Key] = MergeQualityMetrics(
                            resumeMetrics.TryGetValue(kv.Key, out var existingMetrics) ? existingMetrics : null,
                            kv.Value);

                    // 4. 保存引用供 ExportCsv 修补旧行
                    _resumeMetricsForExport = resumeMetrics;
                    _logger.LogInfo($"[RESUME] metrics restored: {resumeMetrics.Count} files");
                    foreach (var kv in resumeMetrics)
                        _logger.LogInfo($"[RESUME] metric: {Path.GetFileName(kv.Key)}");

                    // 5. 合并完成列表 + FileId 匹配（略去 FileId 映射的重复读取，已在上方单次遍历中完成）
                    var completed = new HashSet<string>(snapshotDone, StringComparer.OrdinalIgnoreCase);
                    foreach (var f in deltaDone) completed.Add(f);
                    _logger.LogInfo(
                        $"[RESUME] snapshot={snapshotDone.Count} + delta={deltaDone.Count} = completed={completed.Count}, metrics={resumeMetrics.Count}, encodedOnly={resumeEncodedOnly.Count}");

                    var completedById = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (path, _) in files)
                    {
                        if (completed.Contains(path)) continue;
                        if (_fileIdCache.TryGetValue(path, out var fid) &&
                            fileIdToPath.TryGetValue(fid, out var matchedPath) &&
                            completed.Contains(matchedPath))
                        {
                            completedById.Add(path);
                            _logger.LogInfo($"[RESUME] FileId 匹配: {Path.GetFileName(path)} ↔ {Path.GetFileName(matchedPath)}");
                        }
                    }
                    foreach (var f in completedById) completed.Add(f);

                    // 文件系统检查：Journal 标记完成或已编码，但文件被用户误删 → 重新编码
                    foreach (var (path, idx) in files)
                    {
                        if (completed.Contains(path) || resumeEncodedOnly.Contains(path)) continue;
                        string outPath = GetBaseOutputPathNoReserve(path, idx);
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

                List<EncodeResult?>? results = null;
                try
                {
                    results = await ProcessInitialBatchAsync(files);
                    results = await RetryFailuresAsync(results);
                }
                finally
                {
                    // Save snapshot from journal state only; EncodeResult.Success may only mean AVIF exists.
                    try
                    {
                        int totalFrames = results?.Where(r => r != null).Sum(r => r!.FrameCount) ?? 0;
                        int timeoutSec = Math.Max(120, 5 + (int)(totalFrames * 1.5));
                        await WaitForBackgroundMetricTasksAsync(
                            TimeSpan.FromSeconds(timeoutSec), "SNAPSHOT", requeueUnfinished: true);
                        SaveJournalBackedSnapshot("SNAPSHOT");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogInfo($"[SNAPSHOT] save failed: {ex.Message}");
                    }
                }

                if (results != null)
                {
                    await PrintSummaryAndExport(results);
                }
                else
                {
                    // results=null 时 CSV 行已由 AppendCsvRow 逐行写入磁盘，无需重新导出
                    _logger.LogInfo("[SUMMARY] results=null，CSV 数据已逐行写入磁盘，跳过最终导出");
                }
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

        /// <summary>计算文件稳定标识符（相对路径 + 大小 + 修改时间 → SHA256 前 16 位）。</summary>
        private string ComputeFileId(string filePath)
        {
            string relPath = Path.GetRelativePath(NormalizePathForExternalTool(_inputDir),
                NormalizePathForExternalTool(filePath));
            var fi = new FileInfo(filePath);
            return EncodeHelpers.Sha256(
                $"{relPath.ToLowerInvariant()}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}");
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
            _logger.LogInfo($"[SCAN] 扫描目录: {scanDir}, 递归={_config.RecurseSubdirectories}, 扩展名={string.Join(",", extensions)}");
            var sortedFiles = _fs.EnumerateFiles(scanDir, "*.*", searchOption)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                .OrderBy(f => f, new NaturalComparer())
                .Select((path, idx) => (path, index: idx + 1))
                .ToList();

            if (sortedFiles.Count == 0)
            {
                SafeWriteLine("未找到图片。");
                _logger.LogInfo("[SCAN] 未找到匹配文件");
                return null;
            }

            _progress.SetTotalFiles(sortedFiles.Count);
            SafeWriteLine($"待处理: {_progress.TotalFiles} 张\n");
            _logger.LogInfo($"[SCAN] 找到 {sortedFiles.Count} 个文件: {string.Join(", ", sortedFiles.Take(5).Select(f => Path.GetFileName(f.path)))}");

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

            // ★ 预计算所有文件的 FileId，填充缓存供 journal/snapshot 使用
            foreach (var (path, _) in processingOrder)
                _fileIdCache[path] = ComputeFileId(path);

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
            // 使用 Result 中保存的完整输入路径，不再拼接
            var retryFiles = failures.Select(f => (filePath: f!.InputPath, index: f.Index)).ToList();

            // 删除已有的输出文件，避免干扰
            foreach (var (filePath, index) in retryFiles)
            {
                string outPath = GetBaseOutputPathNoReserve(filePath, index);
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
            await WaitForBackgroundMetricTasksAsync(null, "SUMMARY", requeueUnfinished: false);

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
                $"跳过: {skipCount}, 耗时: {FormatTimeSpan(totalTime)}, " +
                $"压缩率: {overallRatio:P1}");
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
            if (failCount > 0)
            {
                foreach (var r in allResults.Where(r => !r.Skipped && !r.Success))
                    _logger.LogError($"[FAIL] {r.FileName}: {r.ErrorMessage}");
            }


            // 从缓存回填高级指标（方案二：缓存由 journal replay + 本次运行填充）
            int backfillCount = 0;
            int backfillMiss = 0;
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
                    backfillCount++;
                }
                else if (r.Success && !r.Skipped)
                {
                    backfillMiss++;
                    _logger.LogInfo($"[CSV-DIAG] 回填未命中: {r.FileName} cacheKey={r.AdvancedMetricsCacheKey?[..Math.Min(16, r.AdvancedMetricsCacheKey?.Length ?? 0)]}...");
                }
            }
            _logger.LogInfo($"[CSV-DIAG] 高级指标回填: {backfillCount} 成功, {backfillMiss} 未命中");

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

            // ★ 导出前诊断：打印每行的指标状态
            _logger.LogInfo($"[EXPORT] EncodeResults={allResults.Count}");
            foreach (var r in allResults)
            {
                string xpsnr = r.FinalWXPSNR.HasValue ? r.FinalWXPSNR.Value.ToString("F2") : "-";
                string ssimu2 = r.FinalSSIMULACRA2.HasValue ? r.FinalSSIMULACRA2.Value.ToString("F2") : "-";
                string butter3 = r.FinalButteraugli_3norm.HasValue ? r.FinalButteraugli_3norm.Value.ToString("F4") : "-";
                string gmsd = r.FinalGMSD.HasValue ? r.FinalGMSD.Value.ToString("F4") : "-";
                _logger.LogInfo($"[EXPORT] {r.FileName} XPSNR={xpsnr} SSIMU2={ssimu2} Butter3={butter3} GMSD={gmsd}");
            }

            FlushCsvBuffer();  // ★ 最终刷盘，确保缓冲区数据不丢失
            ExportCsv(allResults, _resumeMetricsForExport);
        }

        /// <summary>安全终止本 Pipeline 追踪的 ffmpeg 子进程（不影响系统其他实例）。</summary>
        public void KillTrackedProcesses()
        {
            foreach (var p in _spawnedProcesses)
            {
                try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
            }
        }

        /// <summary> 清理编码缓存及临时文件 </summary>
        private void FinalCleanup()
        {
            FlushCsvBuffer();  // 兜底：确保 CSV 缓冲区落盘
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
            _pngCache.Clear();  // PNG 转换缓存随 session 结束清空
            _srcAlphaCache.Clear();   // Alpha 探测缓存随 session 结束清空
            _srcPixFmtCache.Clear();  // 像素格式缓存随 session 结束清空

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
            // ★ 先查轻量 Alpha 缓存，避免重复 ffprobe
            string normalizedPath = EncodeHelpers.GetNormalizedPathForCache(filePath);
            if (_srcAlphaCache.TryGetValue(normalizedPath, out bool cachedAlpha))
                return cachedAlpha;

            // ★ 优先从统一 Probe 缓存获取
            var info = await GetProbeInfoAsync(filePath);
            if (info != null)
            {
                _srcAlphaCache[normalizedPath] = info.HasAlpha;
                return info.HasAlpha;
            }

            // 兜底：单独探测（不缓存结果，避免临时故障导致错误值永久缓存）
            string args = $"-v error -select_streams v:0 -show_entries stream=pix_fmt -of csv=p=0 \"{EncodeHelpers.EscapeArg(filePath)}\"";
            string raw = await RunProbeAsync(_ffprobePath, args);
            string fmt = raw.Trim().ToLower();
            return fmt switch
            {
                "rgba" or "bgra" or "argb" or "abgr" => true,
                "rgba64le" or "bgra64le" => true,
                _ => false
            };
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
                string normalizedPath = EncodeHelpers.GetNormalizedPathForCache(filePath);
                if (!_srcAlphaCache.ContainsKey(normalizedPath))
                    _srcAlphaCache[normalizedPath] = info.HasAlpha;

                // 像素格式标准化（复用原有逻辑）
                if (fmt == "gray" || fmt.StartsWith("gray"))
                {
                    bool isHighBit = fmt.Contains("16") || fmt.Contains("12") || fmt.Contains("10");
                    fmt = isHighBit ? "yuv420p10le" : "yuv420p";
                }
                else if (fmt == "pal8" || fmt.StartsWith("pal"))
                {
                    // GIF 调色板格式 → yuv444p（保留清晰边缘）
                    fmt = "yuv444p";
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
                    int targetBitDepth = Math.Clamp(perCompBits, 8, 12);

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
                $"-v error -select_streams v:0 -show_entries stream=pix_fmt -of csv=p=0 \"{EncodeHelpers.EscapeArg(filePath)}\"");
            string fmtFallback = raw.Trim().ToLower();

            // 简单标准化（略去复杂部分以保证程序不崩溃，但建议 probe 正常提供）
            if (fmtFallback == "gray" || fmtFallback.StartsWith("gray"))
                fmtFallback = fmtFallback.Contains("16") || fmtFallback.Contains("12") || fmtFallback.Contains("10") ? "yuv420p10le" : "yuv420p";
            else if (fmtFallback.Contains("yuvj"))
                fmtFallback = fmtFallback.Replace("yuvj", "yuv");
            else if (fmtFallback.Contains("rgb") || fmtFallback.Contains("bgr"))
                fmtFallback = fmtFallback.Contains("64") ? "yuva444p10le" : "yuva444p"; // 保守假设有 alpha

            if (string.IsNullOrEmpty(fmtFallback)) fmtFallback = "yuv420p";
            _srcPixFmtCache[EncodeHelpers.GetNormalizedPathForCache(filePath)] = fmtFallback;
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
                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
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
