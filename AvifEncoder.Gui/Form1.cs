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
        private AvifPipeline? _pipeline;
        private CancellationTokenSource? _globalCts;
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
            // 绑定窗口关闭事件
            this.FormClosing += Form1_FormClosing;   // ← 新增绑定
        }
        private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            _pipeline?.Dispose();
        }
        private void ApplyPresetToUI(CliPreset preset)
        {
            _isApplyingPreset = true;
            try
            {
                var cfg = PresetConfig.CreateFromPreset(preset);

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

                // 创建取消令牌供外部使用
                _globalCts = new CancellationTokenSource();
                // 注意：AvifPipeline 内部会创建自己的 _globalCts，我们可以用外部令牌取消
                // 但更好的做法是从外部控制。这里我们只在关闭时通知 pipeline 取消。
                // 我们直接将此令牌传入 pipeline 构造函数？目前构造函数未暴露令牌。
                // 简便：记录 pipeline 实例，关闭时调用其 Dispose（会触发取消）。
                _pipeline = new AvifPipeline(
                    txtInput.Text, txtOutput.Text, config,
                    logger: logger,
                    processRunner: new RealProcessRunner(),
                    fileSystem: new PresetConfig.RealFileSystem(),
                    cacheManager: new CacheManager(),
                    progress: progress);

                try
                {
                    await _pipeline.RunAsync();
                }
                finally
                {
                    _pipeline?.Dispose();
                    _pipeline = null;
                }

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
            AppendLog("\n===== 命令行帮助文本 ====="
                + HelpText.CliHelp
                + HelpText.GuiControlTable);
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



}