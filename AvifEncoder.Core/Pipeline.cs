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

        // 在 PresetConfig 类中，将 AomParams 属性更新为：
        public string AomParams { get; set; } =
            "aq-mode=3:deltaq-mode=0:enable-chroma-deltaq=1:sharpness=0:" +
            "enable-qm=1:enable-restoration=1:enable-cdef=1:" +
            "enable-global-motion=1:enable-warped-motion=1:" +
            "enable-obmc=1:enable-ref-frame-mvs=1:" +
            "enable-tx64=1:enable-dist-wtd-comp=1:" +
            "enable-rect-tx=1:enable-1to4-partitions=1:" +
            "enable-ab-partitions=1:enable-rect-partitions=1";
        public bool Lossless { get; set; } = false;
        public int BitDepth { get; set; } = 8;
        public bool AutoSource { get; set; } = true;
        public bool UserSetChroma { get; set; } = false;
        public bool UserSetBitDepth { get; set; } = false;
        public string OutputNameFormat { get; set; } = "covers-{index}.avif";

        /// <summary> 输出文件冲突时的处理策略 </summary>
        public enum ConflictStrategy
        {
            Rename,    // 自动追加 _1, _2 … 后缀（默认）
            Overwrite, // 直接覆盖已存在的文件
            Skip       // 存在时跳过该文件
        }
        /// <summary> 当前冲突处理策略 </summary>
        public ConflictStrategy FileConflictStrategy { get; set; } = ConflictStrategy.Rename;

        // 自定义 CRF 搜索范围
        public int MinCRF { get; set; } = 0;
        public int MaxCRF { get; set; } = 63;

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
        public int MaxResolution { get; set; } = 0;

        // 是否将缩放应用于最终输出
        public bool ApplyScalingToOutput { get; set; } = true;

        // 是否递归遍历输入目录的子文件夹
        public bool RecurseSubdirectories { get; set; } = false;

        // 在 PresetConfig 类中添加
        public bool SerialEncode { get; set; } = false;

        // 在 PresetConfig 类中添加
        public bool UseProxySearch { get; set; } = false;   // 默认关闭

        /// <summary> 是否启用先验引导搜索（中位数初始化 + 动态哨兵探测） </summary>
        public bool UsePriorSearch { get; set; } = false;

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
        private static ILogger? _instance;

        /// <summary>初始化默认文件日志器（控制台/批处理场景）</summary>
        public static void Init(string outputDir)
        {
            _instance = new FileLogger(outputDir);
        }

        /// <summary>注入自定义日志器（如 GuiLogger）</summary>
        public static void SetInstance(ILogger logger)
        {
            _instance = logger;
        }

        // 静态方法全部委托给 ILogger 实例
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


        private const int HardFailThreshold = 2;
        private const int AvoidRadius = 2;


        // 记录某文件的某像素格式是否已发生“完全无法写入”的致命错误，用于跳过后续尝试
        private readonly ConcurrentDictionary<string, HashSet<string>> _fatalFmts = new();



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
            // 获取相对于输入根目录的相对路径
            string relPath = Path.GetRelativePath(_inputDir, inputFilePath);
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
            int ssimSlots = Math.Max(2, cpuCount);
            int ffmpegPoolSize = isHardwareEncoder
                ? Math.Max(2, cpuCount * 2)
                : Math.Max(2, cpuCount / 2);

            if (isHardwareEncoder && !config.UserSpecifiedMaxJobs)
            {
                config.MaxJobs = Math.Max(config.MaxJobs, ffmpegPoolSize);
            }

            _maxFfmpegConcurrency = config.MaxJobs;
            _ssimConcurrency = new SemaphoreSlim(ssimSlots);
            _ffmpegSlots = new SemaphoreSlim(ffmpegPoolSize);
            _guiProgress = progress;       // ★ 改为 _guiProgress

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

            // 在工作目录下建临时子目录，避免路径中的盘符/冒号干扰
            string workDir = Environment.CurrentDirectory;
            string metricsDir = Path.Combine(workDir, "avif_metrics_tmp");
            Directory.CreateDirectory(metricsDir);

            string jsonName = $"_metrics_{Guid.NewGuid():N}.json";
            string jsonPath = Path.Combine(metricsDir, jsonName);
            // 只传文件名，彻底解决冒号/盘符问题
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
                    // ★ 使用 vmaf_float_v0.6.1 浮点模型
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

                // 启动 ffmpeg，工作目录设为 metricsDir
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo(_ffmpegPath, args)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = metricsDir   // 关键：避免路径中的冒号
                    }
                };

                process.Start();
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

                // 从 stderr 提取 VMAF 分数（部分版本写入 stderr）
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
                try
                {
                    if (File.Exists(jsonPath)) File.Delete(jsonPath);
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
            SafeWriteLine($"输入文件夹: {_inputDir}");
            SafeWriteLine($"输出文件夹: {_outputDir}");

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
                var searchResult = await RunCRFSearchAsync(workingInputPath, config, encInfo, name);
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
            string rowMtArg = GetRowMtArg(config);
            string cacheKey = GetSsimCacheKey(normalizedInput, encodeResult.Crf, cleanPixFmt, tileCols, cpuUsed, jpeg, aomParams, actualDepth, keyW, keyH, rowMtArg);

            // 缓存命中
            if (_cache.TryGetMetrics(cacheKey, out QualityMetrics? cachedMetrics))
            {
                _logger.LogSearch($"最终指标复用缓存: CRF={encodeResult.Crf} VMAF={cachedMetrics!.VMAF:F4}");
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
                _logger.LogSearch($"最终多指标 CRF={encodeResult.Crf}: SSIM={metrics.SSIM:F6}, PSNR-Y={metrics.PSNR_Y:F4}dB, MS-SSIM={metrics.MS_SSIM:F4}, VMAF={metrics.VMAF:F4}");
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
                SearchEvaluations = searchResult.SearchEvalCount
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
            // ★ 修改：不再区分源格式，只要启用无损选项，所有图片都使用真正的无损编码
            bool isTrulyLossless = isLosslessMode;

            string srcFmt = await GetSourcePixelFormat(inputPath);
            bool hasAlpha = await SourceHasAlpha(inputPath);
            string actualPixFmt = await GetPixelFormatForFileAsync(inputPath, isLosslessMode, isTrulyLossless, hasAlpha);

            // ... 省略中间 ...

            // ★ 移除原有的有损源数学无损警告（现在所有图片都是真无损）

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
                string targetDisplay = GetTargetDisplayString(config.TargetSSIM, metricModeLabel);
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
                            SafeWriteLine($" [RETRY] [{name}] 普通搜索失败，开始安全模式全扫描 (yuv420p, cpu‑used 0)...");
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
        /// <summary>
        /// 构造安全模式（yuv420p + 单 tile + 全色域）的 ffmpeg 参数字符串。
        /// 仅对 libaom‑av1 启用 tile 与 row‑mt 参数，其他编码器忽略，避免无效参数造成失败。
        /// 若启用了 DisableTileParallel，则强制 tile=0 且关闭 row‑mt。
        /// </summary>
        /// <summary>
        /// 构造安全模式（yuv420p + 单 tile + 全色域）的 ffmpeg 参数字符串。
        /// 若启用了 SerialEncode，则强制 tile=0 且关闭 row‑mt。
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
        private string GetEncodeCacheKey(
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
        /// <summary> 构建参数集尝试列表（已优化降级顺序，优先保留 AOM 参数） </summary>
        private List<(string aomParams, string tilePart, int actualCpu, string rowMt)> BuildParamSets(
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
                    int safeTileCols = (imageWidth > 0 && imageWidth >= 256)
                                       ? Math.Clamp(Math.Max(2, minLegal), minLegal, maxLegal)
                                       : 0;
                    if (minLegal > maxLegal) safeTileCols = 0;
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
                        _logger.LogSearch($"✅ 编码成功: {input} CRF={crf} 耗时={sw.Elapsed.TotalSeconds:F4}s");
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
            string logLevel = "-loglevel info -hide_banner";
            string aom = string.IsNullOrEmpty(param.aomParams) ? "" : $"-aom-params {param.aomParams}";
            string crfPart = isTrueLossless ? "-lossless 1" : $"-crf {crf}";
            string range = "-color_range pc";
            string colorMeta = "-color_primaries bt709 -color_trc iec61966-2-1 -colorspace bt709";
            string stillPic = EncoderSupportsStillPicture(cfg.Encoder) ? "-still-picture 1" : "";
            string encoderSpecific = BuildEncoderSpecificArgs(cfg, param.actualCpu, param.tilePart, param.rowMt);

            // ---------- 单线程全局控制 ----------
            string threadsArg = cfg.SerialEncode ? "-threads 1" : "";   // 新属性名
                                                                        // ---------------------------------

            return $"{logLevel} -i \"{input}\" " +
                   $"-c:v {cfg.Encoder} -pix_fmt {pixFmt} {range} {colorMeta} " +
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
    int width = 0, int height = 0,
    string rowMt = "")                         // ← 新增
        {
            string res = (width > 0 && height > 0) ? $"|res={width}x{height}" : "";
            return $"{normalizedPath}|crf={crf}|pix={pixFmt}|tile={tileCols}|cpu={cpuUsed}" +
                   $"|jpeg={isJpeg}|aom={effectiveAomParams}|depth={bitDepth}{res}|rowmt={rowMt}";
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
            string rowMtArg = GetRowMtArg(cfg);
            string key = GetSsimCacheKey(normalizedInput, crf, pixFmt, tileCols, cpuUsed, jpeg, effectiveAom, actualDepth, metricsW, metricsH, rowMtArg);

            if (_cache.TryGetMetrics(key, out QualityMetrics? cached))
            {
                _logger.LogSearch($"指标缓存命中: CRF={crf} [{Path.GetFileName(input)}] VMAF={cached!.VMAF:F4}");
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
                                             $"SSIM={metrics.SSIM:F4}, PSNR-Y={metrics.PSNR_Y:F4}dB, " +
                                             $"MS-SSIM={metrics.MS_SSIM:F4}, VMAF={metrics.VMAF:F4}");
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












        /// <summary>
        /// 使用极快的编码参数进行代理评估，返回 0‑1 分数（与 getScore 一致）。
        /// 失败返回 -1。
        /// </summary>
        /// <summary>
        /// 使用极快的编码参数进行代理评估，返回 0‑1 分数。
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
                BitDepth = cfg.BitDepth,
                SerialEncode = cfg.SerialEncode   // ← 新属性名（传递极限压缩设置）
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



        /// <summary>
        /// 数据驱动混合搜索：中位数初始化 + 保守 Proxy 验证 + 安全二分
        /// 始终在用户指定范围（或全范围）内搜索，保证全局最优。
        /// </summary>
        /// <summary>
        /// 混合搜索：默认仅中位数初始化 + 标准二分；可选开启保守 Proxy 验证。
        /// 不再执行 MaxCRF 早停，评估次数精确统计。
        /// </summary>
        /// <summary>
        /// 混合搜索：默认基于先验表直接划定搜索区间，使用标准二分，无需 Proxy。
        /// 若启用 --proxy，则保留保守 Proxy 验证（沿用原有 PerformConservativeProxyPhaseAsync）。
        /// </summary>
        /// <summary>
        /// 数据驱动混合搜索（默认模式）：
        /// 1. 根据先验表获取中位数 CRF，执行一次真实评估。
        /// 2. 若中位数达标 → 向右二分 [median, userMax]（已知下界，不重复测 median）。
        /// 3. 若中位数不达标 → 向左二分 [userMin, median-1]（验证下界 userMin）。
        /// 4. 若仍未找到可行解，回退到安全模式全扫描（兜底离群值）。
        /// 若启用 --proxy，则保留保守 Proxy 验证流程。
        /// </summary>
        /// <summary>
        /// 按目标 VMAF 动态返回最优哨兵偏移量（基于 400 张图片统计）
        /// </summary>
        private static int GetOptimalSentinelDelta(int targetVmafInt)
        {
            return targetVmafInt switch
            {
                90 => 4,                                   // 中位数 38 时最优
                >= 91 and <= 95 => 2,                     // 分布最集中，极小偏移最优
                96 => 3,                                   // 高离散度目标
                _ => 3                                     // 安全默认
            };
        }

        /// <summary>
        /// 混合搜索：先验中位数 + 动态哨兵探测 + 标准二分
        /// </summary>
        /// <summary>
        /// 混合搜索：先验中位数 + 动态哨兵探测 + 标准二分
        /// </summary>
        /// <summary>
        /// 混合搜索：先验中位数 + 动态哨兵探测 + 标准二分（可通过 --prior-search 启用）
        /// </summary>
        private async Task<(int crf, bool searchFailed, bool qualityInsufficient, int evalCount)> HybridSearchCRFAsync(
            string input, int tileCols, PresetConfig cfg, string pixFmt, bool jpeg, string? displayName = null)
        {
            string name = displayName ?? Path.GetFileName(input);
            double target = cfg.TargetSSIM + SSIMMargin;
            string metricMode = cfg.MetricMode ?? "vmaf";
            // ★ 使用 FormatScore 将 0-1 目标值转换为用户可读的原生格式
            string targetDisplay = FormatScore(target, metricMode);
            SafeWriteLine($"  [{name}] [SEARCH] 混合搜索开始 (目标={targetDisplay})");

            using var searchCts = new CancellationTokenSource(TimeSpan.FromMinutes(cfg.SearchTimeoutMinutes));
            var token = CancellationTokenSource.CreateLinkedTokenSource(searchCts.Token, _globalCts?.Token ?? default).Token;

            Func<int, Task<double>> getScore = BuildGetScoreFunc(input, tileCols, cfg, pixFmt, jpeg, name, token);
            int totalEvalCount = 0;
            int userMin = cfg.MinCRF;
            int userMax = cfg.MaxCRF;

            // 先验搜索未启用时，直接全范围二分
            if (!cfg.UsePriorSearch)
            {
                SafeWriteLine($"  [{name}] [INFO] 先验搜索已关闭，使用标准二分区间 [{userMin}, {userMax}]");
                var (directBestCrf, directEval) = await StandardBinarySearch(
                    input, tileCols, cfg, pixFmt, jpeg, name, target, getScore, token,
                    userMin, userMax, knownLoScore: null);
                totalEvalCount = directEval;
                if (directBestCrf >= 0)
                {
                    SafeWriteLine($"  [{name}] [DONE] 搜索完成，最优 CRF={directBestCrf}，总评估 {totalEvalCount} 次");
                    return (directBestCrf, false, false, totalEvalCount);
                }

                // 标准二分无解 → 回退安全扫描
                // 标准二分无解 → 若 MinCRF=0 则直接失败，否则回退安全扫描
                if (userMin == 0)
                {
                    SafeWriteLine($"  [{name}] [FAIL] 所有搜索均失败且 MinCRF=0，跳过安全扫描，将使用 CRF=0 最终编码");
                    return (cfg.BaseCRF, true, false, totalEvalCount);
                }

                SafeWriteLine($"  [{name}] [FALLBACK] 标准二分无解，启动安全模式全扫描 (范围=[{userMin},{userMax}])");
                var (safeOk, safeCrf, _, _) = await RunSafeModeScan(input, cfg, name, userMin, userMax);
                if (safeOk)
                {
                    SafeWriteLine($"  [{name}] [FALLBACK] 安全扫描成功，CRF={safeCrf}");
                    return (safeCrf, false, false, totalEvalCount);
                }

                SafeWriteLine($"  [{name}] [FAIL] 所有搜索均失败，回退到 BaseCRF={cfg.BaseCRF}");
                return (cfg.BaseCRF, true, false, totalEvalCount);
            }

            // ========== 以下为启用先验搜索时的逻辑 ==========
            // 统一计算先验中位数（VMAF 模式使用统计值，否则取中点）
            int priorMedian = (userMin + userMax) / 2;
            if (metricMode == "vmaf")
            {
                double targetVmaf = target * 100.0;
                var (median, _, _) = GetPriorFromVmaf(targetVmaf);
                priorMedian = Math.Clamp(median, userMin, userMax);
            }

            int searchLo, searchHi;
            double? knownLoScore = null;

            // ===== Proxy 模式（保持原逻辑，不使用哨兵） =====
            if (cfg.UseProxySearch)
            {
                SafeWriteLine($"  [{name}] [PRIOR] 先验中位数={priorMedian}");

                var (safeLo, safeHi) = await PerformConservativeProxyPhaseAsync(
                    input, tileCols, cfg, pixFmt, jpeg, name, target, metricMode, token,
                    priorMedian, userMin, userMax);

                searchLo = (safeLo >= 0 && safeHi >= safeLo) ? Math.Max(userMin, safeLo) : userMin;
                searchHi = (safeLo >= 0 && safeHi >= safeLo) ? Math.Min(userMax, safeHi) : userMax;

                SafeWriteLine($"  [{name}] [INFO] 二分区间: [{searchLo}, {searchHi}]");

                var (proxyCrf, proxyEval) = await StandardBinarySearch(
                    input, tileCols, cfg, pixFmt, jpeg, name, target, getScore, token,
                    searchLo, searchHi, knownLoScore: null);
                totalEvalCount += proxyEval;

                if (proxyCrf >= 0)
                {
                    SafeWriteLine($"  [{name}] [DONE] 搜索完成，最优 CRF={proxyCrf}，总评估 {totalEvalCount} 次");
                    return (proxyCrf, false, false, totalEvalCount);
                }

                // Proxy 失败 → 回退安全扫描
                // Proxy 失败 → 若 MinCRF=0 则直接失败，否则回退安全扫描
                if (userMin == 0)
                {
                    SafeWriteLine($"  [{name}] [FAIL] 所有搜索均失败且 MinCRF=0，跳过安全扫描，将使用 CRF=0 最终编码");
                    return (cfg.BaseCRF, true, false, totalEvalCount);
                }

                SafeWriteLine($"  [{name}] [FALLBACK] Proxy 区间无解，启动安全模式全扫描 (范围=[{userMin},{userMax}])");
                var (safeOk, safeCrf, _, _) = await RunSafeModeScan(input, cfg, name, userMin, userMax);
                if (safeOk)
                {
                    SafeWriteLine($"  [{name}] [FALLBACK] 安全扫描成功，CRF={safeCrf}");
                    return (safeCrf, false, false, totalEvalCount);
                }

                SafeWriteLine($"  [{name}] [FAIL] 所有搜索均失败，回退到 BaseCRF={cfg.BaseCRF}");
                return (cfg.BaseCRF, true, false, totalEvalCount);
            }

            // ========== 默认先验模式：中位数 + 哨兵 + 二分 ==========
            // 1. 评估中位数
            SafeWriteLine($"  [{name}] [PRIOR] 先验中位数 CRF={priorMedian} ...");
            double medianScore = await getScore(priorMedian);
            totalEvalCount++;
            string medianDisplay = metricMode == "vmaf" ? $"VMAF={medianScore * 100:F4}" : $"分数={medianScore:F4}";
            SafeWriteLine($"  [{name}] [PRIOR] CRF={priorMedian} → {medianDisplay}");

            // 2. 哨兵探测（仅 VMAF 模式启用）
            if (metricMode == "vmaf" && medianScore >= 0)
            {
                int delta = GetOptimalSentinelDelta((int)Math.Round(target * 100.0));
                if (delta > 0)
                {
                    if (medianScore >= target)   // 中位数达标
                    {
                        int probe = Math.Min(priorMedian + delta, userMax);
                        if (probe > priorMedian)
                        {
                            SafeWriteLine($"  [{name}] [SENTINEL] 哨兵探测 CRF={probe} ...");
                            double probeScore = await getScore(probe);
                            totalEvalCount++;
                            string probeDisplay = metricMode == "vmaf" ? $"VMAF={probeScore * 100:F4}" : $"分数={probeScore:F4}";
                            SafeWriteLine($"  [{name}] [SENTINEL] CRF={probe} → {probeDisplay}");

                            if (probeScore >= target)
                            {
                                searchLo = probe;
                                searchHi = userMax;
                                knownLoScore = probeScore;
                            }
                            else
                            {
                                searchLo = priorMedian;
                                searchHi = probe - 1;
                                knownLoScore = medianScore;
                            }
                        }
                        else
                        {
                            searchLo = priorMedian;
                            searchHi = userMax;
                            knownLoScore = medianScore;
                        }
                    }
                    else   // 中位数不达标
                    {
                        int probe = Math.Max(priorMedian - delta, userMin);
                        if (probe < priorMedian)
                        {
                            SafeWriteLine($"  [{name}] [SENTINEL] 哨兵探测 CRF={probe} ...");
                            double probeScore = await getScore(probe);
                            totalEvalCount++;
                            string probeDisplay = metricMode == "vmaf" ? $"VMAF={probeScore * 100:F4}" : $"分数={probeScore:F4}";
                            SafeWriteLine($"  [{name}] [SENTINEL] CRF={probe} → {probeDisplay}");

                            if (probeScore >= target)
                            {
                                searchLo = probe;
                                searchHi = priorMedian - 1;
                                knownLoScore = probeScore;
                            }
                            else
                            {
                                searchLo = userMin;
                                searchHi = probe - 1;
                                knownLoScore = null;
                            }
                        }
                        else
                        {
                            searchLo = userMin;
                            searchHi = priorMedian - 1;
                            knownLoScore = null;
                        }
                    }
                }
                else
                {
                    // delta = 0，退化为标准中位数 + 二分逻辑
                    searchLo = medianScore >= target ? priorMedian : userMin;
                    searchHi = medianScore >= target ? userMax : priorMedian - 1;
                    knownLoScore = medianScore >= target ? medianScore : (double?)null;
                }
            }
            else
            {
                // 非 VMAF 模式或无有效分数
                searchLo = medianScore >= target ? priorMedian : userMin;
                searchHi = medianScore >= target ? userMax : priorMedian - 1;
                knownLoScore = medianScore >= target ? medianScore : (double?)null;
            }

            // 3. 标准二分搜索
            SafeWriteLine($"  [{name}] [INFO] 二分区间: [{searchLo}, {searchHi}] {(knownLoScore.HasValue ? "(下界已知可行)" : "(需验证下界)")}");
            if (knownLoScore.HasValue)
                SafeWriteLine($"  [{name}] [CORE] 下界已知可行 CRF={searchLo} ({FormatScore(knownLoScore.Value, metricMode)})");

            var (bestCrf, binEval) = await StandardBinarySearch(
                input, tileCols, cfg, pixFmt, jpeg, name, target, getScore, token,
                searchLo, searchHi, knownLoScore: knownLoScore);
            totalEvalCount += binEval;

            if (bestCrf >= 0)
            {
                SafeWriteLine($"  [{name}] [DONE] 搜索完成，最优 CRF={bestCrf}，总评估 {totalEvalCount} 次");
                return (bestCrf, false, false, totalEvalCount);
            }

            // 二分未找到可行解 → 回退安全扫描
            // 二分未找到可行解 → 若 MinCRF=0 则直接失败，否则回退安全扫描
            if (userMin == 0)
            {
                SafeWriteLine($"  [{name}] [FAIL] 所有搜索均失败且 MinCRF=0，跳过安全扫描，将使用 CRF=0 最终编码");
                return (cfg.BaseCRF, true, false, totalEvalCount);
            }

            SafeWriteLine($"  [{name}] [FALLBACK] 二分未找到可行解，启动安全模式全扫描 (范围=[{userMin},{userMax}])");
            var (safeOk2, safeCrf2, _, _) = await RunSafeModeScan(input, cfg, name, userMin, userMax);
            if (safeOk2)
            {
                SafeWriteLine($"  [{name}] [FALLBACK] 安全扫描成功，CRF={safeCrf2}");
                return (safeCrf2, false, false, totalEvalCount);
            }

            SafeWriteLine($"  [{name}] [FAIL] 所有搜索均失败，回退到 BaseCRF={cfg.BaseCRF}");
            return (cfg.BaseCRF, true, false, totalEvalCount);
        }

        // 辅助格式化方法（可放在同一类中）
        private static string FormatScore(double score, string metricMode)
        {
            return metricMode == "vmaf" ? $"VMAF={score * 100:F4}" : $"分数={score:F4}";
        }





        /// <summary>
        /// 在 [lo, hi] 区间内执行标准右边界二分，并附带右侧单调扫描。
        /// 返回 (最优CRF, 本阶段真实评估次数)。若下界不可行或全部失败返回 (-1, 评估次数)。
        /// 每一步均通过控制台和日志输出。
        /// </summary>
        /// <summary>
        /// 在 [lo, hi] 区间内使用标准右边界二分查找满足目标的最大 CRF。
        /// 区间内的每一个测试点都通过 getScore 获取真实分数，评估次数精确统计。
        /// 若没有任何点达标，返回 (-1, 评估次数)。
        /// 每一步均输出到控制台和日志。
        /// </summary>
        /// <summary>
        /// 标准右边界二分：在 [lo, hi] 区间内找到满足目标的最大 CRF。
        /// 若提供 knownLoScore（且 >= target），则跳过 lo 的评估，直接从 lo+1 开始搜索。
        /// 每一步均输出到控制台与日志。
        /// 返回 (最优CRF, 本阶段评估次数)。若无任何可行点，返回 (-1, evalCount)。
        /// </summary>
        private async Task<(int bestCrf, int evalCount)> StandardBinarySearch(
    string input, int tileCols, PresetConfig cfg, string pixFmt, bool jpeg,
    string name, double target, Func<int, Task<double>> getScore,
    CancellationToken token, int lo, int hi, double? knownLoScore = null)
        {
            int evalCount = 0;
            int bestCrf = -1;

            // 已知下界可行：直接记录，不评估
            if (knownLoScore.HasValue && knownLoScore.Value >= target)
            {
                bestCrf = lo;
                string loDisplay = cfg.MetricMode == "vmaf" ? $"VMAF={knownLoScore.Value * 100:F4}" : $"分数={knownLoScore.Value:F4}";
                SafeWriteLine($"  [{name}] [CORE] 下界已知可行 CRF={lo} ({loDisplay})");
            }

            // 确定搜索起点：若已知 lo 可行，则从 lo+1 开始；否则从 lo 开始
            int l = bestCrf >= 0 ? bestCrf + 1 : lo;
            int r = hi;

            while (l <= r)
            {
                token.ThrowIfCancellationRequested();
                int mid = (l + r) / 2;
                SafeWriteLine($"  [{name}] [BIN] 测试 CRF={mid} (区间 {l}-{r})...");
                double score = await getScore(mid);
                evalCount++;
                string midDisplay = cfg.MetricMode == "vmaf" ? $"VMAF={score * 100:F4}" : $"分数={score:F4}";
                SafeWriteLine($"  [{name}] [BIN] CRF={mid} → {midDisplay}");

                if (score >= target)
                {
                    bestCrf = mid;
                    l = mid + 1;
                }
                else
                {
                    r = mid - 1;
                }
            }

            if (bestCrf >= 0)
                SafeWriteLine($"  [{name}] [CORE] 二分结束，最优 CRF={bestCrf}，本阶段评估 {evalCount} 次");
            else
                SafeWriteLine($"  [{name}] [CORE] 二分结束，区间内无可行点，评估 {evalCount} 次");

            return (bestCrf, evalCount);
        }



        /// <summary>
        /// 保守 Proxy 阶段：评估中位数附近 3 个点（median-2, median, median+2），
        /// 仅当分数 > target + 0.02 时才视为“明确通过”。
        /// 返回 (safeLo, safeHi) 均钳制在 [globalMin, globalMax] 内。
        /// 若 Proxy 全部失败或无法判断，返回 (-1, -1)。
        /// </summary>
        private async Task<(int safeLo, int safeHi)> PerformConservativeProxyPhaseAsync(
            string input, int tileCols, PresetConfig cfg, string pixFmt, bool jpeg,
            string name, double target, string metricMode, CancellationToken token,
            int priorMedian, int globalMin, int globalMax)
        {
            int median = Math.Clamp(priorMedian, globalMin, globalMax);
            var testCrfs = new[] { median - 2, median, median + 2 }
                .Where(c => c >= globalMin && c <= globalMax)
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            if (testCrfs.Count == 0)
                return (globalMin, globalMax);

            bool anyPass = false;
            int lastPass = -1;
            double passMargin = 0.02;  // 保守余量

            foreach (int crf in testCrfs)
            {
                token.ThrowIfCancellationRequested();
                SafeWriteLine($"  [{name}] [PROXY] 快速验证 CRF={crf} ...");
                double proxyScore = await ProxyEvaluateAsync(input, crf, tileCols, cfg, jpeg, pixFmt);
                if (proxyScore < 0)
                {
                    SafeWriteLine($"  [{name}] [PROXY] CRF={crf} 评估失败，跳过");
                    continue;
                }

                bool pass = proxyScore >= target + passMargin;
                string status = pass ? "明确通过" : "保守失败";
                string display = metricMode == "vmaf" ? $"VMAF={proxyScore * 100:F4}" : $"分数={proxyScore:F4}";
                SafeWriteLine($"  [{name}] [PROXY] CRF={crf} → {display} ({status})");

                if (pass)
                {
                    anyPass = true;
                    if (crf > lastPass) lastPass = crf;
                }
            }

            if (anyPass)
            {
                // 至少一个明确通过 → 下界设为最后一个通过点，上界向右扩展 6 个 CRF
                int safeLo = lastPass;
                int safeHi = Math.Min(globalMax, lastPass + 6);
                return (safeLo, safeHi);
            }
            else
            {
                // 全部未明确通过 → 最优解可能在左侧，向左扩展 6
                int safeLo = Math.Max(globalMin, median - 6);
                int safeHi = median - 1;
                if (safeHi < safeLo) safeHi = safeLo;
                return (safeLo, safeHi);
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

            string vmaf = r.FinalVMAF?.ToString("F4", CultureInfo.InvariantCulture) ?? "";
            string psnrY = r.FinalPSNR_Y?.ToString("F4", CultureInfo.InvariantCulture) ?? "";
            string msssim = r.FinalMSSSIM?.ToString("F6", CultureInfo.InvariantCulture) ?? "";
            string mix = r.FinalMixScore?.ToString("F6", CultureInfo.InvariantCulture) ?? "";

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
            _ => $"{t.TotalSeconds:F4}s"
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
            string line = $"[{done}/{total} {pct,5:F4}%]";

            if (r != null)
            {
                if (r.Skipped)
                    return $"{line} [SKIP] 跳过 {r.FileName} | {r.OriginalFileName}";
                if (r.Success)
                {
                    string qualityStr = $"VMAF={r.FinalVMAF?.ToString("F4") ?? "N/A"}  PSNR-Y={r.FinalPSNR_Y?.ToString("F4") ?? "N/A"}dB  SSIM={r.FinalSSIM:F4}  MS-SSIM={r.FinalMSSSIM?.ToString("F4") ?? "N/A"}";
                    return $"{line} [OK] {r.FileName} | {r.OriginalFileName} | CRF:{r.UsedCRF} | " +
                           $"{FormatSizeLocal(r.OriginalSize)} -> {FormatSizeLocal(r.OutputSize)} | " +
                           $"{r.CompressionRatio:P1} | {qualityStr} | 总耗时:{r.TotalTime.TotalSeconds:F4}s | 剩余 {eta}";
                }
                return $"{line} [FAIL] 失败 | {r.OriginalFileName} | 原因:{r.ErrorMessage} | 总耗时:{r.TotalTime.TotalSeconds:F4}s | 剩余 {eta}";
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
            _ => $"{t.TotalSeconds:F4}s"
        };
    }


    /// <summary>编码器类型判断与通用工具方法</summary>
    public static class EncoderUtils
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



}