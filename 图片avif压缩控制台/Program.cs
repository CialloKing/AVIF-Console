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






    class Program
    {
        // 在 Program 类顶部
        private const string AppVersion = "1.0";


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

      --serial-encode          极限压缩模式：强制单线程，关闭所有并行（tile/row-mt/内部线程）
                               仅保留 AV1 规范必须的瓦片分割（宽图自动分片）
                               以追求更高压缩率（编码速度会明显变慢）

      --prior-search           启用概率分布先验引导搜索（中位数+哨兵，通常更快）
                               不启用的情况下默认使用标准二分搜索

      --max-resolution <像素>   预缩放：编码前将图片等比缩放，使长边不超过该值。
                               设为 0 则禁用预缩放，完全按原始分辨率编码（默认 0）。
                               开启后，搜索和质量评估也使用缩放后的图片。
                               若希望搜索用小图加速，但最终保留原图尺寸，需要加上 --output-full-res。

      --output-full-res        最终输出保留原始分辨率（仅搜索/指标使用缩放后图）。
      --proxy                  启用保守代理搜索（需配合 --prior-search），快速评估中位数附近点来缩小区间
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
            public bool Overwrite = false;                     // -y / --overwrite
            public bool NoClobber = false;                     // -n / --no-clobber
            public bool SerialEncode { get; set; } = false;

            // 在 private class ParsedOptions 中添加
            public bool EnableProxySearch { get; set; } = false;

            public bool UsePriorSearch { get; set; } = false;
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
                        case "overwrite": opts.Overwrite = true; break;    // ★ 新增
                        case "no-clobber": opts.NoClobber = true; break;   // ★ 新增 -n 长选项
                        case "help": PrintHelp(); return null!;
                        case "prior-search": opts.UsePriorSearch = true; break;
                        case "serial-encode":
                            opts.SerialEncode = true;
                            break;
                        case "proxy":
                            opts.EnableProxySearch = true;
                            break;
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
                            case 'y': opts.Overwrite = true; break;       // ★ 新增
                            case 'n': opts.NoClobber = true; break;        // ★ 新增
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

            //---------- 11. 代理搜索模式 ----------
            config.UseProxySearch = opts.EnableProxySearch;

            //---------- 12. 极限压缩模式（单线程，禁用所有并行） ----------
            config.SerialEncode = opts.SerialEncode;

            //---------- 13. 先验搜索模式 ----------
            config.UsePriorSearch = opts.UsePriorSearch;

            //---------- 14. 冲突策略 ----------
            if (opts.Overwrite)
                config.FileConflictStrategy = PresetConfig.ConflictStrategy.Overwrite;
            else if (opts.NoClobber)
                config.FileConflictStrategy = PresetConfig.ConflictStrategy.Skip;
            // 默认保持 Rename

            return config;
        }

        // ========== 程序入口 ==========
        // ========== 修复后的 Main 方法 ==========
        // ========== 修复后的 Main 方法（支持交互模式引号路径） ==========
        // ========== 修复后的 Main 方法（引号解析 + 自动禁用/恢复快速编辑）==========
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            // 快速编辑模式不再通过 -e 控制，改为自动管理（见下方）

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
                    args = ParseCommandLineInteractive(line);   // ★ 引号感知分割
                else
                { Console.WriteLine("未输入参数，退出。"); Console.ReadKey(); return; }
            }

            // 解析参数
            ParsedOptions? opts = ParseCommandLineArgs(args);
            if (opts == null) return;

            // 显示版本
            if (opts.ShowVersion)
            {
                Console.WriteLine($"AVIF-Console v{AppVersion} (Linux-style CLI for Windows)");
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

            // ========== 控制台快速编辑管理：运行前禁用，结束后恢复 ==========
            uint originalMode = 0;
            IntPtr consoleHandle = IntPtr.Zero;
            bool quickEditDisabled = false;

            if (OperatingSystem.IsWindows())
            {
                try
                {
                    consoleHandle = GetStdHandle(-10);   // STD_INPUT_HANDLE
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

                // 恢复控制台模式
                if (quickEditDisabled && consoleHandle != IntPtr.Zero)
                {
                    try { SetConsoleMode(consoleHandle, originalMode); } catch { }
                }

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