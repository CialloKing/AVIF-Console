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
        private const string AppVersion = "1.1";

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
AVIF 编码器 —— Linux 风格CLI命令行工具

用法:
  AvifEncoder --input <目录> --output <目录> [选项]
  AvifEncoder -i <目录> -o <目录> [选项]

支持的输入格式:
    "".jpg"", "".jpeg"", "".png"", "".webp"",
    "".bmp"", "".tif"", "".tiff"", "".gif"",
    "".jp2"", "".j2k"", "".jpx""

主要选项:
  -i, --input <目录>           输入目录 (默认: input)
  -o, --output <目录>          输出目录 (默认: Avifoutput)
  -p, --preset <预设>          预设模式: fast, balanced, best, extreme (默认: extreme)
  -e, --encoder <名称>         指定 AV1 编码器 (默认: libaom-av1)
  -j, --jobs <数量>            并行任务数 (默认: 根据 CPU 自动计算)

质量控制:
  -s, --search                 启用 CRF 搜索 (默认启用)
      --no-search              禁用 CRF 搜索
      --metric <模式>           质量评价模式: vmaf, ssim, psnr, msssim, mix, XPSNR, ssimu2, butter3, gmsd (默认 vmaf)
                               设置目标分数自动切换模式
      --target-vmaf <0-100>    直接设置 VMAF 目标
      --target-xpsnr <dB>      直接设置 XPSNR 加权综合分目标（默认 W‑XPSNR，配合 --metric xpsnr_y/u/v 可选择通道）
      --target-ssim <0-1>      直接设置 SSIM 目标
      --target-psnr <dB>       直接设置 PSNR-Y 目标 (典型 30-50)
      --target-msssim <0-1>    直接设置 MS-SSIM 目标
      --target-ssimu2 <值>     直接设置 SSIMULACRA2 目标（越大越好，通常取 0~100）
      --target-butter3 <值>    直接设置 Butteraugli 3‑norm 目标（越小越好，通常取 0~10）
      --target-gmsd <值>       直接设置 GMSD 目标（越小越好，通常取 0~1）
      --target-mix <0-1>       直接设置多指标加权混合评分目标


      --crf <整数>             手动指定固定 CRF (1-50，同时禁用搜索)
      --crf <最小值>:<最大值>  设置 CRF 搜索范围 (例如 10:50，自动启用搜索)

像素格式:
  -c, --chroma <采样>          色度采样: 420, 422, 444, auto (默认: auto)
  -b, --bit-depth <位数>       输出位深: 8 或 10

其他编码选项:
  -l, --lossless               无损模式 (有bug，不建议使用)
  -t, --output-template <模板> 输出文件名模板 (默认: covers-{index}.avif)
  -r, --recursive              递归处理子目录

      --serial-encode          极限压缩模式：强制单线程，关闭所有并行（tile/row-mt/内部线程）
                               仅保留 AV1 规范必须的瓦片分割（宽图自动分片）
                               以追求更高压缩率（编码速度会明显变慢）

      --search-cpu-used <0-13> 搜索阶段编码器速度（覆盖预设，默认使用预设值）
                               数值越高编码越快，评估精度下降。不同编码器含义：
                               libaom -cpu-used 0-8 (0最慢最高质)，
                               libsvtav1 -preset 0-13 (0最慢)，
                               librav1e --speed 0-10 (0最慢)
                               最终编码仍使用预设或自定义速度
      --final-cpu-used <0-13>  最终编码阶段编码器速度（覆盖预设，默认使用预设值）
                               数值含义同 --search-cpu-used，但仅影响最终输出文件的编码。
                               如果不指定，最终编码将使用预设的高质量速度（通常较慢）。

      --prior-search           启用概率分布先验引导搜索（中位数+哨兵，通常更快）
                               不启用的情况下默认使用标准二分搜索

      --max-resolution <像素>   预缩放：编码前将图片等比缩放，使长边不超过该值。
                               设为 0 则禁用预缩放，完全按原始分辨率编码（默认 0）。
                               开启后，搜索和质量评估也使用缩放后的图片。
                               若希望搜索用小图加速，但最终保留原图尺寸，需要加上 --output-full-res。

      --proxy                  启用保守代理搜索（需配合 --prior-search），快速评估中位数附近点来缩小区间
      --output-full-res        最终输出保留原始分辨率 (搜索和指标使用缩放后图像)

      --sweep                 遍历模式：对每张图片在 MinCRF～MaxCRF 范围内逐个编码并保存所有结果。
                              文件名自动附加 _CRF数字，CSV 包含完整统计数据
                              使用此选项可用于生成 RD 曲线数据，或分析不同 CRF 设置下的质量/文件大小关系

      --timeout-encode <分钟>  单次最终编码超时 (默认自动计算)
      --timeout-search <分钟>  搜索阶段全局超时 (默认 60)
      --timeout-safe <分钟>    安全模式全扫描超时 (默认 180)
      --timeout-safe-encode <分钟> 安全模式单次编码超时 (默认 10)
      --timeout-search-encode <分钟> 搜索过程中临时编码超时 (默认 10)
      --timeout-ssim <分钟>    SSIM 计算超时 (默认 5)

