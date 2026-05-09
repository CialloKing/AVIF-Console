using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;   // 如果使用 System.Text.Json


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

        // 超时配置（分钟，-1 表示自动计算）
        // EncodeTimeoutMinutes：最终编码超时，-1 时根据分辨率自动计算（5～180分钟）
        // SearchTimeoutMinutes：二分搜索全局超时，默认 60 分钟
        // SafeTimeoutMinutes：安全模式全扫描超时，默认 180 分钟
        // SafeEncodeTimeoutMinutes：安全模式单次编码超时（扫描时使用，较短，默认 10 分钟）
        // SearchEncodeTimeoutMinutes：搜索过程中临时编码超时，默认 10 分钟
        // SsimTimeoutMinutes：SSIM 计算超时，默认 5 分钟
        // 编码回退时安全模式使用 timeoutMinutes * 2 作为超时（提供更宽松的时间）
        public int EncodeTimeoutMinutes { get; set; } = -1;
        public int SearchTimeoutMinutes { get; set; } = 60;
        public int SafeTimeoutMinutes { get; set; } = 180;
        public int SafeEncodeTimeoutMinutes { get; set; } = 10;
        public int SearchEncodeTimeoutMinutes { get; set; } = 10;
        public int SsimTimeoutMinutes { get; set; } = 5;

        // ★ 自定义编码器
        public string Encoder { get; set; } = "libaom-av1";

        public string MetricMode { get; set; } = "vmaf";

        /// <summary>
        /// 返回当前编码器实际有效的 AOM 参数字符串。
        /// 只有 libaom-av1 支持 aq-mode/deltaq-mode 等参数，其他编码器返回空字符串。
        /// </summary>
        public string GetEffectiveAomParams()
        {
            if (Encoder.StartsWith("libaom-av1", StringComparison.OrdinalIgnoreCase))
                return AomParams;
            return "";
        }



        /// <summary>
        /// 根据当前 MetricMode 自动调整 TargetSSIM 的默认上限，
        /// 确保在不同度量下搜索目标落在合理区间。
        /// 仅在用户未手动指定 -q 时调用。
        /// </summary>
        public void AdjustTargetForMetricMode()
        {
            // ★ 无损模式下不调整质量目标，避免日志混淆
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
        /// 例如：vmaf 模式下输入 95 将转换为 0.95；psnr 模式下输入 40 转换为 0.5。
        /// </summary>
        public void SetQualityTarget(double rawValue, string metricMode)
        {
            TargetSSIM = metricMode?.ToLower() switch
            {
                "ssim" => Math.Clamp(rawValue, 0, 1),
                "psnr" => Math.Clamp((rawValue - 30) / 20.0, 0, 1),   // 30 dB -> 0, 50 dB -> 1
                "msssim" => Math.Clamp(rawValue, 0, 1),
                "vmaf" => Math.Clamp(rawValue / 100.0, 0, 1),
                "mix" => Math.Clamp(rawValue, 0, 1),
                _ => Math.Clamp(rawValue, 0, 1)                   // fallback
            };
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
        private static readonly object _lock = new();
        private static string _logDir = "";

        public static void Init(string outputDir)
        {
            _logDir = Path.Combine(outputDir, "log");
            Directory.CreateDirectory(_logDir);

            try
            {
                var cutoff = DateTime.Now.AddDays(-30);
                foreach (var f in Directory.GetFiles(_logDir, "run_*.log"))
                    if (File.GetCreationTime(f) < cutoff)
                        File.Delete(f);
            }
            catch { }

            Log("===== NEW SESSION START =====");
            Log($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        public static void Log(string msg)
        {
            lock (_lock)
                File.AppendAllText(Path.Combine(_logDir, $"run_{DateTime.Now:yyyy-MM-dd}.log"),
                    $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        }

        public static void SSIM(string input, int crf, double ssim)
        {
            lock (_lock)
                File.AppendAllText(Path.Combine(_logDir, "ssim_trace.log"),
                    $"{input} | CRF={crf} | SSIM={ssim}\n");
        }

        public static void CRF(string msg)
        {
            lock (_lock)
                File.AppendAllText(Path.Combine(_logDir, "crf_search.log"),
                    $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
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

        private DateTime _startTime;
        private int _totalFiles;
        private int _processedCount;
        private int _successCount;
        private long _totalOriginalSize;
        private long _totalOutputSize;

        private readonly ConcurrentDictionary<string, double> _ssimCache = new();
        private readonly ConcurrentDictionary<string, Task<double>> _ssimTasks = new();
        // ★ 新增
        private readonly ConcurrentDictionary<string, QualityMetrics> _metricsCache = new();
        private readonly ConcurrentDictionary<string, Task<QualityMetrics?>> _metricsTasks = new();
        // 将原来的 (string file, TimeSpan encodeTime) 改为包含命令字符串
        private readonly ConcurrentDictionary<string, (string file, TimeSpan encodeTime, string commandLine)> _encodeCache = new();
        private readonly SemaphoreSlim _ssimConcurrency;
        private readonly SemaphoreSlim _ffmpegSlots;

        private static readonly object _consoleLock = new();
        private CancellationTokenSource? _globalCts;

        private readonly ConcurrentDictionary<string, (int w, int h)> _resolutionCache = new();

        private static void SafeWriteLine(string msg) { lock (_consoleLock) Console.WriteLine(msg); }

        private readonly ConcurrentDictionary<string, bool> _srcAlphaCache = new();

        private readonly int _maxFfmpegConcurrency;

        /// <summary> 判断编码器是否支持 -still-picture 1 参数（AVIF 单帧静止图像标志） </summary>
        private static bool EncoderSupportsStillPicture(string encoderName)
        {
            // 目前仅 libaom-av1 确定支持；svt-av1、rav1e 及硬件编码器均不支持
            return encoderName.StartsWith("libaom-av1", StringComparison.OrdinalIgnoreCase);
        }


        private static double ComputeMixScore(QualityMetrics m)
        {
            double vmafNorm = m.VMAF / 100.0;
            double psnrNorm = Math.Clamp((m.PSNR_Y - 30) / 20.0, 0, 1);
            return 0.80 * vmafNorm + 0.05 * m.SSIM + 0.10 * m.MS_SSIM + 0.05 * psnrNorm;
        }

        public void Dispose()
        {
            _globalCts?.Cancel();
            _globalCts?.Dispose();
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
    string json = await RunProcessAndGetOutputAsync(_ffprobePath, args);
    if (string.IsNullOrEmpty(json)) return null;

    try
    {
        using var doc = JsonDocument.Parse(json);
        var stream = doc.RootElement.GetProperty("streams")[0];
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

            if (enc.StartsWith("libaom-av1", StringComparison.OrdinalIgnoreCase))
            {
                return $"-cpu-used {cpuUsed} {tilePart} {rowMt}";
            }

            if (enc.StartsWith("libsvtav1", StringComparison.OrdinalIgnoreCase))
            {
                int svtPreset = SvtPresetFromCpuUsed(cpuUsed);
                // 基础：preset + tune + tile
                string baseArgs = $"-preset {svtPreset} -tune 0 {tilePart}";
                // 若非无损模式，附加全部极致参数
                if (!cfg.Lossless)
                    baseArgs += " -svtav1-params scd=0:aq-mode=2:enable-tpl-la=1:enable-mfmv=1:fast-decode=0";
                return baseArgs;
            }

            if (enc.StartsWith("librav1e", StringComparison.OrdinalIgnoreCase))
            {
                return $"-speed {cpuUsed} {tilePart}";
            }

            // 硬件编码器
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
                // Windows：统一小写，避免大小写变体
                // Linux/macOS：保留原始大小写，精确匹配
                return OperatingSystem.IsWindows() ? full.ToLowerInvariant() : full;
            }
            catch
            {
                // 极端异常时使用文件名小写作为回退键（应极少发生）
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

            // 生成唯一临时 JSON 文件
            string jsonPath = Path.Combine(_outputDir, $"_metrics_{Guid.NewGuid():N}.json")
                                      .Replace('\\', '/');

            try
            {
                var (w1, h1) = await GetResolutionAsync(refPath).WaitAsync(TimeSpan.FromSeconds(30));
                var (w2, h2) = await GetResolutionAsync(distPath).WaitAsync(TimeSpan.FromSeconds(30));
                string filter;
                if (w1 > 0 && h1 > 0 && w2 > 0 && h2 > 0 && (w1 != w2 || h1 != h2))
                {
                    int w = Math.Min(w1, w2);
                    int h = Math.Min(h1, h2);
                    // 注意：不再包含 name=vmaf
                    filter = $"[0:v]scale={w}:{h}[ref];[1:v]scale={w}:{h}[dist];[ref][dist]libvmaf=feature=name=psnr|name=float_ssim|name=float_ms_ssim:log_path={jsonPath}:log_fmt=json:n_threads=4";
                }
                else
                {
                    // 注意：不再包含 name=vmaf
                    filter = $"[0:v][1:v]libvmaf=feature=name=psnr|name=float_ssim|name=float_ms_ssim:log_path={jsonPath}:log_fmt=json:n_threads=4";
                }

                string args = $"-loglevel error -hide_banner -i \"{refPath}\" -i \"{distPath}\" " +
                              $"-filter_complex \"{filter}\" -frames:v 1 -f null -";

                var psi = new ProcessStartInfo(_ffmpegPath, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var p = new Process { StartInfo = psi };
                p.Start();
                var stdoutTask = p.StandardOutput.ReadToEndAsync();
                var stderrTask = p.StandardError.ReadToEndAsync();

                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(_config.SsimTimeoutMinutes));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    _globalCts?.Token ?? default, cts.Token);

                try { await Task.WhenAll(stdoutTask, stderrTask, p.WaitForExitAsync(linkedCts.Token)); }
                catch (OperationCanceledException)
                {
                    if (!p.HasExited) try { p.Kill(); } catch { }
                    Logger.Log($"ComputeAllMetrics 超时: {Path.GetFileName(refPath)}");
                    return null;
                }

                // 读取 stderr 完整内容（用于提取 VMAF 分数）
                string stderr = await stderrTask;
                if (!string.IsNullOrWhiteSpace(stderr))
                    Logger.Log($"ComputeAllMetrics stderr [{Path.GetFileName(refPath)}]: {stderr.Trim()}");

                if (p.ExitCode != 0)
                {
                    Logger.Log($"ComputeAllMetrics 失败 (exit {p.ExitCode}) [{Path.GetFileName(refPath)}]: {stderr.Trim()}");
                    return null;
                }

                // 读取 JSON
                string physicalPath = jsonPath.Replace('/', Path.DirectorySeparatorChar);
                if (!File.Exists(physicalPath))
                {
                    Logger.Log($"ComputeAllMetrics: JSON 文件未生成: {physicalPath}");
                    return null;
                }

                string json = await File.ReadAllTextAsync(physicalPath);
                QualityMetrics? metrics = ParseVmafJson(json);
                if (metrics == null) return null;

                // ★ 从 stderr 提取 VMAF 分数（新版 ffmpeg 不会将 vmaf 写入 JSON，而是打印到 stderr）
                var vmafMatch = Regex.Match(stderr, @"VMAF score:\s*([0-9.]+)");
                if (vmafMatch.Success && double.TryParse(vmafMatch.Groups[1].Value,
                        NumberStyles.Float, CultureInfo.InvariantCulture, out double vmafScore))
                {
                    metrics.VMAF = vmafScore;
                }
                else
                {
                    // 如果整个 stderr 都没找到，尝试备用模式（有些版本可能格式不同）
                    vmafMatch = Regex.Match(stderr, @"vmaf\s*=\s*([0-9.]+)");
                    if (vmafMatch.Success && double.TryParse(vmafMatch.Groups[1].Value,
                            NumberStyles.Float, CultureInfo.InvariantCulture, out vmafScore))
                    {
                        metrics.VMAF = vmafScore;
                    }
                    else
                    {
                        Logger.Log($"未从 stderr 提取到 VMAF 分数 [{Path.GetFileName(refPath)}]");
                    }
                }

                return metrics;
            }
            catch (Exception ex)
            {
                Logger.Log($"ComputeAllMetrics 异常: {ex.Message}");
                return null;
            }
            finally
            {
                // 清理临时 JSON 文件
                try
                {
                    string physicalPath = jsonPath.Replace('/', Path.DirectorySeparatorChar);
                    if (File.Exists(physicalPath)) File.Delete(physicalPath);
                }
                catch { }
            }
        }

        private static QualityMetrics? ParseVmafJson(string json)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var pooled = doc.RootElement.GetProperty("pooled_metrics");

                double ssim = pooled.GetProperty("float_ssim").GetProperty("mean").GetDouble();
                double ms_ssim = pooled.GetProperty("float_ms_ssim").GetProperty("mean").GetDouble();
                double vmaf = pooled.GetProperty("vmaf").GetProperty("mean").GetDouble();
                double psnr_y = pooled.GetProperty("psnr_y").GetProperty("mean").GetDouble();

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
                Logger.Log($"解析 VMAF JSON 失败: {ex.Message}");
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


        public AvifPipeline(string inputDir, string outputDir, PresetConfig config)
        {
            // 至少预留 2 个 SSIM 计算并发，避免搜索完全串行
            _inputDir = inputDir; _outputDir = outputDir; _config = config;
            _ffmpegPath = FindExecutable("ffmpeg") ?? throw new Exception("ffmpeg 未找到");
            _ffprobePath = FindExecutable("ffprobe") ?? throw new Exception("ffprobe 未找到");

            bool isHardwareEncoder = !config.Encoder.StartsWith("lib");

            int cpuCount = Environment.ProcessorCount;
            int ssimSlots = Math.Max(2, cpuCount);
            // 硬件编码器基于 GPU，可承受更高并发// SSIM 依旧主要占用 CPU// 软件编码器受限于 CPU
            // 根据编码器类型设置 ffmpeg 进程池大小
            // 根据编码器类型设置 ffmpeg 进程池大小
            int ffmpegPoolSize = isHardwareEncoder
                                 ? Math.Max(2, cpuCount * 2)   // GPU 通常能承受更高并发
                                 : Math.Max(2, cpuCount / 2);  // 软件编码器受限于 CPU

            // 若用户未手动指定 -t，则自动提升文件级并行度（保持 MaxJobs 与 ffmpegPoolSize 协调）
            // 若用户未手动指定 -t，则自动提升文件级并行度（保持 MaxJobs 与 ffmpegPoolSize 协调）
            // 若用户未通过 -t 手动指定，则同步调整文件级并发上限
            // （MaxJobs 的自动值 = sqrt(cpuCount)，手动指定时会覆盖自动值）
            if (isHardwareEncoder && config.MaxJobs <= Math.Max(2, (int)Math.Sqrt(cpuCount)))
            {
                config.MaxJobs = Math.Max(config.MaxJobs, ffmpegPoolSize);
            }

            // 实际可同时运行的 ffmpeg 进程数，受文件级并行限制
            // 记录最大并发数
            _maxFfmpegConcurrency = Math.Min(config.MaxJobs, ffmpegPoolSize);
            _ssimConcurrency = new SemaphoreSlim(ssimSlots);
            _ffmpegSlots = new SemaphoreSlim(ffmpegPoolSize);
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
                string output = await RunProcessAndGetOutputAsync(_ffprobePath, args);
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
            _startTime = DateTime.Now;
            Logger.Init(_outputDir);
            Logger.Log($"Pipeline started: CRF={_config.BaseCRF} SSIM={_config.TargetSSIM}");

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
            {
                // 搜索模式：同时显示基础 CRF 和搜索范围
                crfInfo = $"基础CRF: {_config.BaseCRF}, 搜索范围: {_config.MinCRF}-{_config.MaxCRF}";
            }
            else
            {
                // 非搜索模式：仅显示使用的 CRF
                crfInfo = $"CRF: {_config.BaseCRF}";
            }

            SafeWriteLine($"编码器: {_config.Encoder}");
            SafeWriteLine($"同时调用ffmpeg编码数: {_maxFfmpegConcurrency}");
            SafeWriteLine($"{crfInfo}  SSIM目标: {_config.TargetSSIM}  搜索: {_config.UseCRFSearch}  像素格式: {(_config.AutoSource ? "自适应" : (_config.PixelFormat ?? "动态"))}");
            SafeWriteLine($"文件名模板: {_config.OutputNameFormat}");
        }

        /// <summary> 扫描输入目录，返回按文件大小降序排列的文件列表 </summary>
        private async Task<List<(string path, int index)>?> ScanAndPrepareFilesAsync()
        {
            if (!Directory.Exists(_inputDir))
            {
                SafeWriteLine("输入文件夹不存在。");
                return null;
            }
            Directory.CreateDirectory(_outputDir);

            var extensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var sortedFiles = Directory.EnumerateFiles(_inputDir)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                .OrderBy(f => f, new NaturalComparer())
                .Select((path, idx) => (path, index: idx + 1))
                .ToList();

            if (sortedFiles.Count == 0)
            {
                SafeWriteLine("未找到图片。");
                return null;
            }

            _totalFiles = sortedFiles.Count;
            SafeWriteLine($"待处理: {_totalFiles} 张\n");

            var processingOrder = sortedFiles.OrderByDescending(t => new FileInfo(t.path).Length).ToList();
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

            var retryFiles = failures.Select(f => (
                filePath: Path.Combine(_inputDir, f!.OriginalFileName),
                index: f.Index
            )).ToList();

            foreach (var (filePath, index) in retryFiles)
            {
                string outFile = Path.Combine(_outputDir, GetOutputFileName(filePath, index));
                if (File.Exists(outFile))
                    try { File.Delete(outFile); } catch { }
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
            var totalTime = DateTime.Now - _startTime;
            // 过滤掉 null 值，得到非空结果列表
            var allResults = results.Where(r => r != null).Cast<EncodeResult>().ToList();
            int successCount = allResults.Count(r => !r.Skipped && r.Success);
            int failCount = allResults.Count(r => !r.Skipped && !r.Success);
            int skipCount = allResults.Count(r => r.Skipped);

            long totalOriginal = allResults.Where(r => !r.Skipped && r.Success).Sum(r => r.OriginalSize);
            long totalOutput = allResults.Where(r => !r.Skipped && r.Success).Sum(r => r.OutputSize);
            double overallRatio = totalOriginal == 0 ? 0 : 1.0 - (double)totalOutput / totalOriginal;

            SafeWriteLine("\n================ 转换完成 ================");
            SafeWriteLine($"总文件数: {_totalFiles}  成功: {successCount}  失败: {failCount}  跳过: {skipCount}");
            SafeWriteLine($"原始大小: {FormatSize(totalOriginal)}  输出大小: {FormatSize(totalOutput)}");
            SafeWriteLine($"整体压缩率: {overallRatio:P1}  总耗时: {FormatTimeSpan(totalTime)}");
            SafeWriteLine($"SSIM缓存项: {_ssimCache.Count}  编码缓存项: {_encodeCache.Count}");
            Logger.Log($"Finished. 成功: {successCount}, 失败: {failCount}, 跳过: {skipCount}, 耗时: {FormatTimeSpan(totalTime)}");

            ExportCsv(allResults);
        }

        /// <summary> 清理编码缓存及临时文件 </summary>
        private void FinalCleanup()
        {
            CleanDirectory(Path.Combine(_outputDir, "_enc_cache"));
            foreach (var f in Directory.GetFiles(_outputDir, "_p_*.avif"))
                try { File.Delete(f); } catch { }
        }

        // ========== 修复后的 PrintProgress（区分跳过） ==========
        private void PrintProgress(EncodeResult? r)
        {
            int done = Volatile.Read(ref _processedCount), total = _totalFiles;
            double pct = done * 100.0 / total;
            var elapsed = DateTime.Now - _startTime;
            string eta = "计算中...";
            if (done > 0 && done < total) eta = FormatTimeSpan(TimeSpan.FromSeconds(elapsed.TotalSeconds / done * (total - done)));
            else if (done == total) eta = "已完成";
            string line = $"[{done}/{total} {pct,5:F1}%]";

            if (r != null)
            {
                if (r.Skipped)
                {
                    SafeWriteLine($"{line} [SKIP] 跳过 {r.FileName} | {r.OriginalFileName}");
                }
                else if (r.Success)
                {
                    // 修改为（显示多个原生指标）：
                    string qualityStr = $"VMAF={r.FinalVMAF?.ToString("F1") ?? "N/A"}  PSNR-Y={r.FinalPSNR_Y?.ToString("F2") ?? "N/A"}dB  SSIM={r.FinalSSIM:F4}  MS-SSIM={r.FinalMSSSIM?.ToString("F4") ?? "N/A"}";
                    SafeWriteLine($"{line} [OK] {r.FileName} | {r.OriginalFileName} | CRF:{r.UsedCRF} | " +
                                  $"{FormatSize(r.OriginalSize)} -> {FormatSize(r.OutputSize)} | " +
                                  $"{r.CompressionRatio:P1} | {qualityStr} | 总耗时:{r.TotalTime.TotalSeconds:F1}s | 剩余 {eta}");
                }
                else
                {
                    SafeWriteLine($"{line} [FAIL] 失败 | {r.OriginalFileName} | 原因:{r.ErrorMessage} | 总耗时:{r.TotalTime.TotalSeconds:F1}s | 剩余 {eta}");
                }
            }
            else
            {
                SafeWriteLine($"{line} [SKIP] 跳过");
            }
        }

        private void CleanDirectory(string dir)
        {
            if (Directory.Exists(dir))
            {
                try { Directory.Delete(dir, true); Logger.Log($"缓存已清理: {dir}"); }
                catch (Exception ex) { Logger.Log($"清理失败: {dir} - {ex.Message}"); }
            }
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
            string raw = await RunProcessAndGetOutputAsync(_ffprobePath, args);
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
            string raw = await RunProcessAndGetOutputAsync(_ffprobePath,
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
                        Logger.Log($"任务信号量获取超时，跳过文件: {Path.GetFileName(file.filePath)}");
                        var failResult = new EncodeResult
                        {
                            Index = file.index,
                            FileName = GetOutputFileName(file.filePath, file.index),
                            OriginalFileName = Path.GetFileName(file.filePath),
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
                        Logger.Log($"文件处理异常: {file.filePath} - {ex.Message}");
                        var failResult = new EncodeResult
                        {
                            Index = file.index,
                            FileName = GetOutputFileName(file.filePath, file.index),
                            OriginalFileName = Path.GetFileName(file.filePath),
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
                    Logger.Log($"操作取消，跳过文件: {Path.GetFileName(file.filePath)}");
                    var cancelResult = new EncodeResult
                    {
                        Index = file.index,
                        FileName = GetOutputFileName(file.filePath, file.index),
                        OriginalFileName = Path.GetFileName(file.filePath),
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
        private async Task<EncodeResult?> ProcessSingleFileAsync(string inputPath, int index, PresetConfig config, bool isRetry)
        {
            string name = Path.GetFileName(inputPath);
            string outputFileName = GetOutputFileName(inputPath, index);
            string outputPath = Path.Combine(_outputDir, outputFileName);
            var fileStartTime = DateTime.Now;

            var skipResult = await TrySkipExistingOutputAsync(inputPath, index, config, isRetry);
            if (skipResult != null) return skipResult;

            Logger.Log($"开始: {name}");

            var encInfo = await PrepareEncodingInfoAsync(inputPath, config);
            if (encInfo == null)
            {
                var failResult = new EncodeResult
                {
                    Index = index,
                    FileName = outputFileName,
                    OriginalFileName = name,
                    UsedCRF = -1,
                    Success = false,
                    ErrorMessage = "无法获取分辨率",
                    TotalTime = DateTime.Now - fileStartTime,
                    PixelFormat = ""
                };
                MarkProcessed(failResult);
                return failResult;
            }

            SafeWriteLine($"[START] {name} [{encInfo.PixInfo}]");

            var searchResult = await RunCRFSearchAsync(inputPath, config, encInfo);
            var encodeResult = await PerformFinalEncodeAsync(inputPath, outputPath, config, encInfo, searchResult);

            double ssim = 0;
            // ★ 将 metrics 声明提前到 if 之外
            QualityMetrics? metrics = null;

            if (encodeResult.Success)
            {
                // ★ 调用多指标计算（同步等待）
                try
                {
                    metrics = await ComputeAllMetricsAsync(inputPath, outputPath);
                }
                catch (Exception ex)
                {
                    Logger.Log($"多指标计算异常 [{name}]: {ex.Message}");
                }

                if (metrics != null)
                {
                    // 使用 VMAF 提供的更准确的 SSIM
                    ssim = metrics.SSIM;
                    Logger.Log($"多指标 [{name}] CRF={encodeResult.Crf}: " +
                               $"SSIM={metrics.SSIM:F4}, PSNR-Y={metrics.PSNR_Y:F2}dB, " +
                               $"MS-SSIM={metrics.MS_SSIM:F4}, VMAF={metrics.VMAF:F2}");

                    // 存入 SSIM 缓存
                    string normalizedInput = GetNormalizedPathForCache(inputPath);
                    string cleanPixFmt = encodeResult.ActualPixFmt?.Replace("a", "") ?? "";
                    int actualDepth = encodeResult.ActualPixFmt?.Contains("10le") == true ? 10 : 8;
                    string aomParams = config.GetEffectiveAomParams();
                    bool jpeg = IsJpeg(inputPath);
                    int tileCols = encInfo.TileCols;
                    int cpuUsed = searchResult.UseSafeModeFinalEncode ? 0 : config.FinalCpuUsed;

                    string cacheKey = GetSsimCacheKey(normalizedInput, encodeResult.Crf, cleanPixFmt, tileCols, cpuUsed, jpeg, aomParams, actualDepth);
                    _ssimCache[cacheKey] = metrics.SSIM;
                }
                else
                {
                    // 多指标失败，回退到旧版 SSIM 计算
                    string normalizedInput = GetNormalizedPathForCache(inputPath);
                    string cleanPixFmt = encodeResult.ActualPixFmt?.Replace("a", "") ?? "";
                    int actualDepth = encodeResult.ActualPixFmt?.Contains("10le") == true ? 10 : 8;
                    string aomParams = config.GetEffectiveAomParams();
                    bool jpeg = IsJpeg(inputPath);
                    int tileCols = encInfo.TileCols;
                    int cpuUsed = searchResult.UseSafeModeFinalEncode ? 0 : config.FinalCpuUsed;

                    string cacheKey = GetSsimCacheKey(normalizedInput, encodeResult.Crf, cleanPixFmt, tileCols, cpuUsed, jpeg, aomParams, actualDepth);

                    if (_ssimCache.TryGetValue(cacheKey, out double cachedSsim) && cachedSsim >= 0)
                    {
                        ssim = cachedSsim;
                        Logger.Log($"最终 SSIM 复用缓存: {name} CRF={encodeResult.Crf} SSIM={cachedSsim:F4}");
                    }
                    else
                    {
                        ssim = await CalcSSIMAsync(inputPath, outputPath, encodeResult.ActualPixFmt);
                        if (ssim >= 0)
                            _ssimCache[cacheKey] = ssim;
                    }
                }
            }

            var result = new EncodeResult
            {
                Index = index,
                FileName = outputFileName,
                OriginalFileName = name,
                OriginalSize = encodeResult.Success ? new FileInfo(inputPath).Length : 0,
                OutputSize = encodeResult.Success ? new FileInfo(outputPath).Length : 0,
                UsedCRF = encodeResult.Success ? encodeResult.Crf : -1,
                FinalSSIM = ssim,
                EncodeTime = encodeResult.EncodeTime,
                SearchTime = searchResult.SearchTime,
                TotalTime = DateTime.Now - fileStartTime,
                Retries = encodeResult.Retries,
                Success = encodeResult.Success,
                ErrorMessage = encodeResult.FailReason,
                Skipped = false,
                PixelFormat = encodeResult.Success ? encodeResult.ActualPixFmt : "",
                SourcePixelFormat = encInfo.SourcePixFmt,
                Mode = config.AutoSource ? "自适应" : "手动",
                IsSafeMode = encodeResult.UseSafeMode,
                AomParamsUsed = encodeResult.ActualAom ?? "",
                CacheReused = encodeResult.FromCache,
                CommandLine = encodeResult.FinalCommand ?? "",

                // ★ 填充新指标
                FinalVMAF = metrics?.VMAF,
                FinalPSNR_Y = metrics?.PSNR_Y,
                FinalMSSSIM = metrics?.MS_SSIM,
                FinalMixScore = metrics == null ? null : ComputeMixScore(metrics)
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

            string outputFileName = GetOutputFileName(inputPath, index);
            string outputPath = Path.Combine(_outputDir, outputFileName);
            if (File.Exists(outputPath))
            {
                string name = Path.GetFileName(inputPath);
                SafeWriteLine($"[SKIP] {name} (已存在，跳过)");
                Logger.Log($"跳过: {name}");
                var skipResult = new EncodeResult
                {
                    Index = index,
                    FileName = outputFileName,
                    OriginalFileName = name,
                    OriginalSize = new FileInfo(inputPath).Length,
                    OutputSize = new FileInfo(outputPath).Length,
                    UsedCRF = -1,
                    FinalSSIM = -1,
                    EncodeTime = TimeSpan.Zero,
                    SearchTime = TimeSpan.Zero,
                    TotalTime = TimeSpan.Zero,
                    Retries = 0,
                    Success = true,
                    Skipped = true,
                    PixelFormat = ""
                };
                MarkProcessed(skipResult);
                return skipResult;
            }
            return null;
        }

        // 2. 准备编码基础信息
        private async Task<EncodingInfo?> PrepareEncodingInfoAsync(string inputPath, PresetConfig config)
        {
            string name = Path.GetFileName(inputPath);
            bool isLosslessMode = config.Lossless;
            bool isTrulyLossless = isLosslessMode && await IsTrulyLosslessSource(inputPath);

            // 优化：通过 GetSourcePixelFormat 获取源格式，同时解析 Alpha 信息
            string srcFmt = await GetSourcePixelFormat(inputPath);
            bool hasAlpha = await SourceHasAlpha(inputPath); // 可进一步优化缓存，当前保留
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

            // ★ 硬件编码器 Alpha 回退检测
            bool alphaDropped = false;
            if (hasAlpha && !config.Encoder.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
            {
                hasAlpha = false;
                alphaDropped = true;
                actualPixFmt = actualPixFmt.Replace("a", "");
                SafeWriteLine($" [WARN] [{name}] 硬件编码器不支持 Alpha 通道，透明度将被丢弃");
                Logger.Log($"Alpha 通道丢弃: {name}，编码器 {config.Encoder} 不支持 yuva 格式");
            }

            // ★ 新增：硬件编码器色度采样警告
            if (!config.Encoder.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
            {
                bool is420 = actualPixFmt.Contains("420");
                if (!is420)
                {
                    SafeWriteLine($" [WARN] [{name}] 硬件编码器 {config.Encoder} 通常只支持 4:2:0 色度采样，" +
                                  "程序将自动尝试降级（444→422→420）。若编码失败可能最终回退到 yuv420p。");
                    // 不直接修改 actualPixFmt，让后续降级逻辑正常运作
                }
            }

            int tileCols = isTrulyLossless ? 0 : Math.Clamp((int)Math.Log2(Environment.ProcessorCount), 1, 4);
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
                HasAlpha = hasAlpha  // 已修正为回退后的值
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
            string name = Path.GetFileName(inputPath);

            if (!encInfo.IsLosslessMode && config.UseCRFSearch)
            {
                SafeWriteLine($"  [SEARCH] [{name}] 开始 CRF 搜索 (目标 SSIM={config.TargetSSIM})，请耐心等待...");
                try
                {
                    var swSearch = Stopwatch.StartNew();
                    bool searchOk;
                    int finalCrf;
                    string usedPixFmt;

                    // 先尝试目标格式搜索
                    (searchOk, finalCrf, usedPixFmt) = await TrySearchWithFormatAttempts(
                        inputPath, config, encInfo, actualPixFmt, name);

                    // 安全模式全扫描
                    if (!searchOk)
                    {
                        SafeWriteLine($" [RETRY] [{name}] 普通搜索失败，开始安全模式全扫描 (yuv420p, cpu‑used 0)...");
                        (searchOk, finalCrf, usedPixFmt, useSafeModeFinalEncode) = await RunSafeModeScan(
                            inputPath, config, name);
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
                    Logger.Log($"搜索异常，回退直接编码: {name} - {ex.Message}");
                    SafeWriteLine($" [WARN] [{name}] CRF搜索异常，使用 BaseCRF ({crf}) 直接编码");
                }
            }

            return new CRFSearchResult
            {
                Crf = crf,
                ActualPixFmt = actualPixFmt,
                SearchTime = searchTime,
                SearchBasedCRF = searchBasedCRF,
                UseSafeModeFinalEncode = useSafeModeFinalEncode
            };
        }

        // ---------- 尝试目标格式列表搜索 ----------
        // ---------- 尝试目标格式列表搜索 ----------
private async Task<(bool ok, int crf, string pixFmt)>
    TrySearchWithFormatAttempts(string inputPath, PresetConfig config, EncodingInfo encInfo,
                                string actualPixFmt, string name)
{
    var attempts = BuildPixFmtAttempts(config, actualPixFmt, encInfo.HasAlpha);
    foreach (var fmt in attempts)
    {
        if (fmt != actualPixFmt && !config.AutoSource)
        {
            string desc = fmt.Contains("422") ? "422" :
                          (fmt.Contains("420") && !actualPixFmt.Contains("420") ? "420" : "");
            if (!string.IsNullOrEmpty(desc))
                SafeWriteLine($"  [RETRY] [{name}] 尝试 {desc} {fmt} ...");
        }

        (int crfResult, bool failed, bool qualityInsufficient) =
            await BinarySearchCRFAsync(inputPath, encInfo.TileCols, config, fmt, IsJpeg(inputPath));

        // 🔧 修复：先检查质量不足，避免误将极低 CRF 当作搜索结果
        if (qualityInsufficient)
        {
            // 所有尝试的 CRF 均达不到目标 SSIM，停止对该格式的搜索
            break;
        }

        if (!failed)
        {
            return (true, crfResult, fmt);
        }
        // 若 failed 为 true，继续尝试下一个像素格式
    }
    // 所有格式均失败或质量不足
    return (false, config.BaseCRF, actualPixFmt);
}

        // ---------- 安全模式全扫描 ----------
        private async Task<(bool ok, int crf, string pixFmt, bool safeMode)>
    RunSafeModeScan(string inputPath, PresetConfig config, string name)
        {
            using var safeCts = new CancellationTokenSource(TimeSpan.FromMinutes(config.SafeTimeoutMinutes));
            var safeToken = CancellationTokenSource.CreateLinkedTokenSource(
                safeCts.Token, _globalCts?.Token ?? default).Token;

            double target = config.TargetSSIM + SSIMMargin;
            int bestSafeCRF = -1;
            int totalSteps = config.MaxCRF - config.MinCRF + 1;
            int step = 0;
            int consecutiveFailures = 0;  // ★ 新增连续失败计数

            for (int testCrf = config.MaxCRF; testCrf >= config.MinCRF; testCrf--)
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

                // ★ 失败计数与上限检查
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
                    consecutiveFailures = 0;  // 有有效分数则重置计数
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
                    // 安全模式编码一次
                    string args = BuildSafeModeArgs(inputPath, tmpAvif, config, testCrf, aomPart);
                    (bool ok, string _) = await RunFfmpegExAsync(_ffmpegPath, args,
                        TimeSpan.FromMinutes(config.SafeEncodeTimeoutMinutes));
                    if (!ok || !File.Exists(tmpAvif) || new FileInfo(tmpAvif).Length < 100) return -1;

                    // ★ 直接使用已生成的临时文件计算多指标，不再二次编码
                    QualityMetrics? metrics = await ComputeAllMetricsAsync(inputPath, tmpAvif);
                    if (metrics != null)
                    {
                        double score = GetSearchScore(metrics, config.MetricMode ?? "ssim");
                        return score;
                    }

                    // 回退：多指标失败时用旧版 SSIM
                    Logger.Log($"安全模式 SSIM 回退到旧版 SSIMDirect: [{Path.GetFileName(inputPath)}] CRF={testCrf}");
                    return await SSIMDirect(inputPath, tmpAvif, "yuv420p");
                }
                finally
                {
                    if (File.Exists(tmpAvif)) try { File.Delete(tmpAvif); } catch { }
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
                : Math.Clamp((int)(((double)encInfo.Width * encInfo.Height) / (1920.0 * 1080.0) * 10), 5, 180); // ★ 浮点除法

            string effectiveAom = config.GetEffectiveAomParams();
            string aomPart = string.IsNullOrEmpty(effectiveAom) ? "" : $"-aom-params {effectiveAom}";
            string name = Path.GetFileName(inputPath);

            if (searchResult.UseSafeModeFinalEncode)
            {
                return await EncodeSafeMode(inputPath, outputPath, config, searchResult,
                                            timeoutMinutes, aomPart);
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
                                         int crf, string aomPart)
        {
            bool useStillPic = EncoderSupportsStillPicture(config.Encoder);
            string stillPic = useStillPic ? "-still-picture 1" : "";

            // 仅对 libaom-av1 传入单 tile 和 -row-mt
            string safeTile = config.Encoder.StartsWith("libaom-av1", StringComparison.OrdinalIgnoreCase)
                              ? "-tile-columns 0 -tile-rows 0"
                              : "";
            string safeRowMt = config.Encoder.StartsWith("libaom-av1", StringComparison.OrdinalIgnoreCase)
                               ? "-row-mt 1"
                               : "";

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

            // 3. 最终安全模式回退
            if (!success)
            {
                SafeWriteLine($" [WARN] [{name}] 常规/降级均失败，尝试最终安全模式（yuv420p）...");
                // ★ 使用统一的安全模式参数构建方法
                // 安全模式回退时，给予常规超时的 2 倍时间（因 yuv420p 解码压力较小，但编码器可能更慢，留出余量）
                string safeArgs = BuildSafeModeArgs(inputPath, outputPath, config, crf, aomPart);

                var swSafe = Stopwatch.StartNew();
                // 注释：提供更宽松的超时，避免因临时降级导致超时误判
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

                    // 动态描述
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
    CRFSearchResult searchResult, int timeoutMinutes, string aomPart)
        {
            var startTime = DateTime.Now;
            int crf = searchResult.Crf;

            // ★ 使用统一构建方法，内部自动处理 tile 与 still-picture
            // 安全模式最终编码使用基于分辨率的动态超时 timeoutMinutes，与大图耗时匹配
            string safeArgs = BuildSafeModeArgs(inputPath, outputPath, config, crf, aomPart);

            var swSafe = Stopwatch.StartNew();
            (bool success, string failReason) = await RunFfmpegExAsync(_ffmpegPath, safeArgs, TimeSpan.FromMinutes(timeoutMinutes));
            swSafe.Stop();

            // 动态生成安全模式描述
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
            Interlocked.Increment(ref _processedCount);
            if (r != null)
            {
                if (!r.Skipped)   // 只有非跳过文件才参与成功/失败统计
                {
                    if (r.Success)
                    {
                        Interlocked.Add(ref _totalOriginalSize, r.OriginalSize);
                        Interlocked.Add(ref _totalOutputSize, r.OutputSize);
                        Interlocked.Increment(ref _successCount);
                    }
                    // 失败的文件不累加 OriginalSize/OutputSize，也不增加 successCount
                }
            }
            PrintProgress(r);
        }


        /// <summary>
        /// 生成用于编码缓存的一致键，确保所有缓存访问使用相同格式。
        /// </summary>
        private string GetEncodeCacheKey(
            string normalizedPath, int crf, string pixFmt,
            string tilePart, int actualCpu, bool isTrueLossless,
            string aomParams, bool jpeg, int bitDepth)
        {
            return $"{normalizedPath}|crf={crf}|pix={pixFmt}" +
                   $"|tile={tilePart}|cpu={actualCpu}|lossless={isTrueLossless}" +
                   $"|aom={aomParams}|jpeg={jpeg}|depth={bitDepth}";
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
            string lastAttemptInfo = "";
            var formatAttempts = new List<string>();

            for (int fmtIdx = 0; fmtIdx < pixFmtsToTry.Length; fmtIdx++)
            {
                string currentPixFmt = pixFmtsToTry[fmtIdx];
                formatAttempts.Add(currentPixFmt);
                var paramSets = BuildParamSets(cfg, currentPixFmt, isTrueLossless, tileCols, cpuUsed, allowParamDegrade);

                foreach (var param in paramSets)
                {
                    lastAttemptInfo = $"pix={currentPixFmt}, aom={param.aomParams}, tile={param.tilePart}, cpu={param.actualCpu}";
                    var result = await TryEncodeWithParamSet(input, output, crf, currentPixFmt, param, cfg,
                                                              isTrueLossless, timeoutMinutes, fileName);
                    if (result.ok)
                        return result;
                    lastError = result.error ?? "未知错误";
                }

                if (fmtIdx < pixFmtsToTry.Length - 1)
                {
                    string nextFmt = pixFmtsToTry[fmtIdx + 1];
                    Logger.Log($"像素格式 {currentPixFmt} 编码失败，降级尝试 {nextFmt} ...");
                    SafeWriteLine($"  [DOWNGRADE] [{fileName}] 像素格式 {currentPixFmt} 失败，降级为 {nextFmt}");
                }
            }

            // ★ 将降级链、CRF 及最后尝试写入日志，便于离线诊断
            string chainDesc = string.Join(" → ", formatAttempts);
            Logger.Log($"编码失败 [CRF={crf}] [{fileName}] 尝试序列: {chainDesc}。最后尝试: {lastAttemptInfo}，错误: {lastError}");

            string enhancedError = $"编码失败 [尝试序列: {chainDesc}] 最后尝试: {lastAttemptInfo} → {lastError}";
            return (false, TimeSpan.Zero, _maxRetries, enhancedError, false, null, null);
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
        private List<(string aomParams, string tilePart, int actualCpu, string rowMt)> BuildParamSets(
            PresetConfig cfg, string currentPixFmt, bool isTrueLossless, int tileCols, int cpuUsed, bool allowParamDegrade)
        {
            string effectiveAom = cfg.GetEffectiveAomParams();
            var sets = new List<(string, string, int, string)>();

            bool isHighChroma = currentPixFmt.Contains("444") || currentPixFmt.Contains("422");
            // ★ 仅 libaom‑av1 支持 -row‑mt 参数，其他编码器传入空字符串
            string rowMt = cfg.Encoder.StartsWith("libaom-av1", StringComparison.OrdinalIgnoreCase)
                           ? "-row-mt 1"
                           : "";

            if (!isTrueLossless && isHighChroma)
            {
                // 常规参数集（使用有效 AOM 参数与计算出的 tile / cpu）
                sets.Add((effectiveAom, TilePart(tileCols, isTrueLossless), isTrueLossless ? 0 : cpuUsed, rowMt));

                if (allowParamDegrade)
                {
                    // 降级参数集：仅对已知支持 tile‑columns/rows 的编码器添加
                    bool encoderSupportsTileParams =
                        cfg.Encoder.StartsWith("libaom-av1", StringComparison.OrdinalIgnoreCase) ||
                        cfg.Encoder.StartsWith("libsvtav1", StringComparison.OrdinalIgnoreCase);

                    string downgradeTile = encoderSupportsTileParams ? "-tile-columns 0 -tile-rows 0" : "";
                    sets.Add(("", downgradeTile, 0, ""));
                }
            }
            else
            {
                sets.Add((effectiveAom, TilePart(tileCols, isTrueLossless), isTrueLossless ? 0 : cpuUsed, rowMt));
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
            string cacheKey = GetEncodeCacheKey(
                normalizedInput, crf, currentPixFmt,
                param.tilePart, param.actualCpu, isTrueLossless,
                param.aomParams, IsJpeg(input),
                currentPixFmt.Contains("10le") ? 10 : 8);

            string cacheFile = Path.Combine(_outputDir, "_enc_cache", $"{Sha256(cacheKey)}.avif");

            // 缓存命中
            if (_encodeCache.TryGetValue(cacheKey, out var cached) && File.Exists(cached.file))
            {
                // ★ 确保输出目录存在（防止极端情况被删除）
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.Copy(cached.file, output, true);
                Logger.Log($"复用编码缓存: {input} CRF={crf} pix={currentPixFmt} 原耗时={cached.encodeTime.TotalSeconds:F1}s");
                return (true, cached.encodeTime, 0, "", true, param.aomParams, cached.commandLine);
            }

            Logger.Log($"  ⏳ [{fileName}] 等待编码资源 (CRF={crf})...");
            bool slotTaken = false;
            try
            {
                if (!await _ffmpegSlots.WaitAsync(TimeSpan.FromSeconds(300), _globalCts?.Token ?? default))
                {
                    Logger.Log($"编码信号量获取超时: {input} CRF={crf}");
                    return (false, TimeSpan.Zero, 0, "编码信号量获取超时", false, null, null);
                }
                slotTaken = true;
                Logger.Log($"  ▶ [{fileName}] 开始编码 (CRF={crf}, pix={currentPixFmt})");

                for (int attempt = 0; attempt <= _maxRetries; attempt++)
                {
                    string ffArgs = BuildFfmpegArgs(input, output, crf, currentPixFmt, param, cfg, isTrueLossless);
                    Logger.Log($"编码: CRF={crf} {fileName} pix={currentPixFmt} 参数集={param.aomParams}");

                    var sw = Stopwatch.StartNew();
                    (bool success, string stderrLastLine) = await RunFfmpegExAsync(_ffmpegPath, ffArgs, TimeSpan.FromMinutes(timeoutMinutes));
                    sw.Stop();

                    if (success)
                    {
                        if (new FileInfo(output).Length < 100)
                        {
                            Logger.Log($"编码输出文件过小 ({new FileInfo(output).Length} 字节)，丢弃并重试");
                            if (File.Exists(output)) File.Delete(output);
                            if (attempt < _maxRetries) { await Task.Delay(1000); continue; }
                            return (false, TimeSpan.Zero, _maxRetries, "编码输出文件过小", false, null, null);
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
                        File.Copy(output, cacheFile, true);
                        _encodeCache[cacheKey] = (cacheFile, sw.Elapsed, ffArgs);
                        return (true, sw.Elapsed, attempt, "", false, param.aomParams, ffArgs);
                    }

                    string error = $"CRF={crf}, {stderrLastLine}";
                    Logger.Log($"❌ 编码失败: {input} - {error}");
                    if (File.Exists(output)) File.Delete(output);
                    if (attempt < _maxRetries) await Task.Delay(1000);
                }
                return (false, TimeSpan.Zero, _maxRetries, $"CRF={crf}, 重试耗尽", false, null, null);
            }
            catch (Exception ex)
            {
                Logger.Log($"编码异常: {input} - {ex.Message}");
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
            string aom = string.IsNullOrEmpty(param.aomParams) ? "" : $"-aom-params {param.aomParams}";
            string crfPart = isTrueLossless ? "-lossless 1" : $"-crf {crf}";
            string range = "-color_range pc";
            string colorMeta = "-color_primaries bt709 -color_trc iec61966-2-1 -colorspace bt709";
            string stillPic = EncoderSupportsStillPicture(cfg.Encoder) ? "-still-picture 1" : "";

            // ★ 编码器特定的速度、分块、以及相应的极致参数
            string encoderSpecific = BuildEncoderSpecificArgs(cfg, param.actualCpu, param.tilePart, param.rowMt);

            return $"-loglevel error -hide_banner -i \"{input}\" " +
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
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo(file, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            p.Start();
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_globalCts?.Token ?? default, timeoutCts.Token);
            try
            {
                await Task.WhenAll(stdoutTask, stderrTask, p.WaitForExitAsync(linkedCts.Token));
                string stderr = await stderrTask;
                string lastLine = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "";

                if (p.ExitCode != 0)
                {
                    Logger.Log($"ffmpeg 错误(退出码 {p.ExitCode}): {lastLine}");
                    return (false, lastLine);
                }
                return (true, "");
            }
            catch (OperationCanceledException)
            {
                if (!p.HasExited)
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                }
                // ★ 修复 Bug2：安全等待读取任务结束，避免未观察异常
                try
                {
                    await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch { }
                Logger.Log($"ffmpeg 超时/取消，进程是否已退出: {p.HasExited}");
                return (false, p.HasExited ? "超时(已退出)" : "超时(进程残留)");
            }
        }

        private static async Task<string> RunProcessAndGetOutputAsync(string file, string args)
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo(file, args)
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

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await Task.WhenAll(outTask, errTask);
                await p.WaitForExitAsync(cts.Token);
                return await outTask;
            }
            catch (OperationCanceledException)
            {
                if (!p.HasExited)
                {
                    try { p.Kill(); } catch { }
                    using var finalCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try { await p.WaitForExitAsync(finalCts.Token); }
                    catch (OperationCanceledException) { Logger.Log("等待 ffprobe 退出超时，已放弃"); }
                }
                Logger.Log($"ffprobe 调用超时: {args}");
                return string.Empty;
            }
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
                    Logger.Log($"SSIM 分辨率无效: a={Path.GetFileName(a)} ({w1}x{h1}), b={Path.GetFileName(b)} ({w2}x{h2})");
                    return -1;
                }

                string args = BuildSsimArgs(a, b, alignFmt, w1, h1, w2, h2);
                string output = await RunSsimProcess(args);
                return ParseSsimOutput(output);
            }
            catch (Exception ex)
            {
                Logger.Log($"SSIM 异常: {Path.GetFileName(a)} vs {Path.GetFileName(b)} - {ex.Message}");
                SafeWriteLine($" [FAIL] SSIM 异常: {ex.Message}");
                return -1;
            }
        }

        private bool EnsureFilesValid(string a, string b)
        {
            if (!File.Exists(a) || !File.Exists(b))
            {
                Logger.Log($"SSIM 文件缺失: a={Path.GetFileName(a)}, b={Path.GetFileName(b)}");
                return false;
            }

            long sizeA = new FileInfo(a).Length;
            long sizeB = new FileInfo(b).Length;
            if (sizeA < 100 || sizeB < 100)
            {
                Logger.Log($"SSIM 文件太小 ({sizeA} / {sizeB} 字节)");
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
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo(_ffmpegPath, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            p.Start();
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(_config.SsimTimeoutMinutes));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                _globalCts?.Token ?? default, timeoutCts.Token);

            try
            {
                await Task.WhenAll(stdoutTask, stderrTask, p.WaitForExitAsync(linkedCts.Token))
                          .WaitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                if (!p.HasExited) { try { p.Kill(); } catch { } }
                Logger.Log($"SSIM 超时");
                return string.Empty;
            }

            string stdout = await stdoutTask;
            string stderr = await stderrTask;
            return stdout + stderr;
        }

        private double ParseSsimOutput(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                LogSsimParseFailure("输出为空");
                return -1;
            }

            Logger.Log($"SSIM output:\n{output}");

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
            Logger.Log($"SSIM 解析失败: tail:\n{tail}");
        }



        /// <summary>
        /// 生成用于 SSIM 缓存的一致键，确保所有缓存访问使用相同格式。
        /// </summary>
        private static string GetSsimCacheKey(
            string normalizedPath, int crf, string pixFmt,
            int tileCols, int cpuUsed, bool isJpeg,
            string effectiveAomParams, int bitDepth)
        {
            return $"{normalizedPath}|crf={crf}|pix={pixFmt}|tile={tileCols}|cpu={cpuUsed}" +
                   $"|jpeg={isJpeg}|aom={effectiveAomParams}|depth={bitDepth}";
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

            // 使用与 SSIM 缓存一致的键，确保后续可以复用
            string key = GetSsimCacheKey(normalizedInput, crf, pixFmt, tileCols, cpuUsed, jpeg, effectiveAom, actualDepth);

            if (_metricsCache.TryGetValue(key, out QualityMetrics? cached))
                return cached;

            // 防止重复计算
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
                // 控制并发 ffmpeg 调用
                if (!await _ssimConcurrency.WaitAsync(TimeSpan.FromSeconds(300), _globalCts?.Token ?? default))
                {
                    Logger.Log($"GetOrComputeMetrics 信号量等待超时: [{Path.GetFileName(input)}] CRF={crf}");
                    newTask.SetResult(null);
                    return null;
                }

                try
                {
                    // 生成临时编码文件
                    string tmp = Path.Combine(_outputDir, $"_p_{Guid.NewGuid():N}.avif");
                    try
                    {
                        int searchCpu = Math.Min(cpuUsed + 2, 8);
                        var encResult = await EncodeToFileExAsync(input, tmp, crf, tileCols, searchCpu, cfg, jpeg, pixFmt,
                            isTrueLossless: false, cfg.SearchEncodeTimeoutMinutes, allowParamDegrade: true);

                        if (!encResult.ok || !File.Exists(tmp) || new FileInfo(tmp).Length < 100)
                        {
                            newTask.SetResult(null);
                            return null;
                        }

                        QualityMetrics? metrics = await ComputeAllMetricsAsync(input, tmp);
                        if (metrics != null)
                        {
                            _metricsCache[key] = metrics;
                            Logger.Log($"GetOrComputeMetrics [CRF={crf}] [{Path.GetFileName(input)}]: " +
                                       $"SSIM={metrics.SSIM:F4}, PSNR-Y={metrics.PSNR_Y:F2}dB, " +
                                       $"MS-SSIM={metrics.MS_SSIM:F4}, VMAF={metrics.VMAF:F2}");
                        }
                        newTask.SetResult(metrics);
                        return metrics;
                    }
                    finally
                    {
                        if (File.Exists(tmp)) try { File.Delete(tmp); } catch { }
                    }
                }
                finally
                {
                    _ssimConcurrency.Release();
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"GetOrComputeMetrics 异常: {ex.Message}");
                newTask.TrySetResult(null);
                return null;
            }
            finally
            {
                if (isOwner)
                    _metricsTasks.TryRemove(key, out _);
            }
        }




        // ========== 修复后的 GetOrComputeSSIM（信号量超时） ==========
        private async Task<double> GetOrComputeSSIM(string input, int crf, int tileCols, int cpuUsed, PresetConfig cfg, bool jpeg, string pixFmt)
        {
            if (cfg.Lossless) return 1.0;

            int actualDepth = pixFmt.Contains("10le") ? 10 : 8;
            string normalizedInput = GetNormalizedPathForCache(input);
            string effectiveAom = cfg.GetEffectiveAomParams();

            // ★ 使用统一缓存键生成方法
            string key = GetSsimCacheKey(normalizedInput, crf, pixFmt, tileCols, cpuUsed, jpeg, effectiveAom, actualDepth);

            if (_ssimCache.TryGetValue(key, out double cached))
                return cached;

            var newTask = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = _ssimTasks.GetOrAdd(key, newTask.Task);
            bool isOwner = task == newTask.Task;

            if (!isOwner)
            {
                try { return await task.WaitAsync(TimeSpan.FromMinutes(30)); }
                catch { return -1; }
            }

            try
            {
                if (!await _ssimConcurrency.WaitAsync(TimeSpan.FromSeconds(300), _globalCts?.Token ?? default))
                {
                    double fail = -1;
                    newTask.SetResult(fail);
                    return fail;
                }

                try
                {
                    string tmp = Path.Combine(_outputDir, $"_p_{Guid.NewGuid():N}.avif");
                    try
                    {
                        int searchCpu = Math.Min(cpuUsed + 2, 8);
                        (bool ok, TimeSpan _, int retries, string error, bool _, string? _, string? _) =
                            await EncodeToFileExAsync(input, tmp, crf, tileCols, searchCpu, cfg, jpeg, pixFmt, false, cfg.SearchEncodeTimeoutMinutes, allowParamDegrade: true);

                        if (!ok || !File.Exists(tmp) || new FileInfo(tmp).Length < 100)
                        {
                            double fail = -1;
                            newTask.SetResult(fail);
                            return fail;
                        }

                        double s = await SSIMDirect(input, tmp, pixFmt);
                        if (s >= 0)
                            _ssimCache[key] = s;
                        newTask.SetResult(s);
                        return s;
                    }
                    finally { if (File.Exists(tmp)) File.Delete(tmp); }
                }
                finally { _ssimConcurrency.Release(); }
            }
            catch (Exception)
            {
                double fail = -1;
                newTask.TrySetResult(fail);
                return fail;
            }
            finally
            {
                if (isOwner)
                    _ssimTasks.TryRemove(key, out _);
            }
        }

        private async Task<int> FindLowBoundWithRetry(
        Func<int, Task<double>> getSSIM, int minCRF, int maxCRF,
        string name, CancellationToken token)
        {
            for (int i = minCRF; i <= maxCRF && i - minCRF < 5; i++)  // ★ 由 3 改为 5
            {
                token.ThrowIfCancellationRequested();
                double s = await getSSIM(i);
                if (s >= 0) return i;
            }
            SafeWriteLine($"  [{name}] [FAIL] 低端指标连续失败");
            return -1;
        }


        private async Task<int> FindHighBoundWithRetry(
        Func<int, Task<double>> getSSIM, int low, int maxCRF,
        string name, CancellationToken token)
        {
            for (int i = maxCRF; i >= low && maxCRF - i < 5; i--)  // ★ 由 3 改为 5
            {
                token.ThrowIfCancellationRequested();
                double s = await getSSIM(i);
                if (s >= 0) return i;
            }
            SafeWriteLine($"  [{name}] [FAIL] 高端 SSIM 连续失败");
            return -1;
        }


        private async Task<(int bestCRF, bool found, bool anySuccess)> BinarySearchWithSkip(
    Func<int, Task<double>> getSSIM, int low, int high,
    double target, string name, CancellationToken token,
    // 新增参数：用于获取原始 metrics 显示
    PresetConfig cfg, int tileCols, string pixFmt, bool jpeg)
        {
            int best = low;
            bool found = false;
            bool anySuccess = false;
            int l = low, r = high;
            while (l <= r)
            {
                token.ThrowIfCancellationRequested();
                int mid = (l + r) / 2;
                double s = await getSSIM(mid);
                if (s < 0)
                {
                    SafeWriteLine($"  [{name}] [SEARCH] CRF={mid} 计算失败，跳过");
                    l = mid + 1;
                    continue;
                }

                anySuccess = true;

                // ★ 从缓存获取原始 metrics 用于显示（通常直接命中 _metricsCache）
                var m = await GetOrComputeMetrics(name, mid, tileCols, cfg.SearchCpuUsed, cfg, jpeg, pixFmt);
                string display = m != null
                    ? $"VMAF={m.VMAF:F1}  PSNR-Y={m.PSNR_Y:F2}dB  SSIM={m.SSIM:F4}  MS-SSIM={m.MS_SSIM:F4}"
                    : $"分数={s:F4}";

                SafeWriteLine($"  [{name}] [SEARCH] CRF={mid} -> {display}");

                if (s >= target)
                {
                    best = mid;
                    found = true;
                    l = mid + 1;
                }
                else
                {
                    r = mid - 1;
                    if (!found) best = mid;
                }
            }
            return (best, found, anySuccess);
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


        private async Task<(int crf, bool searchFailed, bool qualityInsufficient)> BinarySearchCRFAsync(
    string input, int tileCols, PresetConfig cfg, string pixFmt, bool jpeg)
        {
            string name = Path.GetFileName(input);
            double target = cfg.TargetSSIM + SSIMMargin;   // 目标值仍然沿用 TargetSSIM（现在可以是通用质量目标）
            Logger.CRF($"[{name}] 二分搜索 目标={target:F4} 模式={cfg.MetricMode ?? "ssim"}");

            using var searchCts = new CancellationTokenSource(TimeSpan.FromMinutes(cfg.SearchTimeoutMinutes));
            var token = CancellationTokenSource.CreateLinkedTokenSource(
                searchCts.Token, _globalCts?.Token ?? default).Token;

            // ★ 统一的评分工厂，使用多指标
            Func<int, Task<double>> getScore = async crf =>
            {
                for (int i = 0; i < 3; i++)
                {
                    token.ThrowIfCancellationRequested();
                    QualityMetrics? m = await GetOrComputeMetrics(input, crf, tileCols, cfg.SearchCpuUsed, cfg, jpeg, pixFmt);
                    if (m != null)
                    {
                        double score = GetSearchScore(m, cfg.MetricMode ?? "ssim");
                        return score;
                    }
                    if (i < 2) Logger.Log($"指标获取失败，重试 ({i + 1}/2): {name} CRF={crf}");
                }
                return -1;   // 全部失败
            };

            // 以下逻辑与原 BinarySearchCRFAsync 完全一致，只是把 getSSIM 换成了 getScore
            try
            {
                // 1. 低端边界
                int low = await FindLowBoundWithRetry(getScore, cfg.MinCRF, cfg.MaxCRF, name, token);
                if (low < 0) return (cfg.BaseCRF, true, false);

                double lowScore = await getScore(low);
                if (lowScore < 0) return (cfg.BaseCRF, true, false);
                if (lowScore < target)
                {
                    SafeWriteLine($"  [{name}] [LOW] 最低可用 CRF({low}) 分数={lowScore:F4} 不达标");
                    return (low, false, true);
                }

                // 2. 高端边界
                int high = await FindHighBoundWithRetry(getScore, low, cfg.MaxCRF, name, token);
                if (high < 0)
                {
                    SafeWriteLine($"  [{name}] [WARN] 高端边界搜索失败，回退用 BaseCRF");
                    return (cfg.BaseCRF, true, false);
                }

                if (high < low)
                {
                    SafeWriteLine($"  [{name}] [WARN] 高端边界 {high} < 低端边界 {low}，将 high 调整为 {low}");
                    high = low;
                }

                double highScore = await getScore(high);
                if (highScore < 0)
                {
                    if (high == low)
                        return (low, false, false);
                    SafeWriteLine($"  [{name}] [WARN] 高端边界分数计算失败，回退用 BaseCRF");
                    return (cfg.BaseCRF, true, false);
                }

                if (highScore >= target)
                {
                    SafeWriteLine($"  [HIGH] [{name}] 最高可用 CRF({high}) 分数={highScore:F4} 已达标，使用 MaxCRF");
                    return (high, false, false);
                }

                // 3. 二分搜索
                (int best, bool found, bool anySuccess) = await BinarySearchWithSkip(getScore, low, high, target, name, token, cfg, tileCols, pixFmt, jpeg);
                if (!found && !anySuccess)
                    return (cfg.BaseCRF, true, false);
                if (!found)
                {
                    SafeWriteLine($"  [{name}] [LOW] 所有搜索点分数均不足，使用最佳可用 CRF={best}");
                    return (best, false, true);
                }

                // ★ 获取最佳 CRF 的实际指标用于日志
                QualityMetrics? bestMetrics = await GetOrComputeMetrics(input, best, tileCols, cfg.SearchCpuUsed, cfg, jpeg, pixFmt);
                if (bestMetrics != null)
                {
                    SafeWriteLine($"  [BEST] [{name}] 最佳 CRF = {best} | " +
                                  $"SSIM={bestMetrics.SSIM:F4} PSNR-Y={bestMetrics.PSNR_Y:F2} " +
                                  $"MS-SSIM={bestMetrics.MS_SSIM:F4} VMAF={bestMetrics.VMAF:F2}");
                }
                else
                {
                    SafeWriteLine($"  [BEST] [{name}] 最佳 CRF = {best}");
                }

                return (best, false, false);
            }
            catch (OperationCanceledException)
            {
                SafeWriteLine($" [{name}] [WARN] 搜索超时/取消，回退用 BaseCRF ({cfg.BaseCRF})");
                Logger.Log($"SSIM 搜索超时/取消: {name}");
                return (cfg.BaseCRF, true, false);
            }
        }



        private static string? FindExecutable(string name)
        {
            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator);
            foreach (var p in paths ?? Array.Empty<string>())
            {
                string full = Path.Combine(p, OperatingSystem.IsWindows() ? $"{name}.exe" : name);
                if (File.Exists(full)) return full;
            }
            return null;
        }



        /// <summary>
        /// 获取图像分辨率，优先从统一 Probe 缓存获取。
        /// </summary>
        private async Task<(int w, int h)> GetResolutionAsync(string path)
        {
            // ★ 优先从统一 Probe 缓存获取
            var info = await GetProbeInfoAsync(path);
            if (info != null)
            {
                // 同步更新旧的分辨率缓存
                string cacheKey = GetNormalizedPathForCache(path);
                _resolutionCache[cacheKey] = (info.Width, info.Height);
                return (info.Width, info.Height);
            }

            // 兜底：单独探测
            string args = $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=p=0 \"{path}\"";
            string o = await RunProcessAndGetOutputAsync(_ffprobePath, args).WaitAsync(TimeSpan.FromSeconds(30));
            var parts = o.Trim().Split(',');
            if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
            {
                _resolutionCache[GetNormalizedPathForCache(path)] = (w, h);
                return (w, h);
            }
            return (0, 0);
        }

        private void ExportCsv(IEnumerable<EncodeResult> results)
        {
            string p = Path.Combine(_outputDir, "avif_stats.csv");
            var sb = new StringBuilder();

            // 调整列顺序：将 VMAF、PSNR‑Y、MS‑SSIM、MixScore 紧挨在 “SSIM” 之后
            sb.AppendLine("文件名,原始文件名,原始大小,输出大小,压缩率,CRF,SSIM,VMAF,PSNR-Y,MS-SSIM,MixScore,编码耗时(秒),搜索耗时(秒),总耗时(秒),重试次数,像素格式,源像素格式,模式,安全模式,完整命令行,AOM参数,缓存复用,状态,失败原因");

            foreach (var r in results)
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

                // 拼接顺序务必与表头一致
                sb.AppendLine(CsvEscape(r.FileName) + "," +
                              CsvEscape(r.OriginalFileName) + "," +
                              r.OriginalSize.ToString(CultureInfo.InvariantCulture) + "," +
                              r.OutputSize.ToString(CultureInfo.InvariantCulture) + "," +
                              r.CompressionRatio.ToString("F4", CultureInfo.InvariantCulture) + "," +
                              r.UsedCRF.ToString(CultureInfo.InvariantCulture) + "," +
                              r.FinalSSIM.ToString("F4", CultureInfo.InvariantCulture) + "," +
                              vmaf + "," +
                              psnrY + "," +
                              msssim + "," +
                              mix + "," +
                              r.EncodeTime.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + "," +
                              r.SearchTime.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + "," +
                              r.TotalTime.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + "," +
                              r.Retries.ToString(CultureInfo.InvariantCulture) + "," +
                              CsvEscape(fmt) + "," +
                              CsvEscape(srcFmt) + "," +
                              CsvEscape(mode) + "," +
                              CsvEscape(safe) + "," +
                              command + "," +
                              aomParams + "," +
                              CsvEscape(cache) + "," +
                              CsvEscape(status) + "," +
                              errMsg);
            }

            File.WriteAllText(p, sb.ToString(), new UTF8Encoding(true));
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
            int defaultThreads = GetDefaultThreads();
            Console.WriteLine($@"
AVIF 编码器 CLI -- 帮助手册
========================================

用法:
  AvifEncoder -i <输入目录> -o <输出目录> [选项]

基本参数
----------------------------------------
  -i <dir>         输入文件夹 (默认: input)
  -o <dir>         输出文件夹 (默认: Avifoutput)

预设模式 (快速配置)
----------------------------------------
  -p fast|balanced|best   选择预设 (默认: extreme)

  预设      | CRF | 目标SSIM | 色彩采样 | 搜索
  ----------|-----|----------|----------|-----
  fast      | 38  | 0.91     | 420      | 关闭
  balanced  | 36  | 0.97     | 420      | 关闭
  best      | 34  | 0.97     | 444      | 开启
  extreme   | 35  | 0.99     | 444      | 开启

  注：预设可被后续参数覆盖

质量控制 (互斥开关)
----------------------------------------
  -s         启用 CRF 搜索 (自动选择最优 CRF)
  -n         禁用 CRF 搜索 (默认，使用预设 CRF)
  -q <n>     手动设置质量目标，输入值为当前 --metric 模式的原生数值：
               ssim    - 0.0~1.0（直接 SSIM）
               psnr    - 30~50（单位 dB，亮度 PSNR）
               msssim  - 0.0~1.0（直接 MS-SSIM）
               vmaf    - 0~100（VMAF 分数，90 以上为高质量）
               mix     - 0.0~1.0（加权混合评分）
             示例：
               --metric vmaf -q 95   → 要求 VMAF ≥ 95
               --metric psnr -q 40   → 要求亮度 PSNR ≥ 40 dB
               --metric mix -q 0.90  → 要求综合评分 ≥ 0.90
             若未手动指定，则使用预设值并自动适配上限。

色彩采样 (三选一，默认源自适应)
----------------------------------------
  -c         使用 4:2:0 色度采样
  -g         使用 4:2:2 色度采样
  -f         使用 4:4:4 色度采样
  -a         源自适应 (根据源文件自动选择色度采样和位深，默认启用)
             手动指定 -c/-g/-f 或 -d 会关闭自适应

位深 (二选一，默认8bit / 自适应)
----------------------------------------
  -d 8       使用 8 位位深
  -d 10      使用 10 位位深
             指定 -d 会关闭自适应

输出命名 (自定义文件名) 直接写模板即可，无需加引号
----------------------------------------
  -m <模板>        输出文件名模板 (默认: covers-{{index}}.avif)
                   可用占位符: {{name}} 源文件主名, {{index}} 序号(01,02...)
                   正确示例:
                     -m {{name}}.avif            按源文件名
                     -m img_{{index}}.avif       自定义前缀
                     -m {{name}}_{{index}}.avif   源名+序号
                   错误示例:
                     -m ""{{name}}.avif""        （引号会被当成文件名的一部分）

其他编码选项
----------------------------------------
  -r <crf>   手动指定 CRF (1~50)，仅在搜索禁用时生效
             或 -r min:max 设置 CRF 搜索范围 (0~63)，需配合 -s 使用
  -l         无损模式 (支持真无损源文件，有损源自动数学无损)
  -t <n>     并行处理线程数 (默认: 自动，当前 {defaultThreads} 线程)
  -e         保留控制台快速编辑模式 (允许用鼠标选择文字，但选择期间程序会暂停)
             默认行为：不加 -e 时，程序会自动禁用快速编辑模式，以避免鼠标误触导致程序卡死；
             此时无法用鼠标直接选择文字，但运行结束后会恢复。

  --encoder <名称>   指定编码器 (默认 libaom-av1)
                     常用 AV1 编码器:
                       libaom-av1  压缩效率最高，速度最慢 (支持所有 aq/deltaq 参数)
                       libsvtav1   速度最快，多线程优化 (适合批量处理)
                       rav1e       速度与压缩率平衡，Rust 实现 (部分高级参数不支持)
                     可使用 ffmpeg -encoders | grep av1 查看本机支持的编码器
  --metric <模式>   质量评价模式 (默认 vmaf)
                     vmaf   - VMAF
                     ssim   - SSIM
                     psnr   - PSNR (亮度)
                     msssim - MS-SSIM       
                     mix    - 加权混合评分
超时选项 (所有值均为正整数)
----------------------------------------
  --timeout-encode <分钟>         单次最终编码超时 (默认自动计算：5~180)
  --timeout-search <分钟>         搜索全局超时 (默认 60)
  --timeout-safe <分钟>           安全模式全扫描超时 (默认 180)
  --timeout-safe-encode <分钟>    安全模式内单次编码超时 (默认 10)
  --timeout-search-encode <分钟>  搜索过程中临时编码超时 (默认 10)
  --timeout-ssim <分钟>           SSIM 计算超时 (默认 5)

搜索策略 (启用 -s 时生效)
----------------------------------------
  1. 尝试用户指定的色度+位深
  2. 降级位深 (10->8 bit)
  3. 降级色度采样 (444->422->420, 8/10 bit)
  4. 安全模式全 CRF 扫描 (yuv420p)
  全部失败则回退至预设 CRF 直接编码。
  (自适应模式下仅使用源匹配格式，不降级)

使用示例
----------------------------------------
  . 默认模式 (自适应 + 不搜索):
      -i pics -o out

  . 自适应 + 搜索 + 目标 0.98:
      -s -q 0.98 -i pics -o out

  . 强制 444p 10bit + 搜索:
      -f -d 10 -s -q 0.98

  . 手动指定 CRF=30, 不搜索, 422 色度 (8bit):
      -r 30 -g -n

  . 自定义 CRF 搜索范围 (0~63) 并搜索:
      -s -r 0:63

  . 使用 SVT-AV1 编码器:
      -s -q 0.95 --encoder libsvtav1

  . 调整超时时间 (编码120分钟, 搜索180分钟):
      -s -q 0.95 --timeout-encode 120 --timeout-search 180

  . 保留快速编辑模式 (方便复制日志，但注意不要点击窗口):
      -e -s -q 0.95 -t 2

  . 无损处理 PNG 图片:
      -l -i pngs -o avifs

  . 自定义文件名 (不要加引号):
      -m {{name}}.avif -i pics
      -m cover_{{index}}.avif
");
        }

        // ========== 参数解析数据类 ==========
        private class ParsedOptions
        {
            public string InputDir = "input";
            public string OutputDir = "Avifoutput";
            public CliPreset Preset = CliPreset.Extreme;
            public bool ForceSearch;
            public bool ForceNoSearch;
            public bool Force420, Force444, Force422;
            public bool ForceLossless;
            public int? ManualThreads;
            public double? CustomSSIM;
            public int? ManualCRF;
            public int? BitDepth;
            public string? NameFormat;
            public bool AutoSource = true;
            public int? MinCRF, MaxCRF;
            public int? EncodeTimeout, SearchTimeout, SafeTimeout,
                        SafeEncodeTimeout, SearchEncodeTimeout, SsimTimeout;
            public string? CustomEncoder;
            public string? MetricMode;          // ★ 新增
        }

        // ========== 参数解析 ==========
        private static ParsedOptions ParseCommandLineArgs(string[] args)
        {
            var opts = new ParsedOptions();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                // 超时选项
                if (arg.StartsWith("--timeout-"))
                {
                    string timeoutType = arg["--timeout-".Length..];
                    if (i + 1 >= args.Length) throw new Exception($"选项 {arg} 需要一个值");
                    string valStr = args[++i];
                    if (!int.TryParse(valStr, out int val) || val <= 0)
                        throw new Exception($"{arg} 必须是正整数");
                    switch (timeoutType)
                    {
                        case "encode": opts.EncodeTimeout = val; break;
                        case "search": opts.SearchTimeout = val; break;
                        case "safe": opts.SafeTimeout = val; break;
                        case "safe-encode": opts.SafeEncodeTimeout = val; break;
                        case "search-encode": opts.SearchEncodeTimeout = val; break;
                        case "ssim": opts.SsimTimeout = val; break;
                        default: throw new Exception($"未知超时选项: {arg}");
                    }
                    continue;
                }

                // 自定义编码器
                if (arg == "--encoder" && i + 1 < args.Length)
                {
                    opts.CustomEncoder = args[++i];
                    continue;
                }
                if (arg.StartsWith("--encoder="))
                {
                    opts.CustomEncoder = arg["--encoder=".Length..];
                    continue;
                }

                // ★ 新增指标模式
                if (arg == "--metric" && i + 1 < args.Length)
                {
                    opts.MetricMode = args[++i].ToLower();
                    continue;
                }
                if (arg.StartsWith("--metric="))
                {
                    opts.MetricMode = arg["--metric=".Length..].ToLower();
                    continue;
                }

                // 单字符选项
                if (arg.StartsWith('-') && arg.Length > 1)
                {
                    string flags = arg[1..];
                    if (flags.Equals("p") && i + 1 < args.Length)
                    {
                        string p = args[++i].ToLower();
                        opts.Preset = p switch
                        {
                            "fast" => CliPreset.Fast,
                            "balanced" => CliPreset.Balanced,
                            "best" => CliPreset.Best,
                            "extreme" => CliPreset.Extreme,
                            _ => throw new Exception("预设必须为 fast/balanced/best/extreme")
                        };
                    }
                    else if (flags.Equals("t") && i + 1 < args.Length)
                    {
                        if (int.TryParse(args[++i], out int t)) opts.ManualThreads = t;
                        else throw new Exception("线程数必须是数字");
                    }
                    else if (flags.Equals("d") && i + 1 < args.Length)
                    {
                        if (int.TryParse(args[++i], out int d) && (d == 8 || d == 10))
                        {
                            opts.BitDepth = d;
                            opts.AutoSource = false;
                        }
                        else throw new Exception("位深必须是 8 或 10");
                    }
                    else if (flags.Equals("m") && i + 1 < args.Length)
                    {
                        opts.NameFormat = args[++i].Trim('"').Trim('\'');
                    }
                    else if (flags.Equals("i") && i + 1 < args.Length) { opts.InputDir = args[++i]; }
                    else if (flags.Equals("o") && i + 1 < args.Length) { opts.OutputDir = args[++i]; }
                    else if (flags.Equals("q") && i + 1 < args.Length)
                    {
                        if (double.TryParse(args[++i], out double q))
                            opts.CustomSSIM = q;   // 不再检查范围，由后续转换时负责
                        else
                            throw new Exception("-q 需要是一个数值");
                    }
                    else if (flags.Equals("r") && i + 1 < args.Length)
                    {
                        string rawCRF = args[++i];
                        if (rawCRF.Contains(':'))
                        {
                            var parts = rawCRF.Split(':');
                            if (parts.Length == 2 &&
                                int.TryParse(parts[0], out int minVal) && minVal >= 0 && minVal <= 63 &&
                                int.TryParse(parts[1], out int maxVal) && maxVal >= 0 && maxVal <= 63 &&
                                minVal < maxVal)
                            {
                                opts.MinCRF = minVal;
                                opts.MaxCRF = maxVal;
                            }
                            else throw new Exception("CRF 范围格式错误，应为 min:max (0~63 且 min<max)");
                        }
                        else
                        {
                            if (int.TryParse(rawCRF, out int r) && r >= 1 && r <= 50)
                                opts.ManualCRF = r;
                            else throw new Exception("CRF 必须是 1~50 的整数");
                        }
                    }
                    else if (flags.Equals("h")) { PrintHelp(); return null!; }
                    else
                    {
                        foreach (char c in flags)
                        {
                            switch (c)
                            {
                                case 's': opts.ForceSearch = true; opts.ForceNoSearch = false; break;
                                case 'n': opts.ForceNoSearch = true; opts.ForceSearch = false; break;
                                case 'a': opts.AutoSource = true; break;
                                case 'c': opts.AutoSource = false; opts.Force420 = true; opts.Force444 = false; opts.Force422 = false; break;
                                case 'f': opts.AutoSource = false; opts.Force444 = true; opts.Force420 = false; opts.Force422 = false; break;
                                case 'g': opts.AutoSource = false; opts.Force422 = true; opts.Force420 = false; opts.Force444 = false; break;
                                case 'l': opts.ForceLossless = true; break;
                                default:
                                    Console.WriteLine($"未知选项: -{c}");
                                    return null!;
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"未知参数: {arg}");
                    return null!;
                }
            }
            return opts;
        }

        // ========== 根据解析结果构建配置 ==========
        private static PresetConfig BuildPresetConfig(ParsedOptions opts)
        {
            PresetConfig config;
            if (opts.ForceLossless)
            {
                config = new PresetConfig
                {
                    BaseCRF = 0,
                    TargetSSIM = 1.0,
                    FinalCpuUsed = 0,
                    SearchCpuUsed = 0,
                    UseCRFSearch = false,
                    Lossless = true,
                    PixelFormat = null,
                    AomParams = "aq-mode=0:deltaq-mode=0:enable-chroma-deltaq=0",
                    MaxJobs = GetDefaultThreads(),
                    BitDepth = 10
                };
            }
            else
            {
                config = AvifPipeline.CreateFromPreset(opts.Preset);
                if (opts.ForceSearch) config.UseCRFSearch = true;
                if (opts.ForceNoSearch) config.UseCRFSearch = false;
                if (opts.Force444) config.PixelFormat = "yuv444p10le";
                else if (opts.Force420) config.PixelFormat = "yuv420p10le";
                else if (opts.Force422) config.PixelFormat = "yuv422p10le";
                if (opts.ManualThreads.HasValue) config.MaxJobs = opts.ManualThreads.Value;
                if (opts.CustomSSIM.HasValue)
                {
                    // ★ 修复：优先使用用户通过 --metric 指定的模式，否则使用当前配置（预设）的模式
                    string effectiveMetric = opts.MetricMode ?? config.MetricMode ?? "vmaf";
                    config.SetQualityTarget(opts.CustomSSIM.Value, effectiveMetric);
                }
            }

            if (opts.MinCRF.HasValue) config.MinCRF = opts.MinCRF.Value;
            if (opts.MaxCRF.HasValue) config.MaxCRF = opts.MaxCRF.Value;
            if (config.MinCRF >= config.MaxCRF) throw new Exception("最小 CRF 必须小于最大 CRF");

            if (opts.EncodeTimeout.HasValue) config.EncodeTimeoutMinutes = opts.EncodeTimeout.Value;
            if (opts.SearchTimeout.HasValue) config.SearchTimeoutMinutes = opts.SearchTimeout.Value;
            if (opts.SafeTimeout.HasValue) config.SafeTimeoutMinutes = opts.SafeTimeout.Value;
            if (opts.SafeEncodeTimeout.HasValue) config.SafeEncodeTimeoutMinutes = opts.SafeEncodeTimeout.Value;
            if (opts.SearchEncodeTimeout.HasValue) config.SearchEncodeTimeoutMinutes = opts.SearchEncodeTimeout.Value;
            if (opts.SsimTimeout.HasValue) config.SsimTimeoutMinutes = opts.SsimTimeout.Value;

            if (!string.IsNullOrEmpty(opts.CustomEncoder))
                config.Encoder = opts.CustomEncoder;

            if (!string.IsNullOrEmpty(opts.NameFormat))
                config.OutputNameFormat = opts.NameFormat;

            // ★ 设置指标模式
            if (!string.IsNullOrEmpty(opts.MetricMode))
                config.MetricMode = opts.MetricMode;

            // ★ 自动调整默认目标（仅在用户未手动指定 -q 时）
            bool userSetQuality = opts.CustomSSIM.HasValue;
            if (!userSetQuality)
                config.AdjustTargetForMetricMode();

            config.AutoSource = opts.AutoSource;
            if (!opts.AutoSource)
            {
                if (opts.Force444 || opts.Force422 || opts.Force420) config.UserSetChroma = true;
                if (opts.BitDepth.HasValue) config.UserSetBitDepth = true;
            }
            else
            {
                config.PixelFormat = null;
                if (opts.BitDepth.HasValue)
                {
                    config.BitDepth = opts.BitDepth.Value;
                    config.UserSetBitDepth = true;
                }
            }

            if (!config.AutoSource || config.UserSetBitDepth)
            {
                if (opts.BitDepth.HasValue) config.BitDepth = opts.BitDepth.Value;
                AvifPipeline.ApplyBitDepth(config);
            }

            // 位置：BuildPresetConfig 方法内，替换原有的 if (opts.ManualCRF.HasValue) 块
            if (opts.ManualCRF.HasValue && !config.Lossless)  // 增加 Lossless 检查
            {
                if (!config.UseCRFSearch)
                {
                    config.BaseCRF = opts.ManualCRF.Value;
                    Console.WriteLine($"手动设置 CRF = {config.BaseCRF}");
                }
                else
                {
                    Console.WriteLine("[WARN] -r 仅在禁用搜索时有效，已忽略");
                }
            }

            return config;
        }

        // ========== 程序入口 ==========
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            // 快速编辑模式管理
            bool forceEnableQuickEdit = false;
            uint originalMode = 0;
            IntPtr consoleHandle = IntPtr.Zero;

            if (args.Contains("-e"))
            {
                forceEnableQuickEdit = true;
                args = args.Where(a => a != "-e").ToArray();
            }

            // ---------- 预先检查 ffmpeg 是否可用 ----------
            bool ffmpegFound = false;
            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator);
            foreach (var p in paths ?? Array.Empty<string>())
            {
                string full = Path.Combine(p, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
                if (File.Exists(full))
                {
                    ffmpegFound = true;
                    break;
                }
            }

            if (!ffmpegFound)
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
                Console.WriteLine($"当前ffmpeg 支持的 AV1 编码器: {string.Join(", ", allEncoders)}");

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
                        Console.WriteLine("  -- 软件编码器（推荐，支持全部参数） --");
                        foreach (var (name, _, _) in softAvail)
                            Console.WriteLine($"  [OK] {name,-12}  (--encoder {name})");
                    }
                    if (hardAvail.Count > 0)
                    {
                        Console.WriteLine("  -- 硬件编码器（速度较快，不支持部分高级参数） --");
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
                    args = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                else
                { Console.WriteLine("未输入参数，退出。"); Console.ReadKey(); return; }
            }

            // 解析参数
            ParsedOptions? opts = ParseCommandLineArgs(args);
            if (opts == null) return;

            // 构建配置
            PresetConfig config = BuildPresetConfig(opts);

            // 临时禁用快速编辑
            if (!forceEnableQuickEdit && OperatingSystem.IsWindows())
            {
                try
                {
                    consoleHandle = GetStdHandle(-10);
                    if (GetConsoleMode(consoleHandle, out originalMode))
                    {
                        const uint ENABLE_QUICK_EDIT = 0x0040;
                        SetConsoleMode(consoleHandle, originalMode & ~ENABLE_QUICK_EDIT);
                    }
                }
                catch { }
            }

            AvifPipeline? pipeline = null;
            try
            {
                pipeline = new AvifPipeline(opts.InputDir, opts.OutputDir, config);
                await pipeline.RunAsync();
            }
            catch (Exception ex) { Console.WriteLine($"[FAIL] 错误: {ex.Message}"); }
            finally
            {
                // 释放 pipeline 资源（信号量、取消令牌等）
                pipeline?.Dispose();

                if (!forceEnableQuickEdit && consoleHandle != IntPtr.Zero)
                {
                    try { SetConsoleMode(consoleHandle, originalMode); } catch { }
                }
                Console.WriteLine("按任意键退出...");
                Console.ReadKey();
            }
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