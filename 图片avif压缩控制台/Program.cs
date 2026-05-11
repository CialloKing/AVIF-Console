using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;   // 如果使用 System.Text.Json
using System.Text.RegularExpressions;
using static AvifEncoder.PresetConfig;


namespace AvifEncoder
{
    public enum CliPreset { Fast, Balanced, Best, Extreme }




    class ProbeInfo
    {
        public string PixFmt { get; set; } = "yuv420p";
        public bool HasAlpha { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }




    /// <summary>缓存管理器接口</summary>
    public interface ICacheManager
    {
        bool TryGetEncode(string key, out (string file, TimeSpan encodeTime, string commandLine) cached);
        void SetEncode(string key, string cacheFile, TimeSpan encodeTime, string commandLine);
        bool TryGetMetrics(string key, out QualityMetrics? metrics);   // 改为 QualityMetrics?
        void SetMetrics(string key, QualityMetrics metrics);
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

        public bool TryGetSSIM(string key, out double ssim)
            => _ssimCache.TryGetValue(key, out ssim);

        public void SetSSIM(string key, double ssim)
            => _ssimCache[key] = ssim;
    }






    public class PresetConfig
    {
        public int BaseCRF { get; set; }
        public double TargetSSIM { get; set; }
        public int FinalCpuUsed { get; set; } = 0;
        public int SearchCpuUsed { get; set; } = 2;
        public bool UseCRFSearch { get; set; }
        public int MaxJobs { get; set; }
        public string? PixelFormat { get; set; }
        public string AomParams { get; set; } =
            "aq-mode=3:deltaq-mode=0:enable-chroma-deltaq=1:sharpness=0:" +
            "enable-qm=1:enable-restoration=1:enable-cdef=1:" +
            "enable-global-motion=1:enable-warped-motion=1:" +
            "enable-obmc=1:enable-ref-frame-mvs=1:enable-tx64=1:enable-dist-wtd-comp=1";
        public bool Lossless { get; set; } = false;
        public int BitDepth { get; set; } = 8;
        public bool AutoSource { get; set; } = true;
        public bool UserSetChroma { get; set; } = false;
        public bool UserSetBitDepth { get; set; } = false;
        public string OutputNameFormat { get; set; } = "covers-{index}.avif";

        // 自定义 CRF 搜索范围
        public int MinCRF { get; set; } = 1;
        public int MaxCRF { get; set; } = 38;

        // 超时配置（分钟）
        public int EncodeTimeoutMinutes { get; set; } = -1;
        public int SearchTimeoutMinutes { get; set; } = 60;
        public int SafeTimeoutMinutes { get; set; } = 180;
        public int SafeEncodeTimeoutMinutes { get; set; } = 10;
        public int SearchEncodeTimeoutMinutes { get; set; } = 10;
        public int SsimTimeoutMinutes { get; set; } = 5;

        // 自定义编码器
        public string Encoder { get; set; } = "libaom-av1";
        public string MetricMode { get; set; } = "vmaf";

        // 用户是否通过 -t 手动指定了 MaxJobs
        public bool UserSpecifiedMaxJobs { get; set; } = false;

        // 预缩放：长边最大像素数，0 或负数表示禁用
        public int MaxResolution { get; set; } = 2560;

        // 是否将缩放应用于最终输出
        public bool ApplyScalingToOutput { get; set; } = true;

        // 是否递归遍历输入目录的子文件夹
        public bool RecurseSubdirectories { get; set; } = false;

        /// <summary>
        /// 返回当前编码器实际有效的 AOM 参数字符串。
        /// 只有 libaom-av1 支持 aq-mode/deltaq-mode 等参数，其他编码器返回空字符串。
        /// </summary>
        public string GetEffectiveAomParams()
        {
            if (EncoderUtils.SupportsAomParams(Encoder))
                return AomParams;
            return "";
        }

        /// <summary>
        /// 根据当前 MetricMode 自动调整 TargetSSIM 的默认上限。
        /// 仅在用户未手动指定 -q 时调用。
        /// </summary>
        public void AdjustTargetForMetricMode()
        {
            if (Lossless) return;
            switch (MetricMode?.ToLower())
            {
                case "vmaf":
                    TargetSSIM = Math.Min(TargetSSIM, 0.98);
                    break;
                case "mix":
                    TargetSSIM = Math.Min(TargetSSIM, 0.95);
                    break;
            }
        }

        /// <summary>
        /// 根据当前的 MetricMode，将用户输入的原生质量值转换为内部 0‑1 目标。
        /// </summary>
        public void SetQualityTarget(double rawValue, string metricMode)
        {
            TargetSSIM = metricMode?.ToLower() switch
            {
                "ssim" => Math.Clamp(rawValue, 0, 1),
                "psnr" => Math.Clamp((rawValue - 30) / 20.0, 0, 1),
                "msssim" => Math.Clamp(rawValue, 0, 1),
                "vmaf" => Math.Clamp(rawValue / 100.0, 0, 1),
                "mix" => Math.Clamp(rawValue, 0, 1),
                _ => Math.Clamp(rawValue, 0, 1)
            };
        }


        // ========== 文件系统抽象（解决跨平台/长路径/可测试性）==========
        public interface IFileSystem
        {
            bool FileExists(string path);
            long GetFileLength(string path);
            void DeleteFile(string path);
            void CopyFile(string source, string dest, bool overwrite);
            void CreateDirectory(string path);
            void DeleteDirectory(string path, bool recursive);
            string[] GetFiles(string path, string searchPattern);
            DateTime GetCreationTime(string path);
            void AppendAllText(string path, string contents);
            void WriteAllText(string path, string contents, Encoding encoding);
            Task<string> ReadAllTextAsync(string path);
            IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);
            bool DirectoryExists(string path);
        }

        public class RealFileSystem : IFileSystem
        {
            public bool FileExists(string path) => File.Exists(path);
            public long GetFileLength(string path) => new FileInfo(path).Length;
            public void DeleteFile(string path) => File.Delete(path);
            public void CopyFile(string source, string dest, bool overwrite) => File.Copy(source, dest, overwrite);
            public void CreateDirectory(string path) => Directory.CreateDirectory(path);
            public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
            public string[] GetFiles(string path, string searchPattern) => Directory.GetFiles(path, searchPattern);
            public DateTime GetCreationTime(string path) => File.GetCreationTime(path);
            public void AppendAllText(string path, string contents) => File.AppendAllText(path, contents);
            public void WriteAllText(string path, string contents, Encoding encoding) => File.WriteAllText(path, contents, encoding);
            public async Task<string> ReadAllTextAsync(string path) => await File.ReadAllTextAsync(path);
            public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
                => Directory.EnumerateFiles(path, searchPattern, searchOption);
            public bool DirectoryExists(string path) => Directory.Exists(path);
        }
    }





    public class EncodeResult
    {
        public int Index { get; set; }
        public string FileName { get; set; } = "";
        public string OriginalFileName { get; set; } = "";
        public long OriginalSize { get; set; }
        public long OutputSize { get; set; }
        public int UsedCRF { get; set; }
        public double FinalSSIM { get; set; }
        public double CompressionRatio => OriginalSize == 0 ? 0 : Math.Round(1.0 - (double)OutputSize / OriginalSize, 4);
        public TimeSpan EncodeTime { get; set; }
        public TimeSpan SearchTime { get; set; }
        public TimeSpan TotalTime { get; set; }
        public int Retries { get; set; }
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; } = "";
        public bool Skipped { get; set; } = false;
        public string? PixelFormat { get; set; }

        public string? SourcePixelFormat { get; set; }
        public string? Mode { get; set; }
        public bool IsSafeMode { get; set; }
        public string? AomParamsUsed { get; set; }      // 保留原有字段（可选）
        public bool CacheReused { get; set; }

        // ★ 新增：最终成功的 ffmpeg 命令字符串（便于完整审计）
        public string? CommandLine { get; set; }




        // ★ 新增多指标字段
        public double? FinalVMAF { get; set; }
        public double? FinalPSNR_Y { get; set; }
        public double? FinalMSSSIM { get; set; }
        public double? FinalMixScore { get; set; }


        public int SearchEvaluations { get; set; }   // ★ 新增：搜索阶段实际成功评估CRF点的次数