通用选项:
  -v, --verbose                详细输出
  -q, --quiet                  安静模式，仅输出错误
  -D, --dry-run                仅打印配置，不实际编码，用于验证命令行是否正确，或查看程序将如何执行
  -y, --overwrite              覆盖已存在的输出文件（默认行为是自动添加 _1 等后缀）
  -n, --no-clobber             已存在的文件，直接跳过
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
        }

        // ========== 参数解析 ==========
        private static ParsedOptions ParseCommandLineArgs(string[] args)
        {
            var opts = new ParsedOptions();
            int i = 0;
            while (i < args.Length)
            {
                string arg = args[i];
                if (arg == "--") { i++; break; }
                if (arg.StartsWith("--"))
                {
                    string key = arg.Substring(2);
                    string? value = null;
                    int eq = key.IndexOf('=');
                    if (eq >= 0) { value = key.Substring(eq + 1); key = key.Substring(0, eq); }
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
                        case "metric":
                            {
                                string raw = GetValue().ToLower();
                                opts.MetricMode = raw;
                                if (raw is "ssimu2" or "butter3" or "gmsd")
                                    opts.AdvancedMetricMode = raw;
                            }
                            break;
                        case "target-vmaf": opts.DirectTargetMode = "vmaf"; opts.DirectTargetValue = double.Parse(GetValue()); break;
                        case "target-ssim": opts.DirectTargetMode = "ssim"; opts.DirectTargetValue = double.Parse(GetValue()); break;
                        case "target-psnr": opts.DirectTargetMode = "psnr"; opts.DirectTargetValue = double.Parse(GetValue()); break;
                        case "target-msssim": opts.DirectTargetMode = "msssim"; opts.DirectTargetValue = double.Parse(GetValue()); break;
                        case "target-mix": opts.DirectTargetMode = "mix"; opts.DirectTargetValue = double.Parse(GetValue()); break;
                        case "target-ssimu2":
                            opts.DirectTargetMode = "ssimu2";
                            opts.AdvancedMetricMode = "ssimu2";
                            opts.DirectTargetValue = double.Parse(GetValue());
                            break;
                        case "target-butter3":
                            opts.DirectTargetMode = "butter3";
                            opts.AdvancedMetricMode = "butter3";
                            opts.DirectTargetValue = double.Parse(GetValue());
                            break;
                        case "target-gmsd":
                            opts.DirectTargetMode = "gmsd";
                            opts.AdvancedMetricMode = "gmsd";
                            opts.DirectTargetValue = double.Parse(GetValue());
                            break;
                        case "target-xpsnr":
                            if (!opts.MetricMode.StartsWith("xpsnr", StringComparison.OrdinalIgnoreCase))
                                opts.MetricMode = "xpsnr";
                            opts.QualityTarget = double.Parse(GetValue());
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
                if (arg.StartsWith('-') && arg.Length > 1 && !char.IsDigit(arg[1]))
                {
                    string flags = arg.Substring(1);
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
                return config;
            }
            config = AvifPipeline.CreateFromPreset(opts.Preset);
            config.Encoder = opts.Encoder;
            if (opts.ForceNoSearch) config.UseCRFSearch = false;
            else if (opts.EnableSearch) config.UseCRFSearch = true;
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
            if (opts.BitDepth.HasValue)
            {
                config.BitDepth = opts.BitDepth.Value;
                config.UserSetBitDepth = true;
                config.AutoSource = false;
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
                if (!opts.EnableSearch) config.UseCRFSearch = false;
            }
            if (opts.CrfMin.HasValue) config.MinCRF = opts.CrfMin.Value;
            if (opts.CrfMax.HasValue) config.MaxCRF = opts.CrfMax.Value;
            if (config.MinCRF >= config.MaxCRF) throw new Exception("最小 CRF 必须小于最大 CRF");
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
            PresetConfig config = BuildPresetConfig(opts);
            if (opts.DryRun)
            {
                Console.WriteLine("== Dry Run ==");
                Console.WriteLine($"Input: {opts.InputDir}\nOutput: {opts.OutputDir}");
                Console.WriteLine($"Encoder: {config.Encoder}\nSearch: {config.UseCRFSearch}");
                Console.WriteLine($"Target: {config.TargetSSIM} (Metric: {config.MetricMode})");
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
                await pipeline.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] 错误: {ex.Message}");
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
            var args = new List<string>();
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
            return args.ToArray();
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