using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using AvifEncoder;
using static AvifEncoder.PresetConfig;

namespace AvifEncoder.Gui
{
    public partial class Form1 : Form
    {
        // 防止程序设置控件值时触发“自定义”标记
        private bool _isApplyingPreset = false;
        // 预设对应选项文本（与 CliPreset 枚举 + 自定义）
        private const string CustomPresetName = "自定义";
        private readonly Dictionary<string, CliPreset?> _presetMap = new()
        {
            { CustomPresetName, null },
            { "fast", CliPreset.Fast },
            { "balanced", CliPreset.Balanced },
            { "best", CliPreset.Best },
            { "extreme", CliPreset.Extreme }
        };

        public Form1()
        {
            InitializeComponent();

            // ========== 初始化所有控件可选项 ==========
            cmbPreset.Items.Clear();
            cmbPreset.Items.AddRange(new[] { "fast", "balanced", "best", "extreme" });
            cmbPreset.SelectedIndex = 1;

            cmbEncoder.Items.Clear();
            cmbEncoder.Items.AddRange(new[] { "libaom-av1", "libsvtav1", "librav1e",
                                              "av1_nvenc", "av1_qsv", "av1_amf", "av1_vaapi" });
            cmbEncoder.SelectedItem = "libaom-av1";

            numJobs.Value = 0;
            cmbEncoder.SelectedIndex = 0;

            numSearchCpuUsed.Minimum = 0;
            numSearchCpuUsed.Maximum = 8;
            numSearchCpuUsed.Value = 4;
            numSearchCpuUsed.DecimalPlaces = 0;

            numFinalCpuUsed.Minimum = 0;
            numFinalCpuUsed.Maximum = 8;
            numFinalCpuUsed.Value = 0;
            numFinalCpuUsed.DecimalPlaces = 0;

            txtTemplate.Text = "covers-{index}.avif";

            cmbMetric.Items.Clear();
            cmbMetric.Items.AddRange(new[] { "vmaf", "xpsnr", "ssim", "psnr", "msssim", "mix",
                                             "ssimu2", "butter3", "gmsd" });
            cmbMetric.SelectedIndex = 0;

            cmbQualityMode.Items.Clear();
            cmbQualityMode.Items.AddRange(new[] { "无", "VMAF", "XPSNR", "SSIM", "PSNR-Y", "MS-SSIM",
                                                  "Mix", "SSIMULACRA2", "Butteraugli 3-norm", "GMSD" });
            cmbQualityMode.SelectedIndex = 0;
            numQualityValue.Minimum = 0;
            numQualityValue.Maximum = 1;
            numQualityValue.Value = 0.95m;
            numQualityValue.DecimalPlaces = 4;
            numQualityValue.Enabled = false;

            cmbChroma.Items.Clear();
            cmbChroma.Items.AddRange(new[] { "auto", "420", "422", "444" });
            cmbChroma.SelectedIndex = 0;

            cmbBitDepth.Items.Clear();
            cmbBitDepth.Items.AddRange(new[] { "auto", "8", "10" });
            cmbBitDepth.SelectedIndex = 0;

            numCrfFix.Minimum = 0; numCrfFix.Maximum = 63;
            numCrfMin.Minimum = 0; numCrfMin.Maximum = 63;
            numCrfMax.Minimum = 0; numCrfMax.Maximum = 63;
            numCrfFix.Enabled = true;
            numCrfMin.Enabled = false;
            numCrfMax.Enabled = false;
            rbCrfFix.Checked = true;

            cmbConflict.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbConflict.Items.Clear();
            cmbConflict.Items.AddRange(new[] { "自动重命名 (默认)", "覆盖已存在文件", "跳过已存在文件" });
            cmbConflict.SelectedIndex = 0;

            // 绑定原有事件
            chkLossless.CheckedChanged += chkLossless_CheckedChanged;
            cmbQualityMode.SelectedIndexChanged += cmbQualityMode_SelectedIndexChanged;
            rbCrfFix.CheckedChanged += rbCrfFix_CheckedChanged;
            rbCrfRange.CheckedChanged += rbCrfRange_CheckedChanged;

            // ========== 预设联动改造 ==========
            cmbPreset.Items.Clear();
            cmbPreset.Items.AddRange(new[] { CustomPresetName, "fast", "balanced", "best", "extreme" });
            cmbPreset.SelectedItem = "fast";
            ApplyPresetToUI(CliPreset.Fast);
            cmbPreset.SelectedIndexChanged += cmbPreset_SelectedIndexChanged;
            AttachCustomMarkEvents();

            AttachCustomMarkEvents();

            // 初始化遍历模式控件（需在设计器已添加名为 chkSweep 的 CheckBox）
            chkSweep.Checked = false;

            // 启动时异步检测编码器和外部工具，将结果输出到日志
            this.Load += async (s, e) => await PerformStartupCheckAsync();
        }

