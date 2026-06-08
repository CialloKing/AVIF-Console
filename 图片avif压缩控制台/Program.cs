using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static AvifEncoder.PresetConfig;



namespace AvifEncoder
{
    class Program
    {
        private static string AppVersion =>
    System.Reflection.Assembly.GetEntryAssembly()?
        .GetName().Version?.ToString(3) ?? "1.1.0";

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
            Console.WriteLine(HelpText.CliHelp);
        }

        // ========== 参数解析数据类 ==========
        private class ParsedOptions
        {
            public string InputDir = "input";
            public string OutputDir = "Avifoutput";
            public CliPreset Preset = CliPreset.Balanced;
            public bool EnableSearch = true;
            public bool ForceNoSearch = false;
            public double? QualityTarget;
            public string MetricMode = "vmaf";
            public string? DirectTargetMode;
            public double? DirectTargetValue;
            public int? ManualCrf;
            public int? CrfMin, CrfMax;
            public string Chroma = "auto";
            public int? BitDepth;
            public bool Lossless = false;
            public string? OutputTemplate;
            public string Encoder = "libaom-av1";
            public int? Jobs;
            public bool Recursive = false;
            public int? MaxResolution;
            public bool OutputFullRes = false;
            public int? EncodeTimeout, SearchTimeout, SafeTimeout,
                        SafeEncodeTimeout, SearchEncodeTimeout, SsimTimeout;
            public bool Verbose = false;
            public bool Quiet = false;
            public bool ShowVersion = false;
            public bool DryRun = false;
            public bool Overwrite = false;
            public bool NoClobber = false;
            public bool SerialEncode { get; set; } = false;
            public bool EnableProxySearch { get; set; } = false;
            public bool UsePriorSearch { get; set; } = false;
            public string? AdvancedMetricMode;
            public int? SearchCpuUsed;
            public int? FinalCpuUsed;
            public bool SweepMode { get; set; } = false;
            public bool RecomputeMetrics { get; set; } = false;
            public bool Resume { get; set; } = false;
            public string? Extensions { get; set; }
            public string? EncoderCustomParams { get; set; }
            public int? Denoise { get; set; }
            public bool ArNrUseMaxFrames { get; set; }
            public string? RgbMode { get; set; } = "";  // 默认关闭
        }