        public string InputPath { get; set; } = "";   // 原始输入文件完整路径，用于重试

    }

    /// <summary> 一次 libvmaf 计算得到的全部常用指标 </summary>
    public sealed class QualityMetrics
    {
        public double SSIM { get; set; }
        public double PSNR_Y { get; set; }
        public double MS_SSIM { get; set; }
        public double VMAF { get; set; }
    }


    public static class Logger
    {
        private static FileLogger? _instance;

        public static void Init(string outputDir) => _instance = new FileLogger(outputDir);
        public static void SetInstance(FileLogger logger) => _instance = logger;

        public static void Log(string msg) => _instance?.LogInfo(msg);
        public static void SSIM(string input, int crf, double ssim)
            => _instance?.LogMetric("ssim", $"{input} | CRF={crf} | SSIM={ssim}");
        public static void CRF(string msg) => _instance?.LogMetric("crf", msg);
        public static void Error(string msg) => _instance?.LogError(msg);
        public static void Search(string msg) => _instance?.LogSearch(msg);
    }

    /// <summary>日志接口，解耦具体日志实现</summary>
    public interface ILogger
    {
        void LogInfo(string msg);
        void LogError(string msg);
        void LogMetric(string metricName, string msg);
        void LogSearch(string msg);   // 新增：搜索阶段专用日志
    }

    /// <summary>基于文件的日志实现，兼容原 Logger 行为</summary>
    public class FileLogger : ILogger
    {
        private readonly object _lock = new();
        private readonly string _logDir;
        private readonly PresetConfig.IFileSystem _fs;   // 改为完整限定名


        public FileLogger(string outputDir, PresetConfig.IFileSystem? fileSystem = null)  // 改为完整限定名
        {
            _fs = fileSystem ?? new PresetConfig.RealFileSystem();
            _logDir = Path.Combine(outputDir, "log");
            _fs.CreateDirectory(_logDir);

            // 清理30天前的 run 日志（原有逻辑不变）
            try
            {
                var cutoff = DateTime.Now.AddDays(-30);
                foreach (var f in _fs.GetFiles(_logDir, "run_*.log"))
                {
                    if (_fs.GetCreationTime(f) < cutoff)
                        _fs.DeleteFile(f);
                }
            }
            catch { }

            LogInfo("===== NEW SESSION START =====");
            LogInfo($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        public void LogInfo(string msg)
        {
            lock (_lock)
                _fs.AppendAllText(
                    Path.Combine(_logDir, $"run_{DateTime.Now:yyyy-MM-dd}.log"),
                    $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        }

        public void LogError(string msg)
        {
            lock (_lock)
            {
                // 错误日志同时写入 run 日志和 error.log
                _fs.AppendAllText(
                    Path.Combine(_logDir, $"run_{DateTime.Now:yyyy-MM-dd}.log"),
                    $"[{DateTime.Now:HH:mm:ss}] [ERROR] {msg}\n");
                _fs.AppendAllText(
                    Path.Combine(_logDir, "error.log"),
                    $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            }
        }

        public void LogMetric(string metricName, string msg)
        {
            string fileName = metricName.ToLower() switch
            {
                "ssim" => "ssim_trace.log",
                "crf" => "crf_search.log",
                _ => $"metric_{metricName}.log"
            };

            lock (_lock)
                _fs.AppendAllText(
                    Path.Combine(_logDir, fileName),
                    $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        }

        // 搜索专用日志：写入 crf_search.log
        public void LogSearch(string msg)
        {
            LogMetric("crf", msg);
        }
    }



    public class AvifPipeline : IDisposable
    {
        private readonly string _inputDir;
        private readonly string _outputDir;
        private readonly PresetConfig _config;
        private readonly int _maxRetries = 2;
        private readonly string _ffmpegPath;
        private readonly string _ffprobePath;


        private const double SSIMMargin = 0.001;

        private readonly ProgressTracker _progress = new ProgressTracker();

        
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


        private const int HardFailThreshold = 2;
        private const int AvoidRadius = 2;


        // 记录某文件的某像素格式是否已发生“完全无法写入”的致命错误，用于跳过后续尝试
        private readonly ConcurrentDictionary<string, HashSet<string>> _fatalFmts = new();

        /// <summary> 每次开始新的搜索时重置失败跟踪状态 </summary>



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
        private string GetOutputPath(string inputFilePath, int index)
        {
            // 获取相对于输入根目录的相对路径
            string relPath = Path.GetRelativePath(_inputDir, inputFilePath);
            string? relDir = Path.GetDirectoryName(relPath);
            string fileName = GetOutputFileName(inputFilePath, index);

            string targetDir = string.IsNullOrEmpty(relDir)
                ? _outputDir
                : Path.Combine(_outputDir, relDir);

            _fs.CreateDirectory(targetDir);
            return Path.Combine(targetDir, fileName);
        }


        private async Task<double> EvaluateCrfSafe(int crf, Func<int, Task<double>> getScore, string name, FileScopedFailTracker tracker)
        {
            if (tracker.IsBlacklisted(crf))
                return double.PositiveInfinity;

            double score = await getScore(crf);
            if (score < 0)
            {
                tracker.RecordFailedAttempt(crf);
                return double.PositiveInfinity;
            }

            tracker.ClearCrf(crf);
            return score;
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
                    ICacheManager? cacheManager = null)
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
            int ssimSlots = Math.Max(2, cpuCount);
            int ffmpegPoolSize = isHardwareEncoder
                ? Math.Max(2, cpuCount * 2)
                : Math.Max(2, cpuCount / 2);

            if (isHardwareEncoder && !config.UserSpecifiedMaxJobs)
            {
                config.MaxJobs = Math.Max(config.MaxJobs, ffmpegPoolSize);
            }

            _maxFfmpegConcurrency = Math.Min(config.MaxJobs, ffmpegPoolSize);
            _ssimConcurrency = new SemaphoreSlim(ssimSlots);
            _ffmpegSlots = new SemaphoreSlim(ffmpegPoolSize);
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
            double vmafNorm = m.VMAF / 100.0;
            double psnrNorm = Math.Clamp((m.PSNR_Y - 30) / 20.0, 0, 1);
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
            _globalCts?.Cancel();
            _globalCts?.Dispose();
            _globalCts = null;            // ← 新增：防止已释放对象被引用
            _ssimConcurrency?.Dispose();
            _ffmpegSlots?.Dispose();
        }





        private readonly ConcurrentDictionary<string, ProbeInfo> _probeCache = new();

        private async Task<ProbeInfo?> GetProbeInfoAsync(string filePath)
        {
            string key = GetNormalizedPathForCache(filePath);
            if (_probeCache.TryGetValue(key, out var cached)) return cached;

            // 一次性 ffprobe 获取所有信息
            string args = $"-v error -select_streams v:0 -show_entries stream=pix_fmt,width,height,is_lossless -of json \"{filePath}\"";
            string json = await RunProbeAsync(_ffprobePath, args);
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var streams = doc.RootElement.GetProperty("streams");
                if (streams.GetArrayLength() == 0) return null;   // ★ 无视频流，返回 null

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

                var info = new ProbeInfo { PixFmt = fmt, HasAlpha = hasAlpha, Width = w, Height = h };
                _probeCache[key] = info;
                return info;
            }
            catch { return null; }
        }

        /// <summary>
        /// 根据编码器名称返回专用的命令行参数片段（速度控制、分块等），
        /// 替代原先固定的 -cpu-used / -row-mt。
        /// </summary>
        private string BuildEncoderSpecificArgs(PresetConfig cfg, int cpuUsed, string tilePart, string rowMt)
        {
            string enc = cfg.Encoder;

            if (EncoderUtils.IsLibAom(enc))
            {
                return $"-cpu-used {cpuUsed} {tilePart} {rowMt}";
            }

            if (EncoderUtils.IsSvtAv1(enc))
            {
                int svtPreset = SvtPresetFromCpuUsed(cpuUsed);
                string baseArgs = $"-preset {svtPreset} -tune 0 {tilePart}";
                if (!cfg.Lossless)
                    baseArgs += " -svtav1-params scd=0:aq-mode=2:enable-tpl-la=1:enable-mfmv=1:fast-decode=0";
                return baseArgs;
            }

            if (EncoderUtils.IsRav1e(enc))
            {
                return $"-speed {cpuUsed} {tilePart}";
            }

            return "-preset p4";
        }

        /// <summary> 将你的 cpu-used (0-8) 折合为 SVT 的 preset (0-13) </summary>
        private static int SvtPresetFromCpuUsed(int cpuUsed)
        {
            // cpuUsed 0 → SVT preset 0 (最慢)
            // cpuUsed 8 → SVT preset 13 (最快)
            return Math.Clamp(cpuUsed * 13 / 8, 0, 13);
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
        /// 使用 libvmaf 一次性计算 ref (原图) 与 dist (编码后) 的 SSIM / PSNR‑Y / MS‑SSIM / VMAF。
        /// 返回 QualityMetrics，失败返回 null。会自动处理分辨率不一致的情况（缩放至相同尺寸）。
        /// </summary>
        private async Task<QualityMetrics?> ComputeAllMetricsAsync(string refPath, string distPath)
        {
            if (!EnsureFilesValid(refPath, distPath)) return null;

            // ========== 去除 Windows 长路径前缀并统一为正斜杠 ==========
            string cleanOutputDir = _outputDir;
            if (OperatingSystem.IsWindows() && cleanOutputDir.StartsWith(@"\\?\"))
                cleanOutputDir = cleanOutputDir.Substring(4);

            cleanOutputDir = cleanOutputDir.Replace('\\', '/');

            string jsonName = $"_metrics_{Guid.NewGuid():N}.json";
            // ★ 关键修复：转义路径中的冒号，防止 ffmpeg 将其解释为参数分隔符
            // 正确写法：两个反斜杠（在 C# 中写为四个反斜杠）
            string jsonPathSafe = (cleanOutputDir + "/" + jsonName).Replace(":", "\\\\:");

            try
            {
                var (w1, h1) = await GetResolutionAsync(refPath).WaitAsync(TimeSpan.FromSeconds(30));
                var (w2, h2) = await GetResolutionAsync(distPath).WaitAsync(TimeSpan.FromSeconds(30));

                string filter;
                if (w1 > 0 && h1 > 0 && w2 > 0 && h2 > 0 && (w1 != w2 || h1 != h2))
                {
                    int w = Math.Min(w1, w2);
                    int h = Math.Min(h1, h2);
                    filter = $"[0:v]scale={w}:{h}[ref];[1:v]scale={w}:{h}[dist];[ref][dist]libvmaf=feature=name=psnr|name=float_ssim|name=float_ms_ssim:log_path={jsonPathSafe}:log_fmt=json:n_threads=4";
                }
                else
                {
                    filter = $"[0:v][1:v]libvmaf=feature=name=psnr|name=float_ssim|name=float_ms_ssim:log_path={jsonPathSafe}:log_fmt=json:n_threads=4";
                }

                string args = $"-loglevel error -hide_banner -i \"{refPath}\" -i \"{distPath}\" " +
                              $"-filter_complex \"{filter}\" -frames:v 1 -f null -";

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

                // 读取 JSON 指标文件时，使用未转义的实际路径
                string physicalPath = (cleanOutputDir + "/" + jsonName).Replace('/', Path.DirectorySeparatorChar);
                if (!_fs.FileExists(physicalPath))
                {
                    _logger.LogInfo($"ComputeAllMetrics: JSON 文件未生成: {physicalPath}");
                    return null;
                }

                string json = await _fs.ReadAllTextAsync(physicalPath);
                QualityMetrics? metrics = ParseVmafJson(json);
                if (metrics == null) return null;

                // 从 stderr 提取 VMAF 分数
                var vmafMatch = Regex.Match(stderr, @"VMAF score:\s*([0-9.]+)");
                if (vmafMatch.Success && double.TryParse(vmafMatch.Groups[1].Value,
                        NumberStyles.Float, CultureInfo.InvariantCulture, out double vmafScore))
                {
                    metrics.VMAF = vmafScore;
                }
                else
                {
                    vmafMatch = Regex.Match(stderr, @"vmaf\s*=\s*([0-9.]+)");
                    if (vmafMatch.Success && double.TryParse(vmafMatch.Groups[1].Value,
                            NumberStyles.Float, CultureInfo.InvariantCulture, out vmafScore))
                    {
                        metrics.VMAF = vmafScore;
                    }
                    else
                    {
                        _logger.LogInfo($"未从 stderr 提取到 VMAF 分数 [{Path.GetFileName(refPath)}]");
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
                // 清理临时 JSON 文件（使用未转义的实际路径）
                try
                {
                    string physicalPath = (cleanOutputDir + "/" + jsonName).Replace('/', Path.DirectorySeparatorChar);
                    if (_fs.FileExists(physicalPath)) _fs.DeleteFile(physicalPath);
                }
                catch { }
            }
        }

        private QualityMetrics? ParseVmafJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var pooled = doc.RootElement.GetProperty("pooled_metrics");

                double ssim = pooled.TryGetProperty("float_ssim", out var e) ? e.GetProperty("mean").GetDouble() : 0;
                double ms_ssim = pooled.TryGetProperty("float_ms_ssim", out e) ? e.GetProperty("mean").GetDouble() : 0;
                double vmaf = pooled.TryGetProperty("vmaf", out e) ? e.GetProperty("mean").GetDouble() : -1;
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

        private async Task<List<string>> GetAvailableEncodersAsync()
        {
            var encoders = new List<string>();
            try
            {
                using var p = new Process
                {
                    StartInfo = new ProcessStartInfo(_ffmpegPath, "-encoders")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                p.Start();
                // 同时读取 stdout 和 stderr，避免管道缓冲区填满导致死锁
                var outTask = p.StandardOutput.ReadToEndAsync();
                var errTask = p.StandardError.ReadToEndAsync();
                await Task.WhenAll(outTask, errTask, p.WaitForExitAsync());

                using var reader = new StringReader(await outTask);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.TrimStart();
                    if (trimmed.Length > 0 && trimmed[0] == 'V' && trimmed.Contains("av1"))
                    {
                        string[] parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            string name = parts[1];
                            if (!encoders.Contains(name))
                                encoders.Add(name);
                        }
                    }
                }
            }
            catch
            {
                // 忽略
            }
            return encoders;
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
            int jobs = Math.Max(2, (int)Math.Sqrt(Environment.ProcessorCount));
            return preset switch
            {
                CliPreset.Fast => new PresetConfig
                {
                    BaseCRF = 38,
                    TargetSSIM = 0.91,
                    FinalCpuUsed = 2,
                    SearchCpuUsed = 4,
                    UseCRFSearch = false,
                    PixelFormat = "yuv420p10le",   // 保持原始格式，ApplyBitDepth 会根据 BitDepth 修正
                    MaxJobs = jobs,
                    BitDepth = 8
                },
                CliPreset.Balanced => new PresetConfig
                {
                    BaseCRF = 36,
                    TargetSSIM = 0.97,
                    FinalCpuUsed = 2,
                    SearchCpuUsed = 2,
                    UseCRFSearch = false,
                    PixelFormat = "yuv420p10le",
                    MaxJobs = jobs,
                    BitDepth = 8
                },
                CliPreset.Best => new PresetConfig
                {
                    BaseCRF = 34,
                    TargetSSIM = 0.97,
                    FinalCpuUsed = 0,
                    SearchCpuUsed = 2,
                    UseCRFSearch = true,
                    PixelFormat = "yuv444p10le",
                    MaxJobs = jobs,
                    BitDepth = 8
                },
                CliPreset.Extreme => new PresetConfig
                {
                    BaseCRF = 35,
                    TargetSSIM = 0.99,
                    FinalCpuUsed = 0,
                    SearchCpuUsed = 0,
                    UseCRFSearch = true,
                    PixelFormat = "yuv444p10le",
                    MaxJobs = jobs,
                    BitDepth = 10,
                    AomParams = "aq-mode=3:deltaq-mode=0:enable-chroma-deltaq=1:sharpness=0:" +
                "enable-qm=1:enable-restoration=1:enable-cdef=1:" +
                "enable-global-motion=1:enable-warped-motion=1:" +
                "enable-obmc=1:enable-ref-frame-mvs=1:enable-tx64=1:enable-dist-wtd-comp=1"
                },
                _ => throw new ArgumentOutOfRangeException(nameof(preset))
            };
        }

        private static string Sha256(string text)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            return Convert.ToHexString(hash)[..16];
        }

        private async Task<bool> IsTrulyLosslessSource(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower();
            if (ext == ".png") return true;          // PNG 默认视为真无损源
            if (ext == ".webp")
            {
                // 修复：通过 is_lossless 字段准确判断 WebP 是否无损
                string args = $"-v error -select_streams v:0 -show_entries stream=is_lossless -of csv=p=0 \"{filePath}\"";
                string output = await RunProbeAsync(_ffprobePath, args);
                return output.Trim() == "1";         // ffprobe 返回 "1" 表示无损
            }
            return false;
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
            _globalCts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                SafeWriteLine("\n[WARN] 正在安全停止，请稍候...");
                _globalCts.Cancel();
            };

            Console.OutputEncoding = Encoding.UTF8;
            _progress.Start(DateTime.Now);
            // （Init 已由构造函数中的 new FileLogger(_outputDir) 完成，直接删除）
            //Logger.Init(_outputDir);

            _logger.LogInfo($"Pipeline started: CRF={_config.BaseCRF} SSIM={_config.TargetSSIM}");

            await PrintStartupInfoAsync();

            var files = await ScanAndPrepareFilesAsync();
            if (files == null || files.Count == 0) return;

            var results = await ProcessInitialBatchAsync(files);
            results = await RetryFailuresAsync(results);

            PrintSummaryAndExport(results);
            FinalCleanup();
        }

        // ==================== 辅助方法 ====================

        /// <summary> 打印启动信息，包括编码器检测 </summary>
        private async Task PrintStartupInfoAsync()
        {
            SafeWriteLine("===== AVIF 全自动编码流水线 =====");
            SafeWriteLine($"输入文件夹: {_inputDir}  输出文件夹: {_outputDir}");

            string crfInfo;
            if (_config.UseCRFSearch)
                crfInfo = $"基础CRF: {_config.BaseCRF}, 搜索范围: {_config.MinCRF}-{_config.MaxCRF}";
            else
                crfInfo = $"CRF: {_config.BaseCRF}";

            // 根据 MetricMode 动态生成标签和原生数值
            string metricMode = (_config.MetricMode ?? "vmaf").ToUpper();
            string targetDisplay = GetTargetDisplayString(_config.TargetSSIM, metricMode);

            SafeWriteLine($"编码器: {_config.Encoder}");
            SafeWriteLine($"同时调用ffmpeg编码数: {_maxFfmpegConcurrency}");
            SafeWriteLine($"{crfInfo}  {metricMode}目标: {targetDisplay}  搜索: {_config.UseCRFSearch}  像素格式: {(_config.AutoSource ? "自适应" : (_config.PixelFormat ?? "动态"))}");
            SafeWriteLine($"文件名模板: {_config.OutputNameFormat}");
        }

        // 辅助方法：将内部 0~1 目标值转换为对应模式的原生显示字符串
        private static string GetTargetDisplayString(double targetSSIM, string metricMode)
        {
            switch (metricMode.ToLower())
            {
                case "vmaf":
                    double vmafTarget = targetSSIM * 100;
                    // 若小于 0.005 视为整数（消除浮点误差）
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

            var extensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };

            // 根据配置选择是否递归遍历子文件夹
            var searchOption = _config.RecurseSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var sortedFiles = _fs.EnumerateFiles(_inputDir, "*.*", searchOption)
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
        private void PrintSummaryAndExport(List<EncodeResult?> results)
        {
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

            ExportCsv(allResults);
        }

        /// <summary> 清理编码缓存及临时文件 </summary>
        private void FinalCleanup()
        {
            CleanDirectory(Path.Combine(_outputDir, "_enc_cache"));
            string scaledDir = Path.Combine(_outputDir, "_scaled");
            if (_fs.DirectoryExists(scaledDir))
            {
                try { _fs.DeleteDirectory(scaledDir, true); } catch { }
            }
            foreach (var f in _fs.GetFiles(_outputDir, "_p_*.avif"))   // 替换 Directory.GetFiles
                try { _fs.DeleteFile(f); } catch { }
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
            if (OperatingSystem.IsWindows() && !path.StartsWith(@"\\?\"))
            {
                // 如果是根路径，直接添加前缀；否则 Path.GetFullPath 会规范化
                string full = Path.GetFullPath(path);
                if (!full.StartsWith(@"\\?\"))
                    full = @"\\?\" + full;
                return full;
            }
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
        /// 获取源文件的标准化像素格式，高位深 RGB 会保留对应位深（10‑bit），灰度映射为 yuv420p
        /// </summary>
        /// <summary>
        /// 获取源文件的标准化像素格式，高位深 RGB 会保留对应位深（10‑bit），灰度映射为 yuv420p
        /// </summary>
        /// <summary>
        /// 获取源文件的标准化像素格式，优先使用统一 Probe 缓存，消除重复 ffprobe。
        /// 高位深 RGB 会保留对应位深（10‑bit），灰度映射为 yuv420p。
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
                else if (fmt.StartsWith("rgb") || fmt.StartsWith("bgr") || fmt.StartsWith("gbr"))
                {
                    bool is4Comp = fmt.Contains('a') || fmt.Contains('0') || fmt.Contains('x') ||
                                   fmt == "argb" || fmt == "abgr";
                    if (fmt.Contains("64") && !is4Comp) is4Comp = true;

                    int components = is4Comp ? 4 : 3;
                    var match = Regex.Match(fmt, @"(\d+)");
                    int totalBits = 0;
                    if (match.Success) int.TryParse(match.Groups[1].Value, out totalBits);
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


        private async Task<string> GetPixelFormatForFileAsync(string filePath, bool isLosslessMode, bool isTrulyLossless, bool hasAlpha)
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
                var searchResult = await RunCRFSearchAsync(workingInputPath, config, encInfo);
                string finalEncodeInput = (scaling.WasScaled && !config.ApplyScalingToOutput) ? inputPath : workingInputPath;
                var encodeResult = await PerformFinalEncodeAsync(finalEncodeInput, outputPath, config, encInfo, searchResult);

                // 计算最终质量
                (double ssim, QualityMetrics? metrics) = await EvaluateFinalQualityAsync(
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


        private async Task<(double ssim, QualityMetrics? metrics)> EvaluateFinalQualityAsync(
    string workingInputPath, string outputPath, FinalEncodeResult encodeResult,
    EncodingInfo encInfo, CRFSearchResult searchResult, PresetConfig config)
        {
            if (!encodeResult.Success)
                return (0, null);

            string normalizedInput = GetNormalizedPathForCache(workingInputPath);
            string cleanPixFmt = encodeResult.ActualPixFmt?.Replace("a", "") ?? "";
            int actualDepth = encodeResult.ActualPixFmt?.Contains("10le") == true ? 10 : 8;
            string aomParams = config.GetEffectiveAomParams();
            bool jpeg = IsJpeg(workingInputPath);
            int tileCols = encInfo.TileCols;
            int cpuUsed = searchResult.UseSafeModeFinalEncode ? 0 : config.FinalCpuUsed;
            var (keyW, keyH) = await GetResolutionAsync(workingInputPath);
            string cacheKey = GetSsimCacheKey(normalizedInput, encodeResult.Crf, cleanPixFmt, tileCols, cpuUsed, jpeg, aomParams, actualDepth, keyW, keyH);

            // 缓存命中
            if (_cache.TryGetMetrics(cacheKey, out QualityMetrics? cachedMetrics))
            {
                _logger.LogSearch($"最终指标复用缓存: CRF={encodeResult.Crf} VMAF={cachedMetrics!.VMAF:F2}");
                return (cachedMetrics!.SSIM, cachedMetrics);
            }

            // 计算多指标
            QualityMetrics? metrics = null;
            try
            {
                metrics = await ComputeAllMetricsAsync(workingInputPath, outputPath);
            }
            catch (Exception ex) { _logger.LogError($"多指标计算异常: {ex.Message}"); }

            if (metrics != null)
            {
                _cache.SetMetrics(cacheKey, metrics);
                _logger.LogSearch($"最终多指标 CRF={encodeResult.Crf}: SSIM={metrics.SSIM:F4}, PSNR-Y={metrics.PSNR_Y:F2}dB, MS-SSIM={metrics.MS_SSIM:F4}, VMAF={metrics.VMAF:F2}");
                return (metrics.SSIM, metrics);
            }

            // 回退 SSIM 单一缓存
            if (_cache.TryGetSSIM(cacheKey, out double cachedSsim) && cachedSsim >= 0)
                return (cachedSsim, null);

            double ssim = await CalcSSIMAsync(workingInputPath, outputPath, encodeResult.ActualPixFmt);
            if (ssim >= 0) _cache.SetSSIM(cacheKey, ssim);
            return (ssim, null);
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
    EncodingInfo encInfo, double ssim, QualityMetrics? metrics, DateTime fileStartTime)
        {
            var result = new EncodeResult
            {
                Index = index,
                FileName = outputFileName,
                OriginalFileName = name,
                InputPath = inputPath,                 // ★ 记录原始输入路径
                OriginalSize = encodeResult.Success ? _fs.GetFileLength(inputPath) : 0,
                OutputSize = encodeResult.Success ? _fs.GetFileLength(outputPath) : 0,
                UsedCRF = encodeResult.Success ? encodeResult.Crf : -1,
                FinalSSIM = ssim,
                EncodeTime = encodeResult.EncodeTime,
                SearchTime = searchResult.SearchTime,
                TotalTime = DateTime.Now - fileStartTime,
                Retries = encodeResult.Retries,
                Success = encodeResult.Success,
                ErrorMessage = encodeResult.FailReason,
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
                SearchEvaluations = searchResult.SearchEvalCount   // ★ 新增
            };
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

            string outputPath = GetOutputPath(inputPath, index);   // ★ 使用新方法保持子目录结构
            if (_fs.FileExists(outputPath))
            {
                string name = Path.GetFileName(inputPath);
                SafeWriteLine($"[SKIP] {name} (已存在，跳过)");
                _logger.LogInfo($"跳过: {name}");
                var skipResult = new EncodeResult
                {
                    Index = index,
                    FileName = Path.GetFileName(outputPath),
                    OriginalFileName = name,
                    InputPath = inputPath,                // ★ 赋值
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
            bool isTrulyLossless = isLosslessMode && await IsTrulyLosslessSource(inputPath);

            string srcFmt = await GetSourcePixelFormat(inputPath);
            bool hasAlpha = await SourceHasAlpha(inputPath);
            string actualPixFmt = await GetPixelFormatForFileAsync(inputPath, isLosslessMode, isTrulyLossless, hasAlpha);

            string pixInfo;
            if (config.AutoSource && !isLosslessMode)
                pixInfo = $"源: {srcFmt} -> 输出: {actualPixFmt}";
            else
                pixInfo = actualPixFmt;

            var (w, h) = await GetResolutionAsync(inputPath);
            if (w == 0 || h == 0) return null;

            if (isLosslessMode && !isTrulyLossless)
                SafeWriteLine($" [WARN] [{name}] 有损源，使用 -crf 0 数学无损...");

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
                // 基础性能推荐值
                tileCols = Math.Clamp((int)Math.Log2(Environment.ProcessorCount), 1, 4);

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
        private async Task<CRFSearchResult> RunCRFSearchAsync(string inputPath, PresetConfig config, EncodingInfo encInfo)
        {
            int crf = encInfo.BaseCrf;
            string actualPixFmt = encInfo.ActualPixFmt;
            var searchTime = TimeSpan.Zero;
            bool searchBasedCRF = false, useSafeModeFinalEncode = false;
            int totalEvaluations = 0;    // ★ 搜索评估次数
            string name = Path.GetFileName(inputPath);

            if (!encInfo.IsLosslessMode && config.UseCRFSearch)
            {
                string metricModeLabel = (config.MetricMode ?? "vmaf").ToUpper();
                string targetDisplay = GetTargetDisplayString(config.TargetSSIM, metricModeLabel);
                SafeWriteLine($"  [SEARCH] [{name}] 开始 CRF 搜索 (目标 {metricModeLabel}={targetDisplay})，请耐心等待...");

                try
                {
                    var swSearch = Stopwatch.StartNew();
                    bool searchOk;
                    int finalCrf;
                    string usedPixFmt;

                    (searchOk, finalCrf, usedPixFmt, totalEvaluations) = await TrySearchWithFormatAttempts(
                        inputPath, config, encInfo, actualPixFmt, name);

                    if (!searchOk)
                    {
                        SafeWriteLine($" [RETRY] [{name}] 普通搜索失败，开始安全模式全扫描 (yuv420p, cpu‑used 0)...");
                        (searchOk, finalCrf, usedPixFmt, useSafeModeFinalEncode) = await RunSafeModeScan(
                            inputPath, config, name, config.MinCRF, config.MaxCRF);
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
                        crf = config.BaseCRF;
                        SafeWriteLine($"  [WARN] [{name}] 所有搜索失败，使用 BaseCRF ({crf}) 直接编码");
                    }
                }
                catch (Exception ex)
                {
                    crf = config.BaseCRF;
                    _logger.LogInfo($"搜索异常，回退直接编码: {name} - {ex.Message}");
                    SafeWriteLine($" [WARN] [{name}] CRF搜索异常，使用 BaseCRF ({crf}) 直接编码");
                }
            }

            return new CRFSearchResult
            {
                Crf = crf,
                ActualPixFmt = actualPixFmt,
                SearchTime = searchTime,
                SearchBasedCRF = searchBasedCRF,
                UseSafeModeFinalEncode = useSafeModeFinalEncode,
                SearchEvalCount = totalEvaluations    // ★ 赋值
            };
        }

        // ---------- 尝试目标格式列表搜索 ----------
        // ---------- 尝试目标格式列表搜索 ----------
        private async Task<(bool ok, int crf, string pixFmt, int totalEvalCount)> TrySearchWithFormatAttempts(
    string inputPath, PresetConfig config, EncodingInfo encInfo,
    string actualPixFmt, string name)
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
                        SafeWriteLine($"  [RETRY] [{name}] 尝试 {desc} {fmt} ...");
                }

                (int crfResult, bool failed, bool qualityInsufficient, int evalCount) =
                    await HybridSearchCRFAsync(inputPath, encInfo.TileCols, config, fmt, IsJpeg(inputPath));
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
                        string cacheKey = GetSsimCacheKey(
                            normalizedInput, testCrf, "yuv420p", 0, 0,
                            IsJpeg(inputPath), effectiveAom, 8, w, h);
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
            string baseFmt = depthSuffix.Length > 0 ? actualPixFmt.Substring(0, actualPixFmt.Length - 4) : actualPixFmt;

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
        /// 仅对 libaom‑av1 启用 tile 与 row‑mt 参数，其他编码器忽略，避免无效参数造成失败。
        /// </summary>
        private string BuildSafeModeArgs(string inputPath, string outputPath, PresetConfig config,
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
                safeTileCols = Math.Clamp(Math.Max(2, minCols), minCols, maxCols);

            string safeTile = EncoderUtils.IsLibAom(config.Encoder)
                ? $"-tile-columns {safeTileCols} -tile-rows 0"
                : "";
            string safeRowMt = EncoderUtils.IsLibAom(config.Encoder) ? "-row-mt 1" : "";

            string encArgs = BuildEncoderSpecificArgs(config, 0, safeTile, safeRowMt);
            return $"-loglevel error -hide_banner -i \"{inputPath}\" " +
                   $"-c:v {config.Encoder} -pix_fmt yuv420p " +
                   $"-crf {crf} {encArgs} " +
                   $"-color_range pc {stillPic} -frames:v 1 {aomPart} -y \"{outputPath}\"";
        }


        private async Task<FinalEncodeResult> EncodeWithFallback(
    string inputPath, string outputPath, PresetConfig config,
    EncodingInfo encInfo, CRFSearchResult searchResult,
    int timeoutMinutes, string aomPart, string name, string effectiveAom)
        {
            var startTime = DateTime.Now;
            int crf = searchResult.Crf;
            string actualPixFmt = searchResult.ActualPixFmt;
            bool success = false;
            TimeSpan encodeTime = TimeSpan.Zero;
            int retries = 0;
            string failReason = "";
            bool fromCache = false;
            string? actualAom = null;
            string? finalCommand = null;
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
        }


        /// <summary>
        /// 生成用于编码缓存的一致键，确保所有缓存访问使用相同格式。
        /// </summary>
        private string GetEncodeCacheKey(
    string normalizedPath, int crf, string pixFmt,
    string tilePart, int actualCpu, bool isTrueLossless,
    string aomParams, bool jpeg, int bitDepth,
    int width = 0, int height = 0)       // ★ 新增
        {
            string res = (width > 0 && height > 0) ? $"|res={width}x{height}" : "";
            return $"{normalizedPath}|crf={crf}|pix={pixFmt}" +
                   $"|tile={tilePart}|cpu={actualCpu}|lossless={isTrueLossless}" +
                   $"|aom={aomParams}|jpeg={jpeg}|depth={bitDepth}{res}";
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
            var fatalSet = _fatalFmts.GetOrAdd(normalizedKey, _ => new HashSet<string>());

            foreach (var currentPixFmt in pixFmtsToTry)
            {
                // 若该格式之前已被标记为“无法生成任何输出”，直接跳过
                if (fatalSet.Contains(currentPixFmt))
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
                if (result.error?.StartsWith("FATAL_NOTHING:", StringComparison.OrdinalIgnoreCase) == true)
                {
                    // 只有所有参数集都 Nothing 才标记
                    fatalSet.Add(currentPixFmt);
                    _logger.LogInfo($"致命格式 {currentPixFmt} 已记录 [{fileName}]，将不再重试");
                }
                // 原有的降级日志保留

                // 仅当还有后续格式时才输出降级日志
                if (currentPixFmt != pixFmtsToTry.Last())
                {
                    string nextFmt = pixFmtsToTry[Array.IndexOf(pixFmtsToTry, currentPixFmt) + 1];
                    if (!fatalSet.Contains(nextFmt))
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
        private List<(string aomParams, string tilePart, int actualCpu, string rowMt)> BuildParamSets(
    PresetConfig cfg, string currentPixFmt, bool isTrueLossless, int tileCols, int cpuUsed,
    bool allowParamDegrade, int imageWidth)
        {
            string effectiveAom = cfg.GetEffectiveAomParams();
            var sets = new List<(string, string, int, string)>();

            bool isHighChroma = currentPixFmt.Contains("444") || currentPixFmt.Contains("422");
            string rowMt = EncoderUtils.IsLibAom(cfg.Encoder) ? "-row-mt 1" : "";

            // 双边约束
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

                    // ★ 安全 tile：至少 2 列（除非图像太小），且必须在 [minLegal, maxLegal] 内
                    int safeTileCols = (imageWidth > 0 && imageWidth >= 256)
                                       ? Math.Clamp(Math.Max(2, minLegal), minLegal, maxLegal)
                                       : 0;
                    if (minLegal > maxLegal) safeTileCols = 0;   // 彻底无法使用 tile

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

        private static string TilePart(int tileCols, bool isTrueLossless)
            => isTrueLossless ? "-tile-columns 0 -tile-rows 0" : $"-tile-columns {tileCols} -tile-rows 0";

        /// <summary> 尝试使用单个参数集编码，返回结果 </summary>
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
                                                IsJpeg(input), currentPixFmt.Contains("10le") ? 10 : 8, encW, encH);

            string cacheFile = Path.Combine(_outputDir, "_enc_cache", $"{Sha256(cacheKey)}.avif");

            // 缓存命中
            if (_cache.TryGetEncode(cacheKey, out var cached) && File.Exists(cached.file))
            {
                _fs.CreateDirectory(Path.GetDirectoryName(output)!);
                _fs.CopyFile(cached.file!, output, true);
                _logger.LogInfo($"复用编码缓存: {input} CRF={crf} pix={currentPixFmt} 原耗时={cached.encodeTime.TotalSeconds:F1}s");
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
            _logger.LogSearch($"  ⏳ [{fileName}] 等待编码资源 (CRF={crf})...");
            bool slotTaken = false;
            try
            {
                if (!await _ffmpegSlots.WaitAsync(TimeSpan.FromSeconds(300), _globalCts?.Token ?? default))
                {
                    _logger.LogSearch($"❌ 编码信号量获取超时: {input} CRF={crf}");
                    return (false, TimeSpan.Zero, 0, "编码信号量获取超时", false, null, null);
                }
                slotTaken = true;
                _logger.LogSearch($"  ▶ [{fileName}] 开始编码 (CRF={crf}, pix={currentPixFmt})");

                for (int attempt = 0; attempt <= _maxRetries; attempt++)
                {
                    string ffArgs = BuildFfmpegArgs(input, output, crf, currentPixFmt, param, cfg, isTrueLossless);
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
                        _logger.LogSearch($"✅ 编码成功: {input} CRF={crf} 耗时={sw.Elapsed.TotalSeconds:F1}s");
                        return (true, sw.Elapsed, attempt, "", false, param.aomParams, ffArgs);
                    }

                    string error = $"CRF={crf}, {stderrLastLine}";
                    _logger.LogSearch($"❌ 编码失败: {input} 尝试{attempt + 1}/{_maxRetries + 1} - {error}");

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







        /// <summary> 构建 ffmpeg 参数字符串 </summary>
        /// <summary> 构建 ffmpeg 参数字符串 </summary>
        private string BuildFfmpegArgs(string input, string output, int crf, string pixFmt,
                               (string aomParams, string tilePart, int actualCpu, string rowMt) param,
                               PresetConfig cfg, bool isTrueLossless)
        {
            // 临时提升日志级别以获取详细错误（例如 tile sizing 错误）
            string logLevel = "-loglevel info -hide_banner";

            string aom = string.IsNullOrEmpty(param.aomParams) ? "" : $"-aom-params {param.aomParams}";
            string crfPart = isTrueLossless ? "-lossless 1" : $"-crf {crf}";
            string range = "-color_range pc";
            string colorMeta = "-color_primaries bt709 -color_trc iec61966-2-1 -colorspace bt709";
            string stillPic = EncoderSupportsStillPicture(cfg.Encoder) ? "-still-picture 1" : "";

            string encoderSpecific = BuildEncoderSpecificArgs(cfg, param.actualCpu, param.tilePart, param.rowMt);

            return $"{logLevel} -i \"{input}\" " +
                   $"-c:v {cfg.Encoder} -pix_fmt {pixFmt} {range} {colorMeta} " +
                   $"{crfPart} {encoderSpecific} " +
                   $"{stillPic} -frames:v 1 {aom} -y \"{output}\"";
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



        // ── 核心 SSIM 计算（已整合色彩空间归一化 + 时间轴同步） ──
        private async Task<double> SSIMDirect(string a, string b, string? targetPixFmt = null)
        {
            if (!EnsureFilesValid(a, b)) return -1;

            string alignFmt = PrepareAlignFormat(targetPixFmt);

            try
            {
                var (w1, h1) = await GetResolutionAsync(a).WaitAsync(TimeSpan.FromSeconds(30));
                var (w2, h2) = await GetResolutionAsync(b).WaitAsync(TimeSpan.FromSeconds(30));

                // ★ 修复：任意一边分辨率无效则立即返回 -1
                if (w1 <= 0 || h1 <= 0 || w2 <= 0 || h2 <= 0)
                {
                    _logger.LogInfo($"SSIM 分辨率无效: a={Path.GetFileName(a)} ({w1}x{h1}), b={Path.GetFileName(b)} ({w2}x{h2})");
                    return -1;
                }

                string args = BuildSsimArgs(a, b, alignFmt, w1, h1, w2, h2);
                string output = await RunSsimProcess(args);
                return ParseSsimOutput(output);
            }
            catch (Exception ex)
            {
                _logger.LogInfo($"SSIM 异常: {Path.GetFileName(a)} vs {Path.GetFileName(b)} - {ex.Message}");
                SafeWriteLine($" [FAIL] SSIM 异常: {ex.Message}");
                return -1;
            }
        }

        private bool EnsureFilesValid(string a, string b)
        {
            if (!_fs.FileExists(a) || !_fs.FileExists(b))
            {
                _logger.LogInfo($"SSIM 文件缺失: a={Path.GetFileName(a)}, b={Path.GetFileName(b)}");
                return false;
            }

            long sizeA = _fs.GetFileLength(a);
            long sizeB = _fs.GetFileLength(b);
            if (sizeA < 100 || sizeB < 100)
            {
                _logger.LogInfo($"SSIM 文件太小 ({sizeA} / {sizeB} 字节)");
                return false;
            }
            return true;
        }


        private static string PrepareAlignFormat(string? targetPixFmt)
        {
            string alignFmt = targetPixFmt ?? "yuv420p";
            return alignFmt.Replace("a", ""); // 移除 Alpha 标记
        }


        private static string BuildSsimArgs(string a, string b, string alignFmt,
                                    int w1, int h1, int w2, int h2)
        {
            if (w1 > 0 && h1 > 0 && w2 > 0 && h2 > 0 && (w1 != w2 || h1 != h2))
            {
                int w = Math.Min(w1, w2);
                int h = Math.Min(h1, h2);
                string scaleFilter = $"[0:v]scale={w}:{h}[ref];[1:v]scale={w}:{h}[dist];[ref][dist]ssim";
                return $"-loglevel info -hide_banner -i \"{a}\" -i \"{b}\" " +
                       $"-filter_complex \"{scaleFilter}\" -frames:v 1 -f null -";
            }
            else
            {
                return $"-loglevel info -hide_banner " +
                       $"-i \"{a}\" -i \"{b}\" " +
                       $"-filter_complex \"[0:v]format={alignFmt}[ref];[1:v]format={alignFmt}[dist];[ref][dist]ssim\" " +
                       $"-frames:v 1 -f null -";
            }
        }




        private async Task<string> RunSsimProcess(string args)
        {
            var (exitCode, stdout, stderr) = await _processRunner.RunAsync(
                _ffmpegPath, args, TimeSpan.FromMinutes(_config.SsimTimeoutMinutes), _globalCts?.Token ?? default);

            // 与之前行为一致，将 stdout 和 stderr 合并返回
            return stdout + stderr;
        }

        private double ParseSsimOutput(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                LogSsimParseFailure("输出为空");
                return -1;
            }

            _logger.LogInfo($"SSIM output:\n{output}");

            // 匹配 "All:0.xxxx" （容错空格）
            var m = Regex.Match(output, @"All:\s*([0-9.]+)");
            if (m.Success &&
                double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double ssim))
            {
                return ssim;
            }

            // 备选：某些版本输出 "SSIM All:"
            m = Regex.Match(output, @"SSIM\s+All:\s*([0-9.]+)");
            if (m.Success &&
                double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out ssim))
            {
                return ssim;
            }

            LogSsimParseFailure(output.Length > 500 ? output[^500..] : output);
            return -1;
        }

        private void LogSsimParseFailure(string tail)
        {
            SafeWriteLine($" [WARN] SSIM 解析失败");
            SafeWriteLine($"  ffmpeg 尾部: {tail}");
            _logger.LogInfo($"SSIM 解析失败: tail:\n{tail}");
        }


        // CSV 列名常量，修改这里即可同步表头和数据行
        private static readonly string[] CsvColumnNames = new[]
{
    "文件名", "原始文件名", "原始大小", "输出大小", "压缩率",
    "CRF", "SSIM", "VMAF", "PSNR-Y", "MS-SSIM", "MixScore",
    "编码耗时(秒)", "搜索耗时(秒)", "总耗时(秒)", "重试次数",
    "像素格式", "源像素格式", "模式", "安全模式",
    "AOM参数", "完整命令行",   // ← 交换后的顺序
    "缓存复用", "状态", "失败原因",
    "搜索评估次数"
};
        /// <summary>
        /// 生成用于 SSIM 缓存的一致键，确保所有缓存访问使用相同格式。
        /// </summary>
        private static string GetSsimCacheKey(
    string normalizedPath, int crf, string pixFmt,
    int tileCols, int cpuUsed, bool isJpeg,
    string effectiveAomParams, int bitDepth,
    int width = 0, int height = 0)      // ★ 新增默认参数，保持兼容
        {
            string res = (width > 0 && height > 0) ? $"|res={width}x{height}" : "";
            return $"{normalizedPath}|crf={crf}|pix={pixFmt}|tile={tileCols}|cpu={cpuUsed}" +
                   $"|jpeg={isJpeg}|aom={effectiveAomParams}|depth={bitDepth}{res}";
        }






        /// <summary> 计算原始图像与编码后 AVIF 的 SSIM </summary>
        /// <param name="orig">原始图片路径</param>
        /// <param name="enc">编码后的 AVIF 路径</param>
        /// <param name="pixFmt">像素格式（可能含 Alpha），将被清洗后用于 SSIM 计算</param>
        private async Task<double> CalcSSIMAsync(string orig, string enc, string? pixFmt = null)
        {
            // 移除 Alpha 标记，确保 SSIM 只比较颜色通道
            string? cleanFmt = pixFmt?.Replace("a", "");
            return await SSIMDirect(orig, enc, cleanFmt);
        }






        /// <summary>
        /// 获取或计算给定编码参数下的多指标。
        /// 使用与 SSIM 缓存相同的键，以便未来统一。
        /// </summary>
        private async Task<QualityMetrics?> GetOrComputeMetrics(
    string input, int crf, int tileCols, int cpuUsed, PresetConfig cfg, bool jpeg, string pixFmt)
        {
            // 无损模式返回理想指标
            if (cfg.Lossless)
                return new QualityMetrics { SSIM = 1.0, PSNR_Y = 100.0, MS_SSIM = 1.0, VMAF = 100.0 };

            int actualDepth = pixFmt.Contains("10le") ? 10 : 8;
            string normalizedInput = GetNormalizedPathForCache(input);
            string effectiveAom = cfg.GetEffectiveAomParams();

            var (metricsW, metricsH) = await GetResolutionAsync(input);
            string key = GetSsimCacheKey(normalizedInput, crf, pixFmt, tileCols, cpuUsed, jpeg, effectiveAom, actualDepth, metricsW, metricsH);

            if (_cache.TryGetMetrics(key, out QualityMetrics? cached))
            {
                _logger.LogSearch($"指标缓存命中: CRF={crf} [{Path.GetFileName(input)}] VMAF={cached!.VMAF:F2}");
                return cached!;
            }

            var newTask = new TaskCompletionSource<QualityMetrics?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = _metricsTasks.GetOrAdd(key, newTask.Task);
            bool isOwner = task == newTask.Task;

            if (!isOwner)
            {
                try { return await task.WaitAsync(TimeSpan.FromMinutes(30)); }
                catch { return null; }
            }

            try
            {
                if (!await _ssimConcurrency.WaitAsync(TimeSpan.FromSeconds(300), _globalCts?.Token ?? default))
                {
                    _logger.LogSearch($"GetOrComputeMetrics 信号量等待超时: [{Path.GetFileName(input)}] CRF={crf}");
                    newTask.SetResult(null);
                    return null;
                }

                try
                {
                    string tmp = Path.Combine(_outputDir, $"_p_{Guid.NewGuid():N}.avif");
                    try
                    {
                        int searchCpu = Math.Min(cpuUsed + 2, 8);
                        var encResult = await EncodeToFileExAsync(input, tmp, crf, tileCols, searchCpu, cfg, jpeg, pixFmt,
                            isTrueLossless: false, cfg.SearchEncodeTimeoutMinutes, allowParamDegrade: true);

                        if (!encResult.ok || !_fs.FileExists(tmp) || _fs.GetFileLength(tmp) < 100)
                        {
                            _logger.LogSearch($"临时编码失败: CRF={crf} [{Path.GetFileName(input)}]");
                            newTask.SetResult(null);
                            return null;
                        }

                        QualityMetrics? metrics = await ComputeAllMetricsAsync(input, tmp);
                        if (metrics != null)
                        {
                            _cache.SetMetrics(key, metrics);
                            _logger.LogSearch($"新指标: CRF={crf} [{Path.GetFileName(input)}] " +
                                             $"SSIM={metrics.SSIM:F4}, PSNR-Y={metrics.PSNR_Y:F2}dB, " +
                                             $"MS-SSIM={metrics.MS_SSIM:F4}, VMAF={metrics.VMAF:F2}");
                        }
                        else
                        {
                            _logger.LogSearch($"指标计算失败: CRF={crf} [{Path.GetFileName(input)}]");
                        }

                        newTask.SetResult(metrics);
                        return metrics;
                    }
                    finally
                    {
                        if (_fs.FileExists(tmp)) try { _fs.DeleteFile(tmp); } catch { }
                    }
                }
                finally
                {
                    _ssimConcurrency.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"GetOrComputeMetrics 意外异常: [{Path.GetFileName(input)}] CRF={crf} - {ex.Message}");
                newTask.TrySetException(ex);
                return null;
            }
            finally
            {
                if (isOwner)
                    _metricsTasks.TryRemove(key, out _);
            }
        }













       


        private static string FormatQualityDisplay(QualityMetrics? m, double fallbackScore)
        {
            if (m != null)
                return $"VMAF={m.VMAF:F1}  PSNR-Y={m.PSNR_Y:F2}dB  SSIM={m.SSIM:F4}  MS-SSIM={m.MS_SSIM:F4}";
            return $"分数={fallbackScore:F4}";
        }


        /// <summary>
        /// 使用 Brent 方法（Dekker-Brent）在整数 CRF 域上搜索满足质量目标的最佳 CRF。
        /// 原理：动态混合逆二次插值、割线法和二分法，保证全局收敛且评估次数更少。
        /// </summary>
        /// <param name="getScore">评分委托，返回 0~1 分数（或 -1 表示失败）</param>
        /// <param name="low">已知 feasible 的最小 CRF（满足 target）</param>
        /// <param name="high">已知 infeasible 的最大 CRF（不满足 target）</param>
        /// <param name="lowScore">low 处的分数</param>
        /// <param name="highScore">high 处的分数</param>
        /// <summary>
        /// 使用 Brent 方法（Dekker-Brent）在整数 CRF 域上搜索满足质量目标的最佳 CRF。
        /// 解决原方法中区间顺序颠倒导致 Math.Clamp 异常的问题。
        /// </summary>
        /// <summary>
        /// 使用 Brent 方法（Dekker-Brent）在整数 CRF 域上搜索满足质量目标的最佳 CRF。
        /// 修复：避免因不可行侧函数值接近零而过早退出，现在以区间宽度为主要收敛判据，
        ///       保证能够探索到满足目标的最大可行 CRF。
        /// </summary>
        /// <summary>
        /// 使用 Brent 方法搜索满足质量目标的最大可行 CRF。
        /// 优化点：
        /// 1. 移除所有基于 |fbVal| < tol 或 bestScore 接近 target 的提前退出。
        /// 2. 仅当区间内无整数点时（xMax - xMin <= 1）或达到最大迭代次数时退出。
        /// 3. 保证不会漏测区间宽度为 2 时的唯一中间点。
        /// </summary>
        /// <summary>
        /// 增强版 Brent 搜索：集成硬失败黑名单、动态区间收缩与安全点搜索（第31步）
        /// </summary>
        /// <summary>
        /// 增强版 Brent 搜索：集成硬失败黑名单、动态区间收缩与安全点搜索（第31步）。
        /// 已修复初始化 bestCRF 时因变量交换导致的逻辑错误。
        /// </summary>
        /// <summary>
        /// 使用 Brent 方法搜索满足质量目标的最大可行 CRF。
        /// 优化点：
        /// 1. 移除所有基于评分接近目标的提前退出，仅以区间宽度作为收敛判据。
        /// 2. 当预测点落于端点且区间宽度 ≥2 时，强制跳至内部未测点，避免浪费迭代。
        /// 3. 保留失败跟踪与黑名单机制，维持鲁棒性。
        /// 4. 后处理探测右端点，确保不漏评。
        /// </summary>
        /// <summary>
        /// 使用 Brent 方法搜索满足质量目标的最大可行 CRF。
        /// 已完全修复遗漏中间整数点的问题。
        /// </summary>
        /// <summary>
        /// 混合搜索：Brent 粗边界定位 + 二分补洞 + 右侧单调校验，
        /// 保证在 CRF 离散域上返回满足目标的最大可行 CRF。
        /// </summary>
        /// <summary>
        /// 混合搜索：Brent 粗边界定位 + 二分补洞 + 右侧单调校验，
        /// 保证在 CRF 离散域上返回满足目标的最大可行 CRF。
        /// </summary>
        /// <summary>
        /// 自适应搜索：当区间宽度 ≤ 阈值时采用哨兵探测，否则采用指数扩展。
        /// 最终通过二分与右侧扫描保证返回全局最大可行 CRF。
        /// </summary>
        /// <summary>
        /// 自适应搜索：当区间宽度 ≤ 40 时采用哨兵探测，否则采用指数扩展。
        /// 最终通过二分与右侧扫描保证返回全局最大可行 CRF。
        /// 已修复 _scoreCache 写入缺失问题。
        /// </summary>
        /// <summary>
        /// 自适应搜索：当区间宽度 ≤ 40 时采用哨兵探测，否则采用指数扩展。
        /// 窄区间（≤20）直接二分，避免冗余哨兵；
        /// 哨兵点不足时自动改为四分位点。
        /// 最终通过二分与右侧扫描保证返回全局最大可行 CRF。
        /// </summary>
        private async Task<(int crf, double score, int evalCount)> SolveCrfBrent(
            Func<int, Task<double>> getScore,
            int low, int high, double lowScore, double highScore,
            double target, string name, CancellationToken token,
            PresetConfig cfg, string input, int tileCols, string pixFmt, bool jpeg)
        {
            var tracker = _failTrackers.GetOrAdd(GetNormalizedPathForCache(input), _ => new FileScopedFailTracker());
            tracker.Reset();

            var evaluated = new HashSet<int>();
            if (lowScore >= 0)
            {
                evaluated.Add(low);
                _scoreCache[low] = lowScore;
            }
            if (highScore >= 0)
            {
                evaluated.Add(high);
                _scoreCache[high] = highScore;
            }

            int totalEvals = 0;
            int bestCRF = -1;
            double bestScore = -1;
            if (lowScore >= target) { bestCRF = low; bestScore = lowScore; }
            if (highScore >= target && high > bestCRF) { bestCRF = high; bestScore = highScore; }

            if (highScore >= target)
                return (high, highScore, 0);

            if (lowScore < target)
                return (-1, lowScore, 0);

            const int widthThreshold = 40;
            const int narrowThreshold = 20;   // 窄区间直接二分，避免无效哨兵
            int searchLow, searchHigh;

            if (high - low <= widthThreshold)
            {
                // ========== 窄区间策略 ==========
                if (high - low <= narrowThreshold)
                {
                    // 区间极窄，直接使用二分，不再探测哨兵
                    searchLow = bestCRF > 0 ? bestCRF : low;
                    searchHigh = high;
                }
                else
                {
                    // 哨兵探测：先尝试固定经验哨兵
                    var rawSentinels = new[] { 12, 20, 28 };
                    var sentinels = rawSentinels.Where(c => c >= low && c <= high).ToList();

                    // 若可用哨兵太少，改用均匀四分位点
                    if (sentinels.Count < 3)
                    {
                        int q = (high - low) / 4;
                        sentinels = new List<int>
                {
                    low + q,
                    low + q * 2,
                    low + q * 3
                }.Where(c => c >= low && c <= high).Distinct().OrderBy(x => x).ToList();
                    }

                    // 哨兵评估
                    foreach (int crf in sentinels)
                    {
                        token.ThrowIfCancellationRequested();
                        if (evaluated.Contains(crf) || tracker.IsBlacklisted(crf))
                            continue;

                        double score = await EvaluateCrfSafe(crf, getScore, name, tracker);
                        if (double.IsInfinity(score)) continue;

                        evaluated.Add(crf);
                        _scoreCache[crf] = score;
                        totalEvals++;
                        LogEvaluation(name, cfg, crf, score, "SENTINEL");

                        if (score >= target && crf > bestCRF) { bestCRF = crf; bestScore = score; }
                    }

                    // 根据哨兵收缩 searchHigh
                    searchLow = bestCRF > 0 ? bestCRF : low;
                    searchHigh = high;
                    foreach (int crf in sentinels)
                    {
                        if (evaluated.Contains(crf) && getCachedScore(crf) < target)
                        {
                            searchHigh = crf;
                            break;
                        }
                    }
                }
            }
            else
            {
                // ========== 宽区间：指数扩展 + 二分 ==========
                searchLow = bestCRF > 0 ? bestCRF : low;
                searchHigh = high;
                int current = searchLow;
                int step = 1;

                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    int next = current + step;
                    if (next > high) next = high;
                    if (next == current) break;

                    if (evaluated.Contains(next) || tracker.IsBlacklisted(next))
                    {
                        if (evaluated.Contains(next) && getCachedScore(next) < target)
                            break;
                        step *= 2;
                        continue;
                    }

                    double score = await EvaluateCrfSafe(next, getScore, name, tracker);
                    if (double.IsInfinity(score))
                    {
                        searchHigh = Math.Min(high, next - 1);
                        break;
                    }

                    evaluated.Add(next);
                    _scoreCache[next] = score;
                    totalEvals++;
                    LogEvaluation(name, cfg, next, score, "EXP");

                    if (score >= target)
                    {
                        if (next > bestCRF) { bestCRF = next; bestScore = score; }
                        current = next;
                        step *= 2;
                    }
                    else
                    {
                        searchHigh = next;
                        break;
                    }
                }

                searchLow = bestCRF > 0 ? bestCRF : low;
            }

            // ========== 阶段 3：二分搜索 ==========
            int l = searchLow;
            int r = searchHigh;
            while (l < r)
            {
                token.ThrowIfCancellationRequested();
                int mid = (l + r + 1) / 2;
                if (evaluated.Contains(mid) || tracker.IsBlacklisted(mid))
                {
                    r = mid - 1;
                    continue;
                }

                double score = await EvaluateCrfSafe(mid, getScore, name, tracker);
                if (double.IsInfinity(score))
                {
                    r = mid - 1;
                    continue;
                }

                evaluated.Add(mid);
                _scoreCache[mid] = score;
                totalEvals++;
                LogEvaluation(name, cfg, mid, score, "BINARY");

                if (score >= target)
                {
                    if (mid > bestCRF) { bestCRF = mid; bestScore = score; }
                    l = mid;
                }
                else
                {
                    r = mid - 1;
                }
            }

            // ========== 阶段 4：右侧单调扫描 ==========
            if (bestCRF != -1 && bestScore >= target)
            {
                for (int next = bestCRF + 1; next <= high; next++)
                {
                    token.ThrowIfCancellationRequested();
                    if (evaluated.Contains(next) || tracker.IsBlacklisted(next))
                        break;
                    double score = await EvaluateCrfSafe(next, getScore, name, tracker);
                    if (double.IsInfinity(score) || score < target)
                        break;
                    evaluated.Add(next);
                    _scoreCache[next] = score;
                    totalEvals++;
                    bestCRF = next;
                    bestScore = score;
                    _logger.LogSearch($"[{name}] [SCAN] CRF={next} 达标，更新最优");
                }
            }

            if (bestCRF == -1)
            {
                if (lowScore >= target) { bestCRF = low; bestScore = lowScore; }
                else if (highScore >= target) { bestCRF = high; bestScore = highScore; }
            }

            return (bestCRF, bestScore, totalEvals);
        }

        // 辅助字段和方法（确保只保留一份）
        private Dictionary<int, double> _scoreCache = new();
        private double getCachedScore(int crf)
        {
            return _scoreCache.TryGetValue(crf, out double score) ? score : 0;
        }

        private void LogEvaluation(string name, PresetConfig cfg, int crf, double score, string phase)
        {
            string display = cfg.MetricMode?.ToLower() switch
            {
                "vmaf" => $"VMAF={score * 100:F1}",
                _ => $"分数={score:F4}"
            };
            SafeWriteLine($"  [{name}] [{phase}] CRF={crf} -> {display}");
            _logger.LogSearch($"[{name}] [{phase}] CRF={crf} -> {display}");
        }

























        /// <summary>
        /// 根据当前配置的度量模式从 QualityMetrics 中提取一个 0‑1 的分数。
        /// </summary>
        private double GetSearchScore(QualityMetrics m, string metricMode)
        {
            switch (metricMode?.ToLower())
            {
                case "ssim": return m.SSIM;
                case "psnr": return Math.Clamp((m.PSNR_Y - 30) / 20.0, 0, 1);
                case "msssim": return m.MS_SSIM;
                case "vmaf": return m.VMAF / 100.0;
                case "mix":
                    double vmafNorm = m.VMAF / 100.0;
                    double psnrNorm = Math.Clamp((m.PSNR_Y - 30) / 20.0, 0, 1);
                    return 0.80 * vmafNorm + 0.05 * m.SSIM + 0.10 * m.MS_SSIM + 0.05 * psnrNorm;
                default:
                    return m.SSIM;
            }
        }

        


        private async Task<(int crfNext, double score, bool converged, double newX0, double newX1, double newF0, double newF1)>
PerformSecantIteration(
    Func<int, Task<double>> getScore, double x0, double x1,
    double f0, double f1, double target, int low, int high,
    string name, int iter, CancellationToken token, PresetConfig cfg)
        {
            // 检查斜率有效性
            double slope = (f1 - f0) / (x1 - x0);
            if (Math.Abs(slope) < 1e-6)
                return (crfNext: (int)high, score: f1, converged: false,
                        newX0: x0, newX1: x1, newF0: f0, newF1: f1);

            // 预测下一个 CRF
            double xNext = x1 - (f1 - target) / slope;
            int crfNext = (int)Math.Round(xNext);
            crfNext = Math.Clamp(crfNext, low, high);

            // 避免原地踏步
            if (crfNext == (int)x0 || crfNext == (int)x1)
                crfNext = (int)(x0 + x1) / 2;

            // 评估新点
            double score = await getScore(crfNext);

            // 日志输出（根据度量模式自适应）
            string display = cfg.MetricMode?.ToLower() switch
            {
                "vmaf" => $"VMAF={score * 100:F1}",
                null or "" => $"分数={score:F4}",
                _ => $"分数={score:F4}"
            };
            SafeWriteLine($"  [{name}] [SECANT] iter={iter + 1}, CRF={crfNext} -> {display}");

            // 检查收敛
            const double tolerance = 0.005;
            bool converged = Math.Abs(score - target) < tolerance;

            // 更新区间（返回新值）
            double newX0 = x0, newX1 = x1, newF0 = f0, newF1 = f1;
            if (!converged)
            {
                if (score >= target)
                {
                    newX0 = crfNext;
                    newF0 = score;
                }
                else
                {
                    newX1 = crfNext;
                    newF1 = score;
                }
            }

            return (crfNext, score, converged, newX0, newX1, newF0, newF1);
        }








        /// <summary>
        /// 使用极快的编码参数进行代理评估，返回 0‑1 分数（与 getScore 一致）。
        /// 失败返回 -1。
        /// </summary>
        private async Task<double> ProxyEvaluateAsync(string input, int crf,
    int tileCols, PresetConfig cfg, bool jpeg, string pixFmt)
        {
            // Proxy 始终使用 yuv420p + cpu-used 6 + 最小稳定参数
            var proxyCfg = new PresetConfig
            {
                Encoder = cfg.Encoder,
                BaseCRF = crf,
                FinalCpuUsed = 6,
                SearchCpuUsed = 6,
                PixelFormat = "yuv420p",
                Lossless = false,
                AomParams = "aq-mode=0:enable-cdef=0",
                MaxJobs = cfg.MaxJobs,
                BitDepth = cfg.BitDepth
            };

            string tmpOutput = Path.Combine(_outputDir, $"_proxy_{Guid.NewGuid():N}.avif");
            try
            {
                var encResult = await EncodeToFileExAsync(input, tmpOutput, crf,
                    tileCols, proxyCfg.FinalCpuUsed, proxyCfg, jpeg, "yuv420p",
                    isTrueLossless: false, timeoutMinutes: cfg.SearchEncodeTimeoutMinutes,
                    allowParamDegrade: false);

                if (!encResult.ok || !_fs.FileExists(tmpOutput) || _fs.GetFileLength(tmpOutput) < 100)
                    return -1;

                QualityMetrics? m = await ComputeAllMetricsAsync(input, tmpOutput);
                if (m == null) return -1;

                return GetSearchScore(m, cfg.MetricMode ?? "vmaf");
            }
            finally
            {
                if (_fs.FileExists(tmpOutput)) try { _fs.DeleteFile(tmpOutput); } catch { }
            }
        }



        private async Task<(int crf, bool searchFailed, bool qualityInsufficient, int evalCount)> HybridSearchCRFAsync(
    string input, int tileCols, PresetConfig cfg, string pixFmt, bool jpeg)
        {
            string name = Path.GetFileName(input);
            double target = cfg.TargetSSIM + SSIMMargin;
            _logger.LogSearch($"[{name}] 混合搜索 目标={target:F4} 模式={cfg.MetricMode ?? "vmaf"}");

            using var searchCts = new CancellationTokenSource(TimeSpan.FromMinutes(cfg.SearchTimeoutMinutes));
            var token = CancellationTokenSource.CreateLinkedTokenSource(searchCts.Token, _globalCts?.Token ?? default).Token;

            Func<int, Task<double>> getScore = BuildGetScoreFunc(input, tileCols, cfg, pixFmt, jpeg, name, token);
            int totalEvalCount = 0;

            try
            {
                // 1. MaxCRF 提前早停
                double maxScore = await getScore(cfg.MaxCRF);
                totalEvalCount++;
                if (maxScore >= target)
                {
                    SafeWriteLine($"  [{name}] MaxCRF={cfg.MaxCRF} 已达目标，直接使用");
                    _logger.LogSearch($"[{name}] MaxCRF 早停，CRF={cfg.MaxCRF}");
                    return (cfg.MaxCRF, false, false, totalEvalCount);
                }
                if (maxScore < 0)
                    _logger.LogSearch($"[{name}] MaxCRF 早停评估失败，继续搜索");

                // 2. Proxy 粗定位
                var (low, high, recommendedFmt) = await PerformProxyPhaseAsync(input, tileCols, cfg, pixFmt, jpeg, name, target, getScore, token);
                if (low < 0) return (cfg.BaseCRF, true, false, totalEvalCount);
                _logger.LogSearch($"[{name}] Proxy 粗定位后区间: [{low}, {high}]");

                // 3. 精搜索（Brent/Secant）
                var (crf, failed, insufficient, brentEvalCount) = await SearchCoreAsync(input, tileCols, cfg, pixFmt, jpeg, name, target, low, high, getScore, token);
                totalEvalCount += brentEvalCount;
                if (!failed && !insufficient) return (crf, false, false, totalEvalCount);

                // 4. 若精搜失败或质量不足，尝试安全模式全扫描（yuv420p）
                SafeWriteLine($" [RETRY] [{name}] 精搜索失败，开始安全模式全扫描 (yuv420p)...");
                var (safeOk, safeCrf, safePixFmt, safeMode) = await RunSafeModeScan(input, cfg, name, cfg.MinCRF, cfg.MaxCRF);
                if (safeOk)
                {
                    _logger.LogSearch($"[{name}] 安全扫描成功，CRF={safeCrf}");
                    return (safeCrf, false, false, totalEvalCount);   // 安全模式不计入额外评估次数
                }

                // 全部失败
                _logger.LogSearch($"[{name}] 所有搜索策略失败，回退到 BaseCRF={cfg.BaseCRF}");
                return (cfg.BaseCRF, true, false, totalEvalCount);
            }
            catch (OperationCanceledException)
            {
                SafeWriteLine($" [{name}] [WARN] 搜索超时/取消，用 BaseCRF {cfg.BaseCRF}");
                _logger.LogSearch($"[{name}] 搜索被取消");
                return (cfg.BaseCRF, true, false, totalEvalCount);
            }
        }



        private Func<int, Task<double>> BuildGetScoreFunc(string input, int tileCols, PresetConfig cfg, string pixFmt, bool jpeg, string name, CancellationToken token)
        {
            int consecutiveFailures = 0;
            const int failThreshold = 2;
            string normalizedKey = GetNormalizedPathForCache(input);

            return async crf =>
            {
                // 提前致命短路：若该文件的当前 pixFmt 已被标记为致命，直接失败
                if (_fatalFmts.TryGetValue(normalizedKey, out var fatalSet) && fatalSet.Contains(pixFmt))
                {
                    _logger.LogInfo($"⚠️ 致命格式 {pixFmt} 已禁用，跳过 CRF={crf} [{name}]");
                    return -1;
                }

                for (int i = 0; i < 3; i++)
                {
                    token.ThrowIfCancellationRequested();

                    QualityMetrics? m = null;

                    if (consecutiveFailures < failThreshold)
                    {
                        m = await GetOrComputeMetrics(input, crf, tileCols, cfg.SearchCpuUsed, cfg, jpeg, pixFmt);
                        if (m != null) { consecutiveFailures = 0; return GetSearchScore(m, cfg.MetricMode ?? "ssim"); }

                        m = await GetOrComputeMetrics(input, crf, tileCols, Math.Max(0, cfg.SearchCpuUsed - 1), cfg, jpeg, pixFmt);
                        if (m != null) { consecutiveFailures = 0; return GetSearchScore(m, cfg.MetricMode ?? "ssim"); }
                    }

                    // 仅在 yuv420p 未被标记致命时才降级尝试
                    if (!pixFmt.StartsWith("yuv420p") && (!_fatalFmts.TryGetValue(normalizedKey, out var fs) || !fs.Contains("yuv420p")))
                    {
                        m = await GetOrComputeMetrics(input, crf, tileCols, cfg.SearchCpuUsed, cfg, jpeg, "yuv420p");
                        if (m != null) { consecutiveFailures = 0; return GetSearchScore(m, cfg.MetricMode ?? "ssim"); }
                    }
                    else
                    {
                        // 当前格式就是 yuv420p 或已被致命标记，尝试降速
                        m = await GetOrComputeMetrics(input, crf, tileCols, 0, cfg, jpeg, pixFmt);
                        if (m != null) { consecutiveFailures = 0; return GetSearchScore(m, cfg.MetricMode ?? "ssim"); }
                    }

                    if (i < 2)
                        _logger.LogInfo($"真实指标重试 ({i + 1}/2): {name} CRF={crf}");
                }

                consecutiveFailures++;
                if (consecutiveFailures >= failThreshold)
                    _logger.LogInfo($"连续失败达到阈值，后续 CRF 点将优先使用降级参数 [{name}]");

                return -1;
            };
        }


        private async Task<(int low, int high, string? recommendedPixFmt)> PerformProxyPhaseAsync(
    string input, int tileCols, PresetConfig cfg, string pixFmt, bool jpeg,
    string name, double target, Func<int, Task<double>> getScore, CancellationToken token)
        {
            int globalMin = cfg.MinCRF;
            int globalMax = cfg.MaxCRF;
            int low = globalMin;
            int high = globalMax;
            string? recommendedFmt = null;

            // ─── 1. 目标格式稳定性预探测（保持原有逻辑） ───
            if (!pixFmt.StartsWith("yuv420p"))
            {
                int midForTest = (low + high) / 2;
                string probeOutput = Path.Combine(_outputDir, $"_probe_{Guid.NewGuid():N}.avif");

                var (imgW, _) = await GetResolutionAsync(input);
                int minLegalCols = GetMinLegalTileCols(imgW);
                int probeTileCols = (imgW >= 256) ? Math.Max(2, minLegalCols) : 0;

                try
                {
                    var testParams = (aomParams: cfg.GetEffectiveAomParams(),
                                      tilePart: probeTileCols > 0
                                          ? $"-tile-columns {probeTileCols} -tile-rows 0"
                                          : "-tile-columns 0 -tile-rows 0",
                                      actualCpu: 0,
                                      rowMt: EncoderUtils.IsLibAom(cfg.Encoder) ? "-row-mt 1" : "");

                    var testEnc = await TryEncodeWithParamSet(input, probeOutput,
                        midForTest, pixFmt, testParams, cfg, false, 2, name);

                    if (!testEnc.ok)
                    {
                        recommendedFmt = "yuv420p";
                        SafeWriteLine($"  [{name}] 目标格式 {pixFmt} 预探测失败，精搜索将降级为 yuv420p");
                        _logger.LogMetric("crf", $"[{name}] 目标格式稳定性预探测失败 {pixFmt} → yuv420p");
                    }
                }
                finally
                {
                    try { if (_fs.FileExists(probeOutput)) _fs.DeleteFile(probeOutput); } catch { }
                }
            }

            // ─── 2. Proxy 三点采样与区间裁剪 ───
            // 选取三个代表性的 CRF 点
            int crfLow = globalMin;
            int crfMid = (globalMin + globalMax) / 2;
            int crfHigh = globalMax;

            // 并行执行三个 Proxy 评估（容忍单个失败）
            double scoreLow = -1, scoreMid = -1, scoreHigh = -1;
            try
            {
                var taskLow = ProxyEvaluateAsync(input, crfLow, tileCols, cfg, jpeg, pixFmt);
                var taskMid = ProxyEvaluateAsync(input, crfMid, tileCols, cfg, jpeg, pixFmt);
                var taskHigh = ProxyEvaluateAsync(input, crfHigh, tileCols, cfg, jpeg, pixFmt);

                await Task.WhenAll(taskLow, taskMid, taskHigh);

                scoreLow = taskLow.Result;
                scoreMid = taskMid.Result;
                scoreHigh = taskHigh.Result;
            }
            catch
            {
                // 若并行等待异常，保持原区间不变
                _logger.LogMetric("crf", $"[{name}] Proxy 并行评估异常，保持全区间 [{low}, {high}]");
                return (low, high, recommendedFmt);
            }

            // 检查 Proxy 结果有效性
            bool lowValid = scoreLow >= 0;
            bool midValid = scoreMid >= 0;
            bool highValid = scoreHigh >= 0;

            if (!lowValid || !midValid || !highValid)
            {
                _logger.LogMetric("crf", $"[{name}] Proxy 部分节点评估失败 (low={lowValid}, mid={midValid}, high={highValid})，保持全区间 [{low}, {high}]");
                return (low, high, recommendedFmt);
            }

            _logger.LogSearch($"[{name}] Proxy 分数: CRF={crfLow} → {scoreLow:F4} | CRF={crfMid} → {scoreMid:F4} | CRF={crfHigh} → {scoreHigh:F4}");

            // 如果最高质量点仍不达标，则目标无法实现
            if (scoreLow < target)
            {
                _logger.LogMetric("crf", $"[{name}] Proxy 最低 CRF={crfLow} 质量 {scoreLow:F4} < 目标 {target:F4}，目标无法达成");
                return (-1, high, recommendedFmt);  // low = -1 表示质量不足
            }

            // 如果最差点已达标（理论上不会，因 MaxCRF 早停已处理），直接使用
            if (scoreHigh >= target)
            {
                _logger.LogMetric("crf", $"[{name}] Proxy 最高 CRF={crfHigh} 已达目标，无需收缩");
                return (low, high, recommendedFmt);
            }

            // 检查单调性：scoreLow >= scoreMid >= scoreHigh (CRF 越小质量越高)
            if (scoreLow < scoreMid || scoreMid < scoreHigh)
            {
                _logger.LogMetric("crf", $"[{name}] Proxy 点非单调，保持全区间 [{low}, {high}]");
                return (low, high, recommendedFmt);
            }

            // 安全收缩区间：在满足 low 达标、high 不达标的前提下，尽量缩小范围
            if (scoreMid >= target)
            {
                // 中点达标 → 低点收缩到中点，高点保持 MaxCRF（仍不达标）
                low = crfMid;
                high = crfHigh;
                SafeWriteLine($"  [{name}] Proxy 收缩区间: [{low}, {high}] (中点达标)");
                _logger.LogSearch($"[{name}] Proxy 区间收缩至 [{low}, {high}]");
            }
            else // scoreMid < target
            {
                // 中点不达标 → 低点保持 MinCRF（达标），高点收缩到中点
                low = crfLow;
                high = crfMid;
                SafeWriteLine($"  [{name}] Proxy 收缩区间: [{low}, {high}] (中点不达标)");
                _logger.LogSearch($"[{name}] Proxy 区间收缩至 [{low}, {high}]");
            }

            // 确保区间至少有两个不同的整数点，否则保持原区间
            if (high - low < 1)
            {
                low = globalMin;
                high = globalMax;
                _logger.LogMetric("crf", $"[{name}] Proxy 收缩后区间过窄，恢复全区间");
            }

            return (low, high, recommendedFmt);
        }





        /// <summary>
        /// 搜索公共核心：在已知区间 [low, high] 内进行精搜索，包含边界验证、割线法、二分回退、最终指标输出。
        /// 返回 (bestCrf, failed, insufficient)。
        /// failed: 表示搜索过程本身失败（如无法计算评分），需上层回退。
        /// insufficient: 表示最低 CRF 仍无法达标，目标无法实现。
        /// </summary>
        private async Task<(int crf, bool failed, bool insufficient, int brentEvalCount)> SearchCoreAsync(
    string input, int tileCols, PresetConfig cfg, string pixFmt, bool jpeg,
    string name, double target, int low, int high,
    Func<int, Task<double>> getScore, CancellationToken token)
        {
            // 高端边界探测
            double highScore = await getScore(high);
            if (highScore >= target)
            {
                SafeWriteLine($"  [HIGH] [{name}] CRF={high} 已达标，直接使用");
                _logger.LogSearch($"[{name}] 高端边界 CRF={high} 达标，直接使用");
                return (high, false, false, 0);
            }
            if (highScore < 0)
            {
                _logger.LogSearch($"[{name}] 高端边界 CRF={high} 评估失败，搜索失败");
                return (cfg.BaseCRF, true, false, 0);
            }
            _logger.LogSearch($"[{name}] 高端边界 CRF={high} 不达标，继续搜索");

            // 低端边界探测
            double lowScore = await getScore(low);
            if (lowScore < target)
            {
                SafeWriteLine($"  [LOW] [{name}] 最低可用 CRF={low} VMAF={lowScore * 100:F1} 无法达标");
                _logger.LogSearch($"[{name}] 低端边界 CRF={low} 无法达标");
                return (low, false, true, 0);
            }
            _logger.LogSearch($"[{name}] 低端边界 CRF={low} 达标");

            // Brent 求解
            (int bestCrf, double bestScore, int brentEvalCount) = await SolveCrfBrent(
                getScore, low, high, lowScore, highScore, target, name, token, cfg, input, tileCols, pixFmt, jpeg);

            _logger.LogSearch($"[{name}] Brent 完成，最佳CRF={bestCrf}，分数={bestScore:F4}，评估次数={brentEvalCount}");

            // 输出最终指标
            QualityMetrics? finalMetrics = await GetOrComputeMetrics(input, bestCrf, tileCols, cfg.SearchCpuUsed, cfg, jpeg, pixFmt);
            if (finalMetrics != null)
                SafeWriteLine($"  [BEST] [{name}] 最佳 CRF = {bestCrf} | VMAF={finalMetrics.VMAF:F1} SSIM={finalMetrics.SSIM:F4} PSNR-Y={finalMetrics.PSNR_Y:F2} MS-SSIM={finalMetrics.MS_SSIM:F4}");
            else
                SafeWriteLine($"  [BEST] [{name}] 最佳 CRF = {bestCrf}");

            return (bestCrf, false, false, brentEvalCount);
        }









        /// <summary>
        /// 获取图像分辨率，优先从统一 Probe 缓存获取。
        /// </summary>
        private async Task<(int w, int h)> GetResolutionAsync(string path)
        {
            // 优先从统一 Probe 缓存获取
            var info = await GetProbeInfoAsync(path);
            if (info != null)
            {
                return (info.Width, info.Height);
            }

            // 兜底：单独探测
            string args = $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=p=0 \"{path}\"";
            string o = await RunProbeAsync(_ffprobePath, args).WaitAsync(TimeSpan.FromSeconds(30));
            var parts = o.Trim().Split(',');
            if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
            {
                return (w, h);
            }
            return (0, 0);
        }

        private static string GetCsvRow(EncodeResult r)
        {
            string status = r.Skipped ? "跳过" : (r.Success ? "成功" : "失败");
            string errMsg = CsvEscape(r.ErrorMessage);
            string fmt = r.PixelFormat ?? "";
            string srcFmt = r.SourcePixelFormat ?? "";
            string mode = r.Mode ?? "";
            string safe = r.IsSafeMode ? "是" : "否";
            string command = CsvEscape(r.CommandLine ?? "");
            string aomParams = CsvEscape(r.AomParamsUsed ?? "");
            string cache = r.CacheReused ? "是" : "否";

            string vmaf = r.FinalVMAF?.ToString("F2", CultureInfo.InvariantCulture) ?? "";
            string psnrY = r.FinalPSNR_Y?.ToString("F2", CultureInfo.InvariantCulture) ?? "";
            string msssim = r.FinalMSSSIM?.ToString("F4", CultureInfo.InvariantCulture) ?? "";
            string mix = r.FinalMixScore?.ToString("F4", CultureInfo.InvariantCulture) ?? "";

            // 按 CsvColumnNames 顺序拼接
            var values = new[]
            {
        CsvEscape(r.FileName),
        CsvEscape(r.OriginalFileName),
        r.OriginalSize.ToString(CultureInfo.InvariantCulture),
        r.OutputSize.ToString(CultureInfo.InvariantCulture),
        r.CompressionRatio.ToString("F4", CultureInfo.InvariantCulture),
        r.UsedCRF.ToString(CultureInfo.InvariantCulture),
        r.FinalSSIM.ToString("F4", CultureInfo.InvariantCulture),
        vmaf,
        psnrY,
        msssim,
        mix,
        r.EncodeTime.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture),
        r.SearchTime.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture),
        r.TotalTime.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture),
        r.Retries.ToString(CultureInfo.InvariantCulture),
        CsvEscape(fmt),
        CsvEscape(srcFmt),
        CsvEscape(mode),
        CsvEscape(safe),
        aomParams,
        command,
        CsvEscape(cache),
        CsvEscape(status),
        errMsg,
        r.SearchEvaluations.ToString(CultureInfo.InvariantCulture)   // ★ 新增
    };

            return string.Join(",", values);
        }

        private void ExportCsv(IEnumerable<EncodeResult> results)
        {
            string p = Path.Combine(_outputDir, "avif_stats.csv");
            var sb = new StringBuilder();

            // 表头
            sb.AppendLine(string.Join(",", CsvColumnNames));

            foreach (var r in results)
            {
                sb.AppendLine(GetCsvRow(r));
            }

            _fs.WriteAllText(p, sb.ToString(), new UTF8Encoding(true));
            SafeWriteLine($"CSV 已保存: {p}");
        }

        private static string FormatSize(long b) => b switch
        {
            >= 1_048_576 => $"{b / 1_048_576.0:F2} MB",
            >= 1024 => $"{b / 1024.0:F2} KB",
            _ => $"{b} B"
        };

        private static string FormatTimeSpan(TimeSpan t) => t switch
        {
            { TotalHours: >= 1 } => $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s",
            { TotalMinutes: >= 1 } => $"{(int)t.TotalMinutes}m {t.Seconds}s",
            _ => $"{t.TotalSeconds:F1}s"
        };
    }


    // ========================================
    // 新增：进程抽象层（放在 namespace AvifEncoder 内）
    // ========================================

    /// <summary>封装外部进程调用的接口，便于替换和测试</summary>
    public interface IProcessRunner
    {
        /// <summary>
        /// 运行指定的可执行文件，返回 (退出码, 标准输出, 标准错误)。
        /// </summary>
        Task<(int exitCode, string stdout, string stderr)> RunAsync(
            string fileName,
            string arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default);
    }

    /// <summary>使用真实操作系统进程的默认实现</summary>
    public class RealProcessRunner : IProcessRunner
    {
        public async Task<(int exitCode, string stdout, string stderr)> RunAsync(
            string fileName,
            string arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(fileName, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

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
            return (process.ExitCode, stdout, stderr);
        }
    }





    /// <summary>封装进度统计、ETA 计算与进度行格式化</summary>
    public class ProgressTracker
    {
        private DateTime _startTime;
        private int _totalFiles;
        private int _processedCount;

        public DateTime StartTime => _startTime;
        public int ProcessedCount => Volatile.Read(ref _processedCount);
        public int TotalFiles => _totalFiles;

        public void Start(DateTime startTime) => _startTime = startTime;
        public void SetTotalFiles(int count) => _totalFiles = count;

        public void MarkFileProcessed()
        {
            Interlocked.Increment(ref _processedCount);
        }

        public string GetProgressLine(EncodeResult? r)
        {
            int done = ProcessedCount, total = TotalFiles;
            double pct = done * 100.0 / total;
            var elapsed = DateTime.Now - _startTime;
            string eta = "计算中...";
            if (done > 0 && done < total)
                eta = FormatTimeSpanLocal(TimeSpan.FromSeconds(elapsed.TotalSeconds / done * (total - done)));
            else if (done == total)
                eta = "已完成";
            string line = $"[{done}/{total} {pct,5:F1}%]";

            if (r != null)
            {
                if (r.Skipped)
                    return $"{line} [SKIP] 跳过 {r.FileName} | {r.OriginalFileName}";
                if (r.Success)
                {
                    string qualityStr = $"VMAF={r.FinalVMAF?.ToString("F1") ?? "N/A"}  PSNR-Y={r.FinalPSNR_Y?.ToString("F2") ?? "N/A"}dB  SSIM={r.FinalSSIM:F4}  MS-SSIM={r.FinalMSSSIM?.ToString("F4") ?? "N/A"}";
                    return $"{line} [OK] {r.FileName} | {r.OriginalFileName} | CRF:{r.UsedCRF} | " +
                           $"{FormatSizeLocal(r.OriginalSize)} -> {FormatSizeLocal(r.OutputSize)} | " +
                           $"{r.CompressionRatio:P1} | {qualityStr} | 总耗时:{r.TotalTime.TotalSeconds:F1}s | 剩余 {eta}";
                }
                return $"{line} [FAIL] 失败 | {r.OriginalFileName} | 原因:{r.ErrorMessage} | 总耗时:{r.TotalTime.TotalSeconds:F1}s | 剩余 {eta}";
            }
            return $"{line} [SKIP] 跳过";
        }

        private static string FormatSizeLocal(long b) => b switch
        {
            >= 1_048_576 => $"{b / 1_048_576.0:F2} MB",
            >= 1024 => $"{b / 1024.0:F2} KB",
            _ => $"{b} B"
        };

        private static string FormatTimeSpanLocal(TimeSpan t) => t switch
        {
            { TotalHours: >= 1 } => $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s",
            { TotalMinutes: >= 1 } => $"{(int)t.TotalMinutes}m {t.Seconds}s",
            _ => $"{t.TotalSeconds:F1}s"
        };
    }


    /// <summary>编码器类型判断与通用工具方法</summary>
    internal static class EncoderUtils
    {
        public static bool IsLibAom(string encoder) =>
            encoder.StartsWith("libaom-av1", StringComparison.OrdinalIgnoreCase);

        public static bool IsSvtAv1(string encoder) =>
            encoder.StartsWith("libsvtav1", StringComparison.OrdinalIgnoreCase);

        public static bool IsRav1e(string encoder) =>
            encoder.StartsWith("librav1e", StringComparison.OrdinalIgnoreCase);

        /// <summary>是否为软件编码器（lib 开头）</summary>
        public static bool IsSoftwareEncoder(string encoder) =>
            encoder.StartsWith("lib", StringComparison.OrdinalIgnoreCase);

        /// <summary>是否支持 still-picture 参数</summary>
        public static bool SupportsStillPicture(string encoder) =>
            IsLibAom(encoder);

        /// <summary>是否支持 AOM 高级参数（目前仅 libaom-av1 支持）</summary>
        public static bool SupportsAomParams(string encoder) =>
            IsLibAom(encoder);

        /// <summary>在 PATH 环境变量中查找可执行文件</summary>
        public static string? FindExecutable(string name)
        {
            // 1. 优先在应用程序所在目录中查找（便携/免安装部署）
            string? appDir = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(appDir))
            {
                string localFile = Path.Combine(appDir,
                    OperatingSystem.IsWindows() ? $"{name}.exe" : name);
                if (File.Exists(localFile))
                    return localFile;
            }

            // 2. 回退到 PATH 环境变量
            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator);
            foreach (var p in paths ?? Array.Empty<string>())
            {
                string full = Path.Combine(p,
                    OperatingSystem.IsWindows() ? $"{name}.exe" : name);
                if (File.Exists(full))
                    return full;
            }

            return null;
        }
    }






    class Program
    {



        [DllImport("kernel32.dll")]
        static extern IntPtr GetStdHandle(int nStdHandle);
        [DllImport("kernel32.dll")]
        static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
        [DllImport("kernel32.dll")]
        static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        static int GetDefaultThreads() => Math.Max(2, (int)Math.Sqrt(Environment.ProcessorCount));

        // ========== 帮助文本 ==========
        static void PrintHelp()
        {
            Console.WriteLine(@"
AVIF 编码器 —— Linux 风格命令行工具

用法:
  AvifEncoder --input <目录> --output <目录> [选项]
  AvifEncoder -i <目录> -o <目录> [选项]

主要选项:
  -i, --input <目录>           输入目录 (默认: input)
  -o, --output <目录>          输出目录 (默认: Avifoutput)
  -p, --preset <预设>          预设模式: fast, balanced, best, extreme (默认: extreme)
  -e, --encoder <名称>         指定 AV1 编码器 (默认: libaom-av1)
  -j, --jobs <数量>            并行任务数 (默认: 根据 CPU 自动计算)

质量控制:
  -s, --search                 启用 CRF 搜索 (默认启用)
      --no-search              禁用 CRF 搜索
      --metric <模式>          质量评价模式: vmaf, ssim, psnr, msssim, mix (默认 vmaf)
      --target-vmaf <0-100>    直接设置 VMAF 目标，自动切换模式
      --target-ssim <0-1>      直接设置 SSIM 目标
      --target-psnr <dB>       直接设置 PSNR-Y 目标 (典型 30-50)
      --target-msssim <0-1>    直接设置 MS-SSIM 目标
      --target-mix <0-1>       直接设置加权混合评分目标
      --crf <整数>             手动指定固定 CRF (1-50，同时禁用搜索)
      --crf <最小值>:<最大值>  设置 CRF 搜索范围 (例如 10:50，自动启用搜索)

像素格式:
  -c, --chroma <采样>          色度采样: 420, 422, 444, auto (默认: auto)
  -b, --bit-depth <位数>       输出位深: 8 或 10

其他编码选项:
  -l, --lossless               无损模式 (真无损或数学无损)
  -t, --output-template <模板> 输出文件名模板 (默认: covers-{index}.avif)
  -r, --recursive              递归处理子目录
      --max-resolution <像素>  长边最大分辨率 (默认 2560, 0 禁用预缩放)
      --output-full-res        最终输出保留原始分辨率 (搜索和指标使用缩放后图像)
      --timeout-encode <分钟>  单次最终编码超时 (默认自动计算)
      --timeout-search <分钟>  搜索阶段全局超时 (默认 60)
      --timeout-safe <分钟>    安全模式全扫描超时 (默认 180)
      --timeout-safe-encode <分钟> 安全模式单次编码超时 (默认 10)
      --timeout-search-encode <分钟> 搜索过程中临时编码超时 (默认 10)
      --timeout-ssim <分钟>    SSIM 计算超时 (默认 5)

通用选项:
  -v, --verbose                详细输出
  -q, --quiet                  安静模式，仅输出错误
  -D, --dry-run                仅打印配置，不实际编码
  -V, --version                显示版本信息
  -h, --help                   显示此帮助信息

示例:
  # 基础用法
  AvifEncoder -i ./图片 -o ./输出

  # 最佳预设 + 目标 VMAF 95
  AvifEncoder --preset best --target-vmaf 95

  # 使用 420 色度、8bit、固定 CRF 30、不搜索
  AvifEncoder -c 420 -b 8 --crf 30 --no-search

  # 自定义搜索范围与超时
  AvifEncoder --crf 10:45 --target-ssim 0.98 --timeout-search 120
");
        
}

        // ========== 参数解析数据类 ==========
        private class ParsedOptions
        {
            public string InputDir = "input";
            public string OutputDir = "Avifoutput";
            public CliPreset Preset = CliPreset.Extreme;
            public bool EnableSearch = true;          // 默认启用搜索
            public bool ForceNoSearch = false;        // --no-search
            public double? QualityTarget;             // --quality
            public string MetricMode = "vmaf";
            public string? DirectTargetMode;          // --target-xxx 对应的度量名
            public double? DirectTargetValue;
            public int? ManualCrf;                    // --crf 单值
            public int? CrfMin, CrfMax;               // --crf min:max
            public string Chroma = "auto";            // --chroma 420/422/444/auto
            public int? BitDepth;                     // --bit-depth 8/10
            public bool Lossless = false;
            public string? OutputTemplate;
            public string Encoder = "libaom-av1";
            public int? Jobs;                         // -j / --jobs
            public bool Recursive = false;
            public int? MaxResolution;
            public bool OutputFullRes = false;
            // 超时（分钟）
            public int? EncodeTimeout, SearchTimeout, SafeTimeout,
                        SafeEncodeTimeout, SearchEncodeTimeout, SsimTimeout;
            public bool Verbose = false;
            public bool Quiet = false;
            public bool ShowVersion = false;
            public bool DryRun = false;
        }

        // ========== 参数解析 ==========
        // ========== 6. ParseCommandLineArgs（仅展示关键新增，需要整合到单字符解析中） ==========
        // 在原有的单字符选项解析部分增加：
        //
        //     else if (flags.Equals("R")) { opts.Recurse = true; }
        // 或者支持 --recursive 长参数
        //
        // 以下为完整方法，包含原有逻辑及新增项
        private static ParsedOptions ParseCommandLineArgs(string[] args)
        {
            var opts = new ParsedOptions();
            int i = 0;
            while (i < args.Length)
            {
                string arg = args[i];

                // 选项结束符
                if (arg == "--") { i++; break; }

                // 长选项
                if (arg.StartsWith("--"))
                {
                    string key = arg.Substring(2);
                    string? value = null;
                    int eq = key.IndexOf('=');
                    if (eq >= 0) { value = key.Substring(eq + 1); key = key.Substring(0, eq); }

                    // 需要值的辅助函数
                    string GetValue() => value ?? (++i < args.Length ? args[i] : throw new Exception($"选项 --{key} 缺少值"));

                    switch (key)
                    {
                        case "input": opts.InputDir = GetValue(); break;
                        case "output": opts.OutputDir = GetValue(); break;
                        case "preset":
                            opts.Preset = GetValue().ToLower() switch
                            {
                                "fast" => CliPreset.Fast,
                                "balanced" => CliPreset.Balanced,
                                "best" => CliPreset.Best,
                                "extreme" => CliPreset.Extreme,
                                _ => throw new Exception("预设参数错误")
                            };
                            break;
                        case "search": opts.EnableSearch = true; opts.ForceNoSearch = false; break;
                        case "no-search": opts.ForceNoSearch = true; opts.EnableSearch = false; break;
                        case "quality": opts.QualityTarget = double.Parse(GetValue()); break;
                        case "metric": opts.MetricMode = GetValue().ToLower(); break;
                        case "target-vmaf": opts.DirectTargetMode = "vmaf"; opts.DirectTargetValue = double.Parse(GetValue()); break;
                        case "target-ssim": opts.DirectTargetMode = "ssim"; opts.DirectTargetValue = double.Parse(GetValue()); break;
                        case "target-psnr": opts.DirectTargetMode = "psnr"; opts.DirectTargetValue = double.Parse(GetValue()); break;
                        case "target-msssim": opts.DirectTargetMode = "msssim"; opts.DirectTargetValue = double.Parse(GetValue()); break;
                        case "target-mix": opts.DirectTargetMode = "mix"; opts.DirectTargetValue = double.Parse(GetValue()); break;
                        case "crf":
                            string crfVal = GetValue();
                            if (crfVal.Contains(':'))
                            {
                                var parts = crfVal.Split(':');
                                if (parts.Length == 2 &&
                                    int.TryParse(parts[0], out int min) && min >= 0 && min <= 63 &&
                                    int.TryParse(parts[1], out int max) && max >= 0 && max <= 63 && min < max)
                                { opts.CrfMin = min; opts.CrfMax = max; opts.EnableSearch = true; }
                                else throw new Exception("CRF 范围格式错误");
                            }
                            else
                            {
                                if (int.TryParse(crfVal, out int r) && r >= 1 && r <= 50)
                                { opts.ManualCrf = r; opts.ForceNoSearch = true; }
                                else throw new Exception("CRF 应为 1-50 的整数");
                            }
                            break;
                        case "chroma":
                            string c = GetValue().ToLower();
                            if (new[] { "420", "422", "444", "auto" }.Contains(c))
                                opts.Chroma = c;
                            else throw new Exception("--chroma 仅支持 420/422/444/auto");
                            break;
                        case "bit-depth":
                            if (int.TryParse(GetValue(), out int bd) && (bd == 8 || bd == 10))
                                opts.BitDepth = bd;
                            else throw new Exception("--bit-depth 必须为 8 或 10");
                            break;
                        case "lossless": opts.Lossless = true; break;
                        case "output-template": opts.OutputTemplate = GetValue().Trim('"', '\''); break;
                        case "encoder": opts.Encoder = GetValue(); break;
                        case "jobs":
                            if (int.TryParse(GetValue(), out int jobs) && jobs > 0)
                                opts.Jobs = jobs;
                            else throw new Exception("--jobs 需要正整数");
                            break;
                        case "recursive": opts.Recursive = true; break;
                        case "max-resolution":
                            if (int.TryParse(GetValue(), out int mr) && mr >= 0)
                                opts.MaxResolution = mr;
                            else throw new Exception("--max-resolution 需要非负整数");
                            break;
                        case "output-full-res": opts.OutputFullRes = true; break;
                        case "verbose": opts.Verbose = true; break;
                        case "quiet": opts.Quiet = true; break;
                        case "version": opts.ShowVersion = true; break;
                        case "dry-run": opts.DryRun = true; break;
                        case "help": PrintHelp(); return null!;
                        // 超时选项
                        default:
                            if (key.StartsWith("timeout-"))
                            {
                                string type = key.Substring("timeout-".Length);
                                if (!int.TryParse(GetValue(), out int val) || val <= 0)
                                    throw new Exception($"--{key} 需要正整数");
                                switch (type)
                                {
                                    case "encode": opts.EncodeTimeout = val; break;
                                    case "search": opts.SearchTimeout = val; break;
                                    case "safe": opts.SafeTimeout = val; break;
                                    case "safe-encode": opts.SafeEncodeTimeout = val; break;
                                    case "search-encode": opts.SearchEncodeTimeout = val; break;
                                    case "ssim": opts.SsimTimeout = val; break;
                                    default: throw new Exception($"未知超时选项 --{key}");
                                }
                            }
                            else throw new Exception($"未知选项 --{key}");
                            break;
                    }
                    i++;
                    continue;
                }

                // 短选项
                if (arg.StartsWith('-') && arg.Length > 1 && !char.IsDigit(arg[1]))
                {
                    string flags = arg.Substring(1);
                    // 带值的短选项（需要下一个参数）
                    if (flags == "i" || flags == "o" || flags == "p" || flags == "c" || flags == "b" ||
                        flags == "t" || flags == "e" || flags == "j")
                    {
                        if (++i >= args.Length) throw new Exception($"选项 -{flags} 缺少值");
                        string val = args[i];
                        switch (flags)
                        {
                            case "i": opts.InputDir = val; break;
                            case "o": opts.OutputDir = val; break;
                            case "p":
                                opts.Preset = val.ToLower() switch
                                {
                                    "fast" => CliPreset.Fast,
                                    "balanced" => CliPreset.Balanced,
                                    "best" => CliPreset.Best,
                                    "extreme" => CliPreset.Extreme,
                                    _ => throw new Exception("预设参数错误")
                                };
                                break;
                            case "c":
                                if (new[] { "420", "422", "444", "auto" }.Contains(val.ToLower()))
                                    opts.Chroma = val.ToLower();
                                else throw new Exception("-c 仅支持 420/422/444/auto");
                                break;
                            case "b":
                                if (int.TryParse(val, out int bd2) && (bd2 == 8 || bd2 == 10))
                                    opts.BitDepth = bd2;
                                else throw new Exception("-b 必须为 8 或 10");
                                break;
                            case "t": opts.OutputTemplate = val.Trim('"', '\''); break;
                            case "e": opts.Encoder = val; break;
                            case "j":
                                if (int.TryParse(val, out int j) && j > 0) opts.Jobs = j;
                                else throw new Exception("-j 需要正整数");
                                break;
                        }
                        i++;
                        continue;
                    }

                    // 无值短选项组合
                    foreach (char c in flags)
                    {
                        switch (c)
                        {
                            case 's': opts.EnableSearch = true; opts.ForceNoSearch = false; break;
                            case 'l': opts.Lossless = true; break;
                            case 'r': opts.Recursive = true; break;
                            case 'v': opts.Verbose = true; break;
                            case 'q': opts.Quiet = true; break;
                            case 'V': opts.ShowVersion = true; break;
                            case 'D': opts.DryRun = true; break;
                            case 'h': PrintHelp(); return null!;
                            default: throw new Exception($"未知短选项 -{c}");
                        }
                    }
                    i++;
                    continue;
                }

                throw new Exception($"无法识别的参数: {arg}");
            }
            return opts;
        }

        // ========== 根据解析结果构建配置 ==========
        // ========== 7. BuildPresetConfig ==========
        // ==================== 配置构建器 ====================
        private static PresetConfig BuildPresetConfig(ParsedOptions opts)
        {
            PresetConfig config;

            // ---------- 1. 基础预设与无损模式 ----------
            if (opts.Lossless)
            {
                config = new PresetConfig
                {
                    BaseCRF = 0,
                    TargetSSIM = 1.0,
                    FinalCpuUsed = 0,
                    SearchCpuUsed = 0,
                    UseCRFSearch = false,
                    Lossless = true,
                    PixelFormat = null,                // 无损模式自动选择合适格式
                    AomParams = "aq-mode=0:deltaq-mode=0:enable-chroma-deltaq=0",
                    MaxJobs = opts.Jobs ?? Math.Max(2, (int)Math.Sqrt(Environment.ProcessorCount)),
                    Encoder = opts.Encoder,
                    BitDepth = 10                     // 无损默认高精度
                };
                // 无损模式下忽略大部分质量参数，直接返回
                return config;
            }

            // ---------- 2. 从预设创建基础配置 ----------
            config = AvifPipeline.CreateFromPreset(opts.Preset);

            // 手动覆盖编码器
            config.Encoder = opts.Encoder;

            // 搜索开关
            if (opts.ForceNoSearch)
                config.UseCRFSearch = false;
            else if (opts.EnableSearch)
                config.UseCRFSearch = true;   // 保持预设，除非显式要求

            // ---------- 3. 色彩采样与位深 ----------
            if (opts.Chroma != "auto")
            {
                config.AutoSource = false;
                config.UserSetChroma = true;
                // 构建像素格式字符串（位深稍后统一处理）
                config.PixelFormat = opts.Chroma switch
                {
                    "420" => "yuv420p",
                    "422" => "yuv422p",
                    "444" => "yuv444p",
                    _ => "yuv420p"
                };
            }

            if (opts.BitDepth.HasValue)
            {
                config.BitDepth = opts.BitDepth.Value;
                config.UserSetBitDepth = true;
                config.AutoSource = false;   // 手动指定位深则关闭自适应
            }

            // 调用 ApplyBitDepth 确保 PixelFormat 后缀与 BitDepth 一致
            AvifPipeline.ApplyBitDepth(config);

            // ---------- 4. 质量目标处理 ----------
            // 直接目标优先（--target-vmaf 等）
            if (opts.DirectTargetValue.HasValue && !string.IsNullOrEmpty(opts.DirectTargetMode))
            {
                opts.MetricMode = opts.DirectTargetMode;
                opts.QualityTarget = opts.DirectTargetValue;
            }

            if (opts.QualityTarget.HasValue)
            {
                string effectiveMetric = opts.MetricMode ?? config.MetricMode ?? "vmaf";
                config.MetricMode = effectiveMetric;
                config.SetQualityTarget(opts.QualityTarget.Value, effectiveMetric);
            }
            else
            {
                // 未手动指定质量时，使用预设目标并根据度量模式调整上限
                config.AdjustTargetForMetricMode();
            }

            // 设置度量模式（可能被 DirectTarget 覆盖，也可能直接通过 --metric 设置）
            if (!string.IsNullOrEmpty(opts.MetricMode))
                config.MetricMode = opts.MetricMode;

            // ---------- 5. CRF 固定值与搜索范围 ----------
            if (opts.ManualCrf.HasValue)
            {
                config.BaseCRF = opts.ManualCrf.Value;
                // 手动固定 CRF 且非显式搜索时，禁用搜索
                if (!opts.EnableSearch)
                    config.UseCRFSearch = false;
            }

            if (opts.CrfMin.HasValue)
                config.MinCRF = opts.CrfMin.Value;
            if (opts.CrfMax.HasValue)
                config.MaxCRF = opts.CrfMax.Value;

            // 范围合法性检查
            if (config.MinCRF >= config.MaxCRF)
                throw new Exception("最小 CRF 必须小于最大 CRF");

            // ---------- 6. 并行任务数 ----------
            if (opts.Jobs.HasValue)
            {
                config.MaxJobs = opts.Jobs.Value;
                config.UserSpecifiedMaxJobs = true;
            }

            // ---------- 7. 输出模板 ----------
            if (!string.IsNullOrEmpty(opts.OutputTemplate))
                config.OutputNameFormat = opts.OutputTemplate;

            // ---------- 8. 分辨率与缩放策略 ----------
            if (opts.MaxResolution.HasValue)
                config.MaxResolution = opts.MaxResolution.Value;
            config.ApplyScalingToOutput = !opts.OutputFullRes;

            // ---------- 9. 递归子目录 ----------
            config.RecurseSubdirectories = opts.Recursive;

            // ---------- 10. 超时配置 ----------
            if (opts.EncodeTimeout.HasValue) config.EncodeTimeoutMinutes = opts.EncodeTimeout.Value;
            if (opts.SearchTimeout.HasValue) config.SearchTimeoutMinutes = opts.SearchTimeout.Value;
            if (opts.SafeTimeout.HasValue) config.SafeTimeoutMinutes = opts.SafeTimeout.Value;
            if (opts.SafeEncodeTimeout.HasValue) config.SafeEncodeTimeoutMinutes = opts.SafeEncodeTimeout.Value;
            if (opts.SearchEncodeTimeout.HasValue) config.SearchEncodeTimeoutMinutes = opts.SearchEncodeTimeout.Value;
            if (opts.SsimTimeout.HasValue) config.SsimTimeoutMinutes = opts.SsimTimeout.Value;

            return config;
        }

        // ========== 程序入口 ==========
        // ========== 修复后的 Main 方法 ==========
        // ========== 修复后的 Main 方法（支持交互模式引号路径） ==========
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            // 快速编辑模式不再使用 -e，如需保留可改为隐藏参数（例如 --win-quick-edit），此处完全移除旧逻辑

            // 预先检查 ffmpeg 是否可用
            if (EncoderUtils.FindExecutable("ffmpeg") == null)
            {
                Console.WriteLine("[FAIL] 错误: ffmpeg 未找到，请确认 ffmpeg 已安装并添加到 PATH 环境变量中。");
                Console.WriteLine("按任意键退出...");
                Console.ReadKey();
                return;
            }

            // 无参数交互模式
            if (args.Length == 0)
            {
                PrintHelp();
                string logOutputDir = "Avifoutput";
                string logInputDir = "input";
                Logger.Init(logOutputDir);

                Console.WriteLine("\n正在检测可用的 AV1 编码器...");
                var allEncoders = await GetAvailableEncodersListAsync();
                Console.WriteLine($"当前 ffmpeg 支持的 AV1 编码器: {string.Join(", ", allEncoders)}");

                Console.WriteLine("\n正在测试编码器实际可用性...");
                var encoderStatuses = await TestEncodersAsync(allEncoders, logOutputDir, logInputDir);

                Console.WriteLine("\n编码器可用性测试结果");
                Console.WriteLine("----------------------------------------");

                var availableList = encoderStatuses.Where(e => e.available).ToList();
                var unavailableList = encoderStatuses.Where(e => !e.available).ToList();

                if (availableList.Count > 0)
                {
                    Console.WriteLine("[可用的编码器]");
                    var softAvail = availableList.Where(e => e.name.StartsWith("lib")).ToList();
                    var hardAvail = availableList.Where(e => !e.name.StartsWith("lib")).ToList();

                    if (softAvail.Count > 0)
                    {
                        Console.WriteLine("  -- 软件编码器（推荐） --");
                        foreach (var (name, _, _) in softAvail)
                            Console.WriteLine($"  [OK] {name,-12}  (--encoder {name})");
                    }
                    if (hardAvail.Count > 0)
                    {
                        Console.WriteLine("  -- 硬件编码器 --");
                        foreach (var (name, _, _) in hardAvail)
                            Console.WriteLine($"  [OK] {name,-12}  (--encoder {name})");
                    }
                }

                if (unavailableList.Count > 0)
                {
                    Console.WriteLine("\n[不可用的编码器]");
                    foreach (var (name, _, note) in unavailableList)
                        Console.WriteLine($"  [FAIL] {name,-12} ({note})");
                }

                Console.WriteLine("----------------------------------------");
                Console.WriteLine("提示: 同一编码器可能因图片格式/尺寸在运行时降级或回退，属正常保护机制。");

                Console.WriteLine("\n请输入命令参数 (例如 -s -p best)");
                Console.Write("> ");
                string? line = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(line))
                    args = ParseCommandLineInteractive(line);   // ★ 使用引号感知分割
                else
                { Console.WriteLine("未输入参数，退出。"); Console.ReadKey(); return; }
            }

            // 解析参数
            ParsedOptions? opts = ParseCommandLineArgs(args);
            if (opts == null) return;

            // 显示版本
            if (opts.ShowVersion)
            {
                Console.WriteLine("AvifEncoder v2.0 (Linux-style CLI)");
                return;
            }

            // 构建配置
            PresetConfig config = BuildPresetConfig(opts);

            // 模拟运行
            if (opts.DryRun)
            {
                Console.WriteLine("== Dry Run ==");
                Console.WriteLine($"Input: {opts.InputDir}\nOutput: {opts.OutputDir}");
                Console.WriteLine($"Encoder: {config.Encoder}\nSearch: {config.UseCRFSearch}");
                Console.WriteLine($"Target: {config.TargetSSIM} (Metric: {config.MetricMode})");
                Console.WriteLine($"CRF: {config.BaseCRF}, Chroma: {opts.Chroma}, BitDepth: {config.BitDepth}");
                return;
            }

            // 实际运行流水线
            AvifPipeline? pipeline = null;
            try
            {
                var fileLogger = new FileLogger(opts.OutputDir);
                Logger.SetInstance(fileLogger);
                var cache = new CacheManager();

                pipeline = new AvifPipeline(opts.InputDir, opts.OutputDir, config,
                    logger: fileLogger,
                    processRunner: null,
                    fileSystem: new PresetConfig.RealFileSystem(),
                    cacheManager: cache);
                await pipeline.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] 错误: {ex.Message}");
            }
            finally
            {
                pipeline?.Dispose();
                Console.WriteLine("按任意键退出...");
                Console.ReadKey();
            }
        }

        // ========== 引号感知的命令行交互分割方法 ==========
        private static string[] ParseCommandLineInteractive(string line)
        {
            var args = new List<string>();
            bool inQuotes = false;
            var current = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        args.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            if (current.Length > 0)
                args.Add(current.ToString());
            return args.ToArray();
        }

        // ========== 获取 ffmpeg 支持的 AV1 编码器列表 ==========
        private static async Task<List<string>> GetAvailableEncodersListAsync()
        {
            var encoders = new List<string>();
            try
            {
                using var p = new Process
                {
                    StartInfo = new ProcessStartInfo("ffmpeg", "-encoders")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                p.Start();
                var outTask = p.StandardOutput.ReadToEndAsync();
                var errTask = p.StandardError.ReadToEndAsync();
                await Task.WhenAll(outTask, errTask, p.WaitForExitAsync());

                using var reader = new StringReader(await outTask);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.TrimStart();
                    if (trimmed.Length > 0 && trimmed[0] == 'V' && trimmed.Contains("av1"))
                    {
                        string[] parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            string name = parts[1];
                            if (!encoders.Contains(name))
                                encoders.Add(name);
                        }
                    }
                }
            }
            catch { }
            return encoders;
        }

        // ========== 编码器实际可用性测试 ==========
        private static async Task<List<(string name, bool available, string note)>> TestEncodersAsync(
    List<string> encoders, string outputDir, string inputDir = "input")
        {
            // 创建测试 BMP
            byte[] testBmp = CreateTestBmp();
            string testDir = Path.Combine(outputDir, "_encoder_test");
            Directory.CreateDirectory(testDir);
            string testInput = Path.Combine(testDir, "test_input.bmp");
            File.WriteAllBytes(testInput, testBmp);
            Logger.Log("======== 编码器可用性测试开始 ========");
            try
            {
                // ★ 并发测试所有编码器
                var tasks = encoders.Select(enc => TestSingleEncoderAsync(enc, testInput, testDir));
                var results = await Task.WhenAll(tasks);
                foreach (var res in results)
                {
                    Logger.Log($"编码器测试 {res.name}: {(res.available ? "[OK]" : "[FAIL]")} {res.note}");
                }
                Logger.Log("======== 编码器可用性测试结束 ========");
                return results.ToList();
            }
            finally
            {
                if (File.Exists(testInput)) File.Delete(testInput);
                // 仅当目录为空时删除
                if (Directory.Exists(testDir) && !Directory.EnumerateFileSystemEntries(testDir).Any())
                    Directory.Delete(testDir);
            }
        }

        // 抽取单个编码器测试逻辑
        private static async Task<(string name, bool available, string note)> TestSingleEncoderAsync(
            string enc, string testInput, string testDir)
        {
            bool ok = false;
            string note = "不可用";
            try
            {
                string outFile = Path.Combine(testDir, $"test_{enc}.avif");
                string qpParam = enc switch
                {
                    var e when e.StartsWith("av1_nvenc") => "-qp 30",
                    var e when e.StartsWith("av1_qsv") => "-global_quality 30",
                    var e when e.StartsWith("av1_amf") => "-qp 30",
                    var e when e.StartsWith("av1_vulkan") => "-qp 30",
                    var e when e.StartsWith("av1_vaapi") => "-global_quality 30",
                    _ => "-crf 30"
                };

                string args = $"-y -loglevel error -i \"{testInput}\" -c:v {enc} -pix_fmt yuv420p {qpParam} -frames:v 1 \"{outFile}\"";

                var psi = new ProcessStartInfo("ffmpeg", args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                using var p = Process.Start(psi);
                if (p == null) return (enc, false, "无法启动 ffmpeg");

                string stderr = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();

                if (p.ExitCode == 0 && File.Exists(outFile) && new FileInfo(outFile).Length > 100)
                {
                    ok = true;
                    note = "可用";
                }
                else
                {
                    note = ParseError(stderr);
                }

                if (File.Exists(outFile)) File.Delete(outFile);
            }
            catch (Exception ex)
            {
                note = $"异常: {ex.Message}";
            }
            return (enc, ok, note);
        }

        private static string ParseError(string stderr)
        {
            if (stderr.Contains("MFX session")) return "缺少 Intel 驱动";
            if (stderr.Contains("MFT")) return "缺少 Media Foundation 编码器";
            if (stderr.Contains("Impossible to convert")) return "格式转换失败";
            if (stderr.Contains("Function not implemented")) return "功能未实现";
            if (stderr.Contains("Invalid argument")) return "参数无效";
            if (stderr.Contains("Unknown error")) return "未知错误";
            return "不可用";
        }




        /// <summary> 生成一个 64x64 纯红色 BMP 文件字节数组（完全内存构建，不依赖 ffmpeg） </summary>
        private static byte[] CreateTestBmp()
        {
            // 使用 256x256 纯红色 BMP，满足所有硬件编码器的最低分辨率要求
            int width = 256, height = 256;
            int rowSize = ((width * 3 + 3) / 4) * 4;
            int pixelDataSize = rowSize * height;
            int fileSize = 54 + pixelDataSize;

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            // 位图文件头
            bw.Write((ushort)0x4D42);
            bw.Write(fileSize);
            bw.Write(0);
            bw.Write(54);

            // 位图信息头
            bw.Write(40);
            bw.Write(width);
            bw.Write(height);
            bw.Write((ushort)1);
            bw.Write((ushort)24);
            bw.Write(0);
            bw.Write(pixelDataSize);
            bw.Write(2835);
            bw.Write(2835);
            bw.Write(0);
            bw.Write(0);

            // 像素数据 (BGR)
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bw.Write((byte)0x00); // 蓝
                    bw.Write((byte)0x00); // 绿
                    bw.Write((byte)0xFF); // 红
                }
                for (int p = width * 3; p < rowSize; p++)
                    bw.Write((byte)0);
            }

            bw.Flush();
            return ms.ToArray();
        }


          

    }
}