        private void ApplyPresetToUI(CliPreset preset)
        {
            _isApplyingPreset = true;
            try
            {
                var cfg = AvifPipeline.CreateFromPreset(preset);

                chkSearch.Checked = cfg.UseCRFSearch;
                if (cfg.UseCRFSearch)
                {
                    rbCrfRange.Checked = true;
                    numCrfMin.Value = cfg.MinCRF;
                    numCrfMax.Value = cfg.MaxCRF;
                }
                else
                {
                    rbCrfFix.Checked = true;
                    numCrfFix.Value = cfg.BaseCRF;
                }

                string chroma = "auto";
                if (!cfg.AutoSource && cfg.PixelFormat != null)
                {
                    if (cfg.PixelFormat.Contains("444")) chroma = "444";
                    else if (cfg.PixelFormat.Contains("422")) chroma = "422";
                    else chroma = "420";
                }
                cmbChroma.SelectedItem = chroma;
                cmbBitDepth.SelectedItem = cfg.BitDepth == 10 ? "10" : (cfg.AutoSource ? "auto" : "8");

                string metricMode = cfg.MetricMode ?? "vmaf";
                cmbMetric.SelectedItem = metricMode;
                if (!string.IsNullOrEmpty(metricMode))
                {
                    string qMode = metricMode switch
                    {
                        "vmaf" => "VMAF",
                        "ssim" => "SSIM",
                        "psnr" => "PSNR-Y",
                        "msssim" => "MS-SSIM",
                        "mix" => "Mix",
                        "xpsnr" => "XPSNR",
                        _ => "无"
                    };
                    cmbQualityMode.SelectedItem = qMode;

                    // 确保质量数值范围正确，防止越界
                    SetQualityValueRange(qMode);
                    double rawValue = metricMode switch
                    {
                        "vmaf" => cfg.TargetSSIM * 100.0,
                        "psnr" => cfg.TargetSSIM * 20 + 30,
                        _ => cfg.TargetSSIM
                    };
                    numQualityValue.Value = (decimal)rawValue;
                }

                chkLossless.Checked = cfg.Lossless;
                chkSerialEncode.Checked = cfg.SerialEncode;
                chkPriorSearch.Checked = cfg.UsePriorSearch;
                chkProxy.Checked = cfg.UseProxySearch;
                numSearchCpuUsed.Value = cfg.SearchCpuUsed;
                numFinalCpuUsed.Value = cfg.FinalCpuUsed;
                numJobs.Value = cfg.MaxJobs;
                chkSweep.Checked = false;     // 预设默认不启用遍历模式
            }
            finally { _isApplyingPreset = false; }
        }

        /// <summary>根据质量模式自动设置 numQualityValue 的有效范围，避免越界</summary>
        private void SetQualityValueRange(string mode)
        {
            switch (mode)
            {
                case "VMAF":
                    numQualityValue.Minimum = 0; numQualityValue.Maximum = 100;
                    numQualityValue.DecimalPlaces = 1; break;
                case "PSNR-Y":
                    numQualityValue.Minimum = 30; numQualityValue.Maximum = 50;
                    numQualityValue.DecimalPlaces = 1; break;
                case "XPSNR":
                    numQualityValue.Minimum = 40; numQualityValue.Maximum = 60;
                    numQualityValue.DecimalPlaces = 1; break;
                case "SSIMULACRA2":
                    numQualityValue.Minimum = -100; numQualityValue.Maximum = 100;
                    numQualityValue.DecimalPlaces = 2; break;
                case "Butteraugli 3-norm":
                    numQualityValue.Minimum = 0; numQualityValue.Maximum = 50;
                    numQualityValue.DecimalPlaces = 4; break;
                case "GMSD":
                    numQualityValue.Minimum = 0; numQualityValue.Maximum = 1;
                    numQualityValue.DecimalPlaces = 4; break;
                default:
                    numQualityValue.Minimum = 0; numQualityValue.Maximum = 1;
                    numQualityValue.DecimalPlaces = 4; break;
            }
            numQualityValue.Enabled = mode != "无";
        }