        // ========== 参数解析 ==========
        private static ParsedOptions ParseCommandLineArgs(string[] args)
        {
            var opts = new ParsedOptions();
            int i = 0;
            while (i < args.Length)
            {
                string arg = args[i];
                if (arg == "--") { break; }
                if (arg.StartsWith("--"))
                {
                    string key = arg[2..];
                    string? value = null;
                    int eq = key.IndexOf('=');
                    if (eq >= 0) { value = key[(eq + 1)..]; key = key[..eq]; }
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
                        case "quality": opts.QualityTarget = double.Parse(GetValue(), CultureInfo.InvariantCulture); break;
                        case "metric":
                            {
                                string raw = GetValue().ToLower();
                                opts.MetricMode = raw;
                                if (raw is "ssimu2" or "butter3" or "gmsd")
                                    opts.AdvancedMetricMode = raw;
                            }
                            break;
                        case "target-vmaf": opts.DirectTargetMode = "vmaf"; opts.DirectTargetValue = double.Parse(GetValue(), CultureInfo.InvariantCulture); break;
                        case "target-ssim": opts.DirectTargetMode = "ssim"; opts.DirectTargetValue = double.Parse(GetValue(), CultureInfo.InvariantCulture); break;
                        case "target-psnr": opts.DirectTargetMode = "psnr"; opts.DirectTargetValue = double.Parse(GetValue(), CultureInfo.InvariantCulture); break;
                        case "target-msssim": opts.DirectTargetMode = "msssim"; opts.DirectTargetValue = double.Parse(GetValue(), CultureInfo.InvariantCulture); break;
                        case "target-mix": opts.DirectTargetMode = "mix"; opts.DirectTargetValue = double.Parse(GetValue(), CultureInfo.InvariantCulture); break;
                        case "target-ssimu2":
                            opts.DirectTargetMode = "ssimu2";
                            opts.AdvancedMetricMode = "ssimu2";
                            opts.DirectTargetValue = double.Parse(GetValue(), CultureInfo.InvariantCulture);
                            break;
                        case "target-butter3":
                            opts.DirectTargetMode = "butter3";
                            opts.AdvancedMetricMode = "butter3";
                            opts.DirectTargetValue = double.Parse(GetValue(), CultureInfo.InvariantCulture);
                            break;
                        case "target-gmsd":
                            opts.DirectTargetMode = "gmsd";
                            opts.AdvancedMetricMode = "gmsd";
                            opts.DirectTargetValue = double.Parse(GetValue(), CultureInfo.InvariantCulture);
                            break;
                        case "target-xpsnr":
                            opts.DirectTargetMode = "xpsnr";
                            opts.DirectTargetValue = double.Parse(GetValue(), CultureInfo.InvariantCulture);
                            break;
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
                                if (int.TryParse(crfVal, out int r) && r >= 0 && r <= 63)
                                { opts.ManualCrf = r; opts.ForceNoSearch = true; }
                                else throw new Exception("CRF 应为 0-63 的整数");
                            }
                            break;
                        case "chroma":
                            string c = GetValue().ToLower();
                            if (new[] { "420", "422", "444", "auto" }.Contains(c))
                                opts.Chroma = c;
                            else throw new Exception("--chroma 仅支持 420/422/444/auto");
                            break;
                        case "bit-depth":
                            {
                                string bdStr = GetValue();
                                if (string.Equals(bdStr, "auto", StringComparison.OrdinalIgnoreCase))
                                {
                                    opts.BitDepth = null; // 使用自动检测
                                }
                                else if (int.TryParse(bdStr, out int bd) && (bd == 8 || bd == 10 || bd == 12))
                                {
                                    opts.BitDepth = bd;
                                }
                                else throw new Exception("--bit-depth 必须为 8、10、12 或 auto");
                            }
                            break;
                        case "lossless": opts.Lossless = true; break;
                        case "output-template": opts.OutputTemplate = GetValue().Trim('"', '\''); break;
                        case "encoder": opts.Encoder = GetValue(); break;
                        case "enc-params": opts.EncoderCustomParams = GetValue(); break;
                        case "denoise":
                            if (int.TryParse(GetValue(), out int dn) && dn >= 0)
                                opts.Denoise = Math.Min(dn, 15);
                            else throw new Exception("--denoise 需要非负整数");
                            break;
                        case "arnr-strength":
                            if (int.TryParse(GetValue(), out int ars) && ars >= 0)
                            {
                                opts.Denoise = Math.Min(ars, 6);
                                opts.ArNrUseMaxFrames = false;
                            }
                            else throw new Exception("--arnr-strength 需要 0-6 之间的整数");
                            break;
                        case "arnr-max-frames":
                            if (int.TryParse(GetValue(), out int armf) && armf >= 0)
                            {
                                opts.Denoise = Math.Min(armf, 15);
                                opts.ArNrUseMaxFrames = true;
                            }
                            else throw new Exception("--arnr-max-frames 需要 0-15 之间的整数");
                            break;
                        case "rgb-mode":
                            {
                                string v = GetValue().ToLower();
                                if (v is "auto")
                                    opts.RgbMode = null;
                                else if (v is "off" or "none")
                                    opts.RgbMode = "";
                                else if (v is "gbrp" or "rgb24" or "24")
                                    opts.RgbMode = "gbrp";
                                else if (v is "gbrap" or "rgb32" or "32")
                                    opts.RgbMode = "gbrap";
                                else if (v is "gbrp16le" or "rgb48" or "48")
                                    opts.RgbMode = "gbrp16le";
                                else throw new Exception("--rgb-mode 仅支持 auto/off/gbrp/gbrap/gbrp16le");
                            }
                            break;
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
                        case "overwrite": opts.Overwrite = true; break;
                        case "no-clobber": opts.NoClobber = true; break;
                        case "help": PrintHelp(); return null!;
                        case "prior-search": opts.UsePriorSearch = true; break;
                        case "serial-encode": opts.SerialEncode = true; break;
                        case "proxy": opts.EnableProxySearch = true; break;
                        case "search-cpu-used":
                            if (int.TryParse(GetValue(), out int searchCpu) && searchCpu >= 0 && searchCpu <= 13)
                                opts.SearchCpuUsed = searchCpu;
                            else throw new Exception("--search-cpu-used 需要 0-13 之间的整数");
                            break;
                        case "final-cpu-used":
                            if (int.TryParse(GetValue(), out int finalCpu) && finalCpu >= 0 && finalCpu <= 13)
                                opts.FinalCpuUsed = finalCpu;
                            else throw new Exception("--final-cpu-used 需要 0-13 之间的整数");
                            break;
                        case "sweep":
                            opts.SweepMode = true;
                            break;
                        case "recompute-metrics":
                            opts.RecomputeMetrics = true;
                            break;
                        case "extensions":
                            opts.Extensions = GetValue();
                            break;
                        case "resume":
                            opts.Resume = true;
                            break;
                        default:
                            if (key.StartsWith("timeout-"))
                            {
                                string type = key["timeout-".Length..];
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
                if (arg.StartsWith('-') && arg.Length > 1 && !char.IsDigit(arg[1]))
                {
                    string flags = arg[1..];
                    if (flags == "i" || flags == "o" || flags == "p" || flags == "c" || flags == "b" ||
                        flags == "t" || flags == "e" || flags == "j" || flags == "x")
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
                                if (string.Equals(val, "auto", StringComparison.OrdinalIgnoreCase))
                                {
                                    opts.BitDepth = null; // 使用自动检测
                                }
                                else if (int.TryParse(val, out int bd2) && (bd2 == 8 || bd2 == 10 || bd2 == 12))
                                {
                                    opts.BitDepth = bd2;
                                }
                                else throw new Exception("-b 必须为 8、10、12 或 auto");
                                break;
                            case "t": opts.OutputTemplate = val.Trim('"', '\''); break;
                            case "e": opts.Encoder = val; break;
                            case "j":
                                if (int.TryParse(val, out int j) && j > 0) opts.Jobs = j;
                                else throw new Exception("-j 需要正整数");
                                break;
                            case "x": opts.Extensions = val; break;
                        }
                        i++;
                        continue;
                    }
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
                            case 'y': opts.Overwrite = true; break;
                            case 'n': opts.NoClobber = true; break;
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

        // ========== 配置构建 ==========
        private static PresetConfig BuildPresetConfig(ParsedOptions opts)
        {
            PresetConfig config;
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
                    PixelFormat = null,
                    AomParams = "aq-mode=0:deltaq-mode=0:enable-chroma-deltaq=0",
                    MaxJobs = opts.Jobs ?? Math.Max(2, (int)Math.Sqrt(Environment.ProcessorCount)),
                    Encoder = opts.Encoder,
                    BitDepth = 10
                };
            }
            else
            {
            config = PresetConfig.CreateFromPreset(opts.Preset);
            config.Encoder = opts.Encoder;
            if (opts.ForceNoSearch) config.UseCRFSearch = false;
            else if (opts.EnableSearch) config.UseCRFSearch = true;
            if (opts.BitDepth.HasValue)
            {
                config.BitDepth = opts.BitDepth.Value;
                config.UserSetBitDepth = true;
                config.AutoSource = false;
            }
            if (opts.Chroma != "auto")
            {
                config.AutoSource = false;
                config.UserSetChroma = true;
                config.PixelFormat = opts.Chroma switch
                {
                    "420" => "yuv420p",
                    "422" => "yuv422p",
                    "444" => "yuv444p",
                    _ => "yuv420p"
                };
            }
            AvifPipeline.ApplyBitDepth(config);
            if (opts.DirectTargetValue.HasValue && !string.IsNullOrEmpty(opts.DirectTargetMode))
            {
                opts.MetricMode = opts.DirectTargetMode;
                opts.QualityTarget = opts.DirectTargetValue;
            }
            if (opts.QualityTarget.HasValue)
            {
                string effectiveMetric = opts.AdvancedMetricMode ?? opts.MetricMode ?? config.MetricMode ?? "vmaf";
                config.MetricMode = effectiveMetric;
                config.SetQualityTarget(opts.QualityTarget.Value, effectiveMetric);
            }
            else config.AdjustTargetForMetricMode();
            if (!string.IsNullOrEmpty(opts.MetricMode)) config.MetricMode = opts.MetricMode;
            if (opts.ManualCrf.HasValue)
            {
                config.BaseCRF = opts.ManualCrf.Value;
                config.UseCRFSearch = false;   // 固定 CRF 强制关闭搜索
            }
            if (opts.CrfMin.HasValue) config.MinCRF = opts.CrfMin.Value;
            if (opts.CrfMax.HasValue) config.MaxCRF = opts.CrfMax.Value;
            if (config.MinCRF > config.MaxCRF)  // 允许相等（Sweep 模式单 CRF 值合法）
                throw new Exception(
                    $"CRF 范围无效：最小值 {config.MinCRF} 必须小于或等于最大值 {config.MaxCRF}。" +
                    " 示例: --crf 20:40 或 --crf 30:30");
            if (opts.Jobs.HasValue) { config.MaxJobs = opts.Jobs.Value; config.UserSpecifiedMaxJobs = true; }
            if (!string.IsNullOrEmpty(opts.OutputTemplate)) config.OutputNameFormat = opts.OutputTemplate;
            if (opts.MaxResolution.HasValue) config.MaxResolution = opts.MaxResolution.Value;
            config.ApplyScalingToOutput = !opts.OutputFullRes;
            config.RecurseSubdirectories = opts.Recursive;
            if (opts.EncodeTimeout.HasValue) config.EncodeTimeoutMinutes = opts.EncodeTimeout.Value;
            if (opts.SearchTimeout.HasValue) config.SearchTimeoutMinutes = opts.SearchTimeout.Value;
            if (opts.SafeTimeout.HasValue) config.SafeTimeoutMinutes = opts.SafeTimeout.Value;
            if (opts.SafeEncodeTimeout.HasValue) config.SafeEncodeTimeoutMinutes = opts.SafeEncodeTimeout.Value;
            if (opts.SearchEncodeTimeout.HasValue) config.SearchEncodeTimeoutMinutes = opts.SearchEncodeTimeout.Value;
            if (opts.SsimTimeout.HasValue) config.SsimTimeoutMinutes = opts.SsimTimeout.Value;
            config.UseProxySearch = opts.EnableProxySearch;
            config.SerialEncode = opts.SerialEncode;
            config.UsePriorSearch = opts.UsePriorSearch;
            if (opts.SearchCpuUsed.HasValue) config.SearchCpuUsed = opts.SearchCpuUsed.Value;
            if (opts.FinalCpuUsed.HasValue) config.FinalCpuUsed = opts.FinalCpuUsed.Value;
            if (opts.Overwrite) config.FileConflictStrategy = PresetConfig.ConflictStrategy.Overwrite;
            else if (opts.NoClobber) config.FileConflictStrategy = PresetConfig.ConflictStrategy.Skip;
            // 遍历模式：强制关闭搜索，使用 MinCRF/MaxCRF
            if (opts.SweepMode)
            {
                config.SweepMode = true;
                config.UseCRFSearch = false;
            }
            if (!string.IsNullOrWhiteSpace(opts.Extensions))
                config.InputExtensions = opts.Extensions;
            if (opts.EncoderCustomParams != null)
                config.EncoderCustomParams = opts.EncoderCustomParams;
            if (opts.Denoise.HasValue)
                config.Denoise = opts.Denoise.Value;
            config.ArNrUseMaxFrames = opts.ArNrUseMaxFrames;
            config.RgbMode = opts.RgbMode;  // ""=关闭(默认), null=auto, "gbrp"等=强制
            // 降噪参数通过 EncoderCustomParams 拼接，值为 0 时不参与
            if (opts.Denoise.HasValue && opts.Denoise.Value > 0 && string.IsNullOrEmpty(opts.EncoderCustomParams))
            {
                string encParams = config.Encoder.StartsWith("libaom", StringComparison.OrdinalIgnoreCase)
                    ? $"-aom-params {config.AomParams}"
                    : config.Encoder.StartsWith("libsvtav1", StringComparison.OrdinalIgnoreCase)
                        ? "-svtav1-params \"tune=3:keyint=1:avif=1:film-grain=0:enable-qm=1:qm-min=0:qm-max=8\""
                        : "";
                if (config.Encoder.StartsWith("libaom", StringComparison.OrdinalIgnoreCase))
                {
                    if (opts.ArNrUseMaxFrames)
                        encParams += $":arnr-max-frames={Math.Clamp(opts.Denoise.Value, 1, 15)}:arnr-strength=4";
                    else
                    {
                        int s = Math.Clamp(opts.Denoise.Value, 1, 6);
                        int f = s <= 2 ? 3 : s <= 4 ? 7 : 15;
                        encParams += $":arnr-strength={s}:arnr-max-frames={f}";
                    }
                }
                else if (config.Encoder.StartsWith("libsvtav1", StringComparison.OrdinalIgnoreCase))
                {
                    int grain = Math.Clamp(opts.Denoise.Value, 1, 15);
                    int lastQ = encParams.LastIndexOf('"');
                    if (lastQ >= 0)
                        encParams = encParams.Insert(lastQ, $":film-grain={grain}:film-grain-denoise=1");
                    else
                        encParams += $" -svtav1-params \"film-grain={grain}:film-grain-denoise=1\"";
                }
                config.EncoderCustomParams = encParams;
            }
            else if (opts.Denoise.HasValue && opts.Denoise.Value > 0 && !string.IsNullOrEmpty(opts.EncoderCustomParams))
            {
                Console.Error.WriteLine("[WARN] --denoise 与 --enc-params 同时使用，降噪参数被自定义参数覆盖，请手动将降噪参数合并到 --enc-params 中");
            }
            if (opts.Resume)
                config.Resume = true;
            config.DryRun = opts.DryRun;
            config.Verbose = opts.Verbose;
            } // ★ 结束非 Lossless 分支，Lossless 和普通配置在此汇合进入统一校验

            // ★ 运行时校验配置，所有模式（含 Lossless）均在此校验
            if (opts.Overwrite && opts.NoClobber)
                throw new Exception("--overwrite 和 --no-clobber 不能同时使用");
            if (opts.Lossless && opts.SweepMode)
                throw new Exception("--lossless 和 --sweep 不能同时使用");
            if (opts.Lossless && (opts.EnableSearch || opts.DirectTargetValue.HasValue))
                Console.Error.WriteLine("[WARN] --lossless 模式下 CRF 固定为 0，搜索/质量目标参数将被忽略");

            var validationErrors = config.Validate();
            if (validationErrors.Count > 0)
            {
                throw new Exception(
                    $"配置校验失败:\n{string.Join("\n", validationErrors.Select(e => $"  - {e}"))}");
            }

            return config;
        }






        // ========== 主函数 ==========
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (EncoderUtils.FindExecutable("ffmpeg") == null)
            {
                Console.WriteLine("[FAIL] 错误: ffmpeg 未找到，请确认 ffmpeg 已安装并添加到 PATH 环境变量中。");
                Console.WriteLine("按任意键退出...");
                Console.ReadKey();
                return;
            }
            if (args.Length == 0)
            {
                PrintHelp();
                Console.WriteLine("\n正在自动检测环境（ffmpeg、编码器、外部工具）...");
                var cliLogger = new ConsoleLogger();
                await AvifEnvironmentChecker.CheckEnvironmentAsync(cliLogger);
                Console.WriteLine("\n请输入命令参数 (例如 -s -p best)");
                Console.Write("> ");
                string? line = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(line))
                    args = ParseCommandLineInteractive(line);
                else { Console.WriteLine("未输入参数，退出。"); Console.ReadKey(); return; }
            }
            ParsedOptions? opts = ParseCommandLineArgs(args);
            if (opts == null) return;
            if (opts.ShowVersion)
            {
                Console.WriteLine($"AVIF-Console v{AppVersion} (Linux-style CLI for Windows)");
                return;
            }
            // 路径校验
            if (string.IsNullOrWhiteSpace(opts.InputDir))
            {
                Console.WriteLine("[ERROR] 输入目录不能为空");
                return;
            }
            if (string.IsNullOrWhiteSpace(opts.OutputDir))
            {
                Console.WriteLine("[ERROR] 输出目录不能为空");
                return;
            }

            PresetConfig config = BuildPresetConfig(opts);
            if (opts.DryRun)
            {
                Console.WriteLine("== Dry Run ==");
                Console.WriteLine($"Input: {opts.InputDir}\nOutput: {opts.OutputDir}");
                Console.WriteLine($"Encoder: {config.Encoder}\nSearch: {config.UseCRFSearch}");
                Console.WriteLine($"Target: {config.GetEffectiveTarget()} (Metric: {config.MetricMode})");
                Console.WriteLine($"CRF: {config.BaseCRF}, Chroma: {opts.Chroma}, BitDepth: {config.BitDepth}");
                return;
            }
            uint originalMode = 0;
            IntPtr consoleHandle = IntPtr.Zero;
            bool quickEditDisabled = false;
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    consoleHandle = GetStdHandle(-10);
                    if (GetConsoleMode(consoleHandle, out originalMode))
                    {
                        const uint ENABLE_QUICK_EDIT = 0x0040;
                        uint newMode = originalMode & ~ENABLE_QUICK_EDIT;
                        SetConsoleMode(consoleHandle, newMode);
                        quickEditDisabled = true;
                    }
                }
                catch { }
            }
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

                if (opts.RecomputeMetrics)
                {
                    await pipeline.RunRecomputeMetricsAsync();
                }
                else
                {
                    await pipeline.RunAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] 错误: {ex.Message}"); try { string logDir = Path.Combine(opts.OutputDir, "log"); Directory.CreateDirectory(logDir); File.AppendAllText(Path.Combine(logDir, "crash.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [FATAL] {ex}\n"); } catch { }
            }
            finally
            {
                pipeline?.Dispose();
                if (quickEditDisabled && consoleHandle != IntPtr.Zero)
                {
                    try { SetConsoleMode(consoleHandle, originalMode); } catch { }
                }
                Console.WriteLine("按任意键退出...");
                Console.ReadKey();
            }
        }

        private static string[] ParseCommandLineInteractive(string line)
        {
            List<string> args = [];
            bool inQuotes = false;
            var current = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') inQuotes = !inQuotes;
                else if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (current.Length > 0) { args.Add(current.ToString()); current.Clear(); }
                }
                else current.Append(c);
            }
            if (current.Length > 0) args.Add(current.ToString());
            return [.. args];
        }
    }

    // ========== 简单控制台 Logger ==========
    internal class ConsoleLogger : ILogger
    {
        public void LogInfo(string msg) => Console.WriteLine(msg);
        public void LogError(string msg) => Console.WriteLine($"[ERROR] {msg}");
        public void LogMetric(string name, string msg) => Console.WriteLine($"[{name}] {msg}");
        public void LogSearch(string msg) => Console.WriteLine($"[SEARCH] {msg}");
    }
}