        private void MarkCustomPreset()
        {
            if (_isApplyingPreset) return;
            if (cmbPreset.SelectedItem?.ToString() == CustomPresetName) return;
            cmbPreset.SelectedItem = CustomPresetName;
        }

        private void AttachCustomMarkEvents()
        {
            cmbEncoder.SelectedIndexChanged += (s, e) => MarkCustomPreset();
            chkLossless.CheckedChanged += (s, e) => MarkCustomPreset();
            rbCrfFix.CheckedChanged += (s, e) => MarkCustomPreset();
            rbCrfRange.CheckedChanged += (s, e) => MarkCustomPreset();
            numCrfFix.ValueChanged += (s, e) => MarkCustomPreset();
            numCrfMin.ValueChanged += (s, e) => MarkCustomPreset();
            numCrfMax.ValueChanged += (s, e) => MarkCustomPreset();
            chkSearch.CheckedChanged += (s, e) => MarkCustomPreset();
            cmbChroma.SelectedIndexChanged += (s, e) => MarkCustomPreset();
            cmbBitDepth.SelectedIndexChanged += (s, e) => MarkCustomPreset();
            cmbMetric.SelectedIndexChanged += (s, e) => MarkCustomPreset();
            cmbQualityMode.SelectedIndexChanged += (s, e) => MarkCustomPreset();
            numQualityValue.ValueChanged += (s, e) => MarkCustomPreset();
            chkSerialEncode.CheckedChanged += (s, e) => MarkCustomPreset();
            chkPriorSearch.CheckedChanged += (s, e) => MarkCustomPreset();
            chkProxy.CheckedChanged += (s, e) => MarkCustomPreset();
            numSearchCpuUsed.ValueChanged += (s, e) => MarkCustomPreset();
            numFinalCpuUsed.ValueChanged += (s, e) => MarkCustomPreset();
            numJobs.ValueChanged += (s, e) => MarkCustomPreset();
            chkSweep.CheckedChanged += (s, e) => MarkCustomPreset();
        }

        private void btnBrowseInput_Click(object? sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog();
            if (dlg.ShowDialog() == DialogResult.OK)
                txtInput.Text = dlg.SelectedPath;
        }

        private void btnBrowseOutput_Click(object? sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog();
            if (dlg.ShowDialog() == DialogResult.OK)
                txtOutput.Text = dlg.SelectedPath;
        }

        private void chkLossless_CheckedChanged(object? sender, EventArgs e)
        {
            bool isLossless = chkLossless.Checked;
            _isApplyingPreset = true;
            try
            {
                chkSearch.Enabled = !isLossless;
                grpCrfMode.Enabled = !isLossless;
                if (isLossless)
                {
                    chkSearch.Checked = false;
                    rbCrfFix.Checked = true;
                    numCrfFix.Value = 0;
                }
            }
            finally { _isApplyingPreset = false; }
            MarkCustomPreset();
        }

        private void cmbQualityMode_SelectedIndexChanged(object? sender, EventArgs e)
        {
            string? mode = cmbQualityMode.SelectedItem?.ToString();
            if (mode == null) return;

            SetQualityValueRange(mode);

            // 根据模式设置默认质量值（用户手动切换时触发）
            switch (mode)
            {
                case "VMAF": numQualityValue.Value = 95; break;
                case "PSNR-Y": numQualityValue.Value = 40; break;
                case "XPSNR": numQualityValue.Value = 45; break;
                case "SSIMULACRA2": numQualityValue.Value = 90; break;
                case "Butteraugli 3-norm": numQualityValue.Value = 1; break;
                case "GMSD": numQualityValue.Value = 0.2m; break;
                default: numQualityValue.Value = 0.95m; break;
            }

            // 联动：搜索度量自动跟随目标类型
            if (mode != "无")
            {
                string metricMode = mode.ToLower() switch
                {
                    "vmaf" => "vmaf",
                    "ssim" => "ssim",
                    "psnr-y" => "psnr",
                    "ms-ssim" => "msssim",
                    "mix" => "mix",
                    "xpsnr" => "xpsnr",
                    "ssimulacra2" => "ssimu2",
                    "butteraugli 3-norm" => "butter3",
                    "gmsd" => "gmsd",
                    _ => ""
                };
                if (!string.IsNullOrEmpty(metricMode))
                    cmbMetric.SelectedItem = metricMode;
            }
        }

        private void rbCrfFix_CheckedChanged(object? sender, EventArgs e)
        {
            numCrfFix.Enabled = rbCrfFix.Checked;
            numCrfMin.Enabled = numCrfMax.Enabled = !rbCrfFix.Checked;
        }

        private void rbCrfRange_CheckedChanged(object? sender, EventArgs e)
        {
            numCrfMin.Enabled = numCrfMax.Enabled = rbCrfRange.Checked;
            numCrfFix.Enabled = !rbCrfRange.Checked;
        }

        private async void btnStart_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtInput.Text) || string.IsNullOrWhiteSpace(txtOutput.Text))
            {
                MessageBox.Show("请输入输入和输出目录");
                return;
            }

            var config = new PresetConfig();
            config.Encoder = cmbEncoder.SelectedItem?.ToString() ?? "libaom-av1";

            int jobs = (int)numJobs.Value;
            if (jobs > 0) { config.MaxJobs = jobs; config.UserSpecifiedMaxJobs = true; }

            config.OutputNameFormat = string.IsNullOrWhiteSpace(txtTemplate.Text)
                ? "covers-{index}.avif"
                : txtTemplate.Text.Trim();
            config.RecurseSubdirectories = chkRecursive.Checked;
            config.Lossless = chkLossless.Checked;

            config.UseCRFSearch = chkSearch.Checked;
            if (rbCrfFix.Checked)
            {
                config.BaseCRF = (int)numCrfFix.Value;
            }
            else
            {
                config.MinCRF = (int)numCrfMin.Value;
                config.MaxCRF = (int)numCrfMax.Value;
                config.UseCRFSearch = true;
            }

            string chroma = cmbChroma.SelectedItem?.ToString()?.ToLower() ?? "auto";
            if (chroma != "auto")
            {
                config.AutoSource = false;
                config.UserSetChroma = true;
                config.PixelFormat = chroma switch
                {
                    "420" => "yuv420p",
                    "422" => "yuv422p",
                    "444" => "yuv444p",
                    _ => "yuv420p"
                };
            }

            string? bitDepthStr = cmbBitDepth.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(bitDepthStr) && bitDepthStr != "auto" && int.TryParse(bitDepthStr, out int bit))
            {
                config.BitDepth = bit;
                config.UserSetBitDepth = true;
                config.AutoSource = false;
                AvifPipeline.ApplyBitDepth(config);
            }

            config.MetricMode = cmbMetric.SelectedItem?.ToString()?.ToLower() ?? "vmaf";

            string? qualityMode = cmbQualityMode.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(qualityMode) && qualityMode != "无")
            {
                double rawValue = (double)numQualityValue.Value;
                string metricMode = qualityMode.ToLower() switch
                {
                    "vmaf" => "vmaf",
                    "ssim" => "ssim",
                    "psnr-y" => "psnr",
                    "ms-ssim" => "msssim",
                    "mix" => "mix",
                    "xpsnr" => "xpsnr",
                    "ssimulacra2" => "ssimu2",
                    "butteraugli 3-norm" => "butter3",
                    "gmsd" => "gmsd",
                    _ => "vmaf"
                };
                config.MetricMode = metricMode;
                config.SetQualityTarget(rawValue, metricMode);
            }
            else
            {
                config.AdjustTargetForMetricMode();
            }

            config.MaxResolution = (int)numMaxRes.Value;
            config.ApplyScalingToOutput = !chkOutputFullRes.Checked;

            config.SerialEncode = chkSerialEncode.Checked;
            config.UsePriorSearch = chkPriorSearch.Checked;
            config.UseProxySearch = chkProxy.Checked;
            config.SearchCpuUsed = (int)numSearchCpuUsed.Value;
            config.FinalCpuUsed = (int)numFinalCpuUsed.Value;
            // 遍历模式（自动从 MinCRF 到 MaxCRF 逐个编码）
            config.SweepMode = chkSweep.Checked;

            config.FileConflictStrategy = cmbConflict.SelectedIndex switch
            {
                1 => PresetConfig.ConflictStrategy.Overwrite,
                2 => PresetConfig.ConflictStrategy.Skip,
                _ => PresetConfig.ConflictStrategy.Rename
            };

            SetControlsEnabled(false);
            progressBar1.Style = ProgressBarStyle.Marquee;
            progressBar1.Value = 0;

            try
            {
                ILogger fileLogger = new FileLogger(txtOutput.Text, new PresetConfig.RealFileSystem());
                ILogger guiLogger = new GuiLogger(rtbLog);          // GuiLogger 应在单独文件中定义
                ILogger logger = new CompositeLogger(fileLogger, guiLogger);

                IProgress<int> progress = new Progress<int>(percent =>
                {
                    if (progressBar1.InvokeRequired)
                        progressBar1.Invoke((Action)(() => UpdateProgress(percent)));
                    else
                        UpdateProgress(percent);
                });

                using var pipeline = new AvifPipeline(
                    txtInput.Text, txtOutput.Text, config,
                    logger: logger,
                    processRunner: new RealProcessRunner(),
                    fileSystem: new PresetConfig.RealFileSystem(),
                    cacheManager: new CacheManager(),
                    progress: progress);

                await pipeline.RunAsync();

                AppendLog("===== 全部完成 =====");
                MessageBox.Show("转换完成！", "信息", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AppendLog($"严重错误: {ex.Message}");
                MessageBox.Show($"处理过程中发生未预期的错误:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetControlsEnabled(true);
                progressBar1.Style = ProgressBarStyle.Blocks;
            }
        }

        private void SetControlsEnabled(bool enabled)
        {
            btnStart.Enabled = enabled;
            cmbPreset.Enabled = enabled;
            cmbEncoder.Enabled = enabled;
        }

        private void AppendLog(string message)
        {
            if (rtbLog.InvokeRequired)
                rtbLog.Invoke((Action)(() => rtbLog.AppendText(message + Environment.NewLine)));
            else
                rtbLog.AppendText(message + Environment.NewLine);
        }

        private void UpdateProgress(int percent)
        {
            if (progressBar1.Style != ProgressBarStyle.Blocks)
                progressBar1.Style = ProgressBarStyle.Blocks;
            progressBar1.Value = Math.Min(percent, 100);
        }

        private void cmbPreset_SelectedIndexChanged(object? sender, EventArgs e)
        {
            string? select = cmbPreset.SelectedItem?.ToString();
            if (select == null || select == CustomPresetName) return;
            if (_presetMap.TryGetValue(select, out var preset) && preset.HasValue)
                ApplyPresetToUI(preset.Value);
        }

        // ========== 帮助文本 ==========
        private void AppendHelpText()
        {
            AppendLog(@"AVIF 编码器 —— Linux 风格CLI命令行工具

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
");
            AppendLog("========== GUI 控件对照表 ==========");
            AppendLog(" 输入/输出目录   -> 文本框 txtInput / txtOutput");
            AppendLog(" 预设模式         -> 下拉框 cmbPreset");
            AppendLog(" ...（其余控件对照同前）...");
            AppendLog("====================================");
        }

        // ========== 启动时环境检测（已重构） ==========
        private async Task PerformStartupCheckAsync()
        {
            AppendHelpText();
            AppendLog("===== 启动检测 =====");

            // GuiLogger 现在位于单独文件，直接使用
            var guiLogger = new GuiLogger(rtbLog);
            await AvifEnvironmentChecker.CheckEnvironmentAsync(guiLogger);

            AppendLog("===== 启动检测完成 =====");
        }

        // 以下空事件处理器保留，避免设计器报错
        private void label1_Click(object sender, EventArgs e) { }
        private void lblOutput_Click(object sender, EventArgs e) { }
        private void progressBar1_Click(object sender, EventArgs e) { }
        private void radioButton1_CheckedChanged(object sender, EventArgs e) { }
        private void label8_Click(object sender, EventArgs e) { }
        private void label5_Click(object sender, EventArgs e) { }
        private void label13_Click(object sender, EventArgs e) { }
        private void label11_Click(object sender, EventArgs e) { }
        private void numSearchCpuUsed_ValueChanged(object sender, EventArgs e) { }
    }

    /// <summary>
    /// CompositeLogger 保留在此文件（无冲突），GuiLogger 已移至独立文件。
    /// </summary>
    public class CompositeLogger : ILogger
    {
        private readonly ILogger[] _loggers;
        public CompositeLogger(params ILogger[] loggers)
        {
            _loggers = loggers ?? Array.Empty<ILogger>();
        }

        public void LogInfo(string message)
        {
            foreach (var logger in _loggers) logger.LogInfo(message);
        }
        public void LogError(string message)
        {
            foreach (var logger in _loggers) logger.LogError(message);
        }
        public void LogMetric(string metric, string message)
        {
            foreach (var logger in _loggers) logger.LogMetric(metric, message);
        }
        public void LogSearch(string message)
        {
            foreach (var logger in _loggers) logger.LogSearch(message);
        }
    }
}