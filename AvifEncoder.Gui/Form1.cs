using static AvifEncoder.PresetConfig;

namespace AvifEncoder.Gui
{
    public partial class Form1 : Form
    {
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
            txtTemplate.Text = "covers-{index}.avif";
            cmbMetric.Items.Clear();
            cmbMetric.Items.AddRange(new[] { "vmaf", "ssim", "psnr", "msssim", "mix" });
            cmbMetric.SelectedIndex = 0;
            cmbQualityMode.Items.Clear();
            cmbQualityMode.Items.AddRange(new[] { "无", "VMAF", "SSIM", "PSNR-Y", "MS-SSIM", "Mix" });
            cmbQualityMode.SelectedIndex = 0;
            numQualityValue.Minimum = 0;
            numQualityValue.Maximum = 1;
            numQualityValue.Value = 0.95m;
            numQualityValue.DecimalPlaces = 4;
            cmbChroma.Items.Clear();
            cmbChroma.Items.AddRange(new[] { "auto", "420", "422", "444" });
            cmbChroma.SelectedIndex = 0;
            cmbBitDepth.Items.Clear();
            cmbBitDepth.Items.AddRange(new[] { "auto", "8", "10" });
            cmbBitDepth.SelectedIndex = 0; // 默认选 auto
            numCrfFix.Enabled = true;
            numCrfMin.Enabled = false;
            numCrfMax.Enabled = false;
            rbCrfFix.Checked = true;
            // 绑定事件    
            chkLossless.CheckedChanged += chkLossless_CheckedChanged;
            cmbQualityMode.SelectedIndexChanged += cmbQualityMode_SelectedIndexChanged;
            rbCrfFix.CheckedChanged += rbCrfFix_CheckedChanged;
            rbCrfRange.CheckedChanged += rbCrfRange_CheckedChanged;
            btnStart.Click += btnStart_Click;
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
            chkSearch.Enabled = !isLossless;
            grpCrfMode.Enabled = !isLossless;
            if (isLossless)
            {
                chkSearch.Checked = false;
                rbCrfFix.Checked = true;
                numCrfFix.Value = 0;
            }
        }

        // 在 Form1.cs 中定义（可放在 Form1 类内部或单独文件）
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

            // 如果 ILogger 还有其他方法（如 LogDebug、LogWarning 等），也按相同模式添加
            // 例如：
            // public void LogDebug(string message) => foreach (var l in _loggers) l.LogDebug(message);
        }
        private void cmbQualityMode_SelectedIndexChanged(object? sender, EventArgs e)
        {
            string? mode = cmbQualityMode.SelectedItem?.ToString();
            if (mode == null) return;
            switch (mode)
            {
                case "VMAF":
                    numQualityValue.Minimum = 0;
                    numQualityValue.Maximum = 100;
                    numQualityValue.Value = 95;
                    numQualityValue.DecimalPlaces = 1;
                    break;
                case "PSNR-Y":
                    numQualityValue.Minimum = 30;
                    numQualityValue.Maximum = 50;
                    numQualityValue.Value = 40;
                    numQualityValue.DecimalPlaces = 1;
                    break;
                case "无":
                    numQualityValue.Value = 0;
                    numQualityValue.Enabled = false;
                    return;
                default: // SSIM / MS-SSIM / Mix
                    numQualityValue.Minimum = 0;
                    numQualityValue.Maximum = 1;
                    numQualityValue.Value = 0.95m;
                    numQualityValue.DecimalPlaces = 4;
                    break;
            }
            numQualityValue.Enabled = true;
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
            // 1. 验证必填路径
            if (string.IsNullOrWhiteSpace(txtInput.Text) || string.IsNullOrWhiteSpace(txtOutput.Text))
            {
                MessageBox.Show("请输入输入和输出目录");
                return;
            }

            // 2. 基于预设创建基础配置
            string? presetStr = cmbPreset.SelectedItem?.ToString() ?? "balanced";
            var preset = presetStr switch
            {
                "fast" => CliPreset.Fast,
                "balanced" => CliPreset.Balanced,
                "best" => CliPreset.Best,
                "extreme" => CliPreset.Extreme,
                _ => CliPreset.Balanced
            };
            var config = AvifPipeline.CreateFromPreset(preset);

            // ==================== 3. 覆盖所有用户设置 ====================
            // 编码器
            config.Encoder = cmbEncoder.SelectedItem?.ToString() ?? "libaom-av1";

            // 并行任务数
            int jobs = (int)numJobs.Value;
            if (jobs > 0)
            {
                config.MaxJobs = jobs;
                config.UserSpecifiedMaxJobs = true;
            }

            // 输出模板
            config.OutputNameFormat = string.IsNullOrWhiteSpace(txtTemplate.Text)
                ? "covers-{index}.avif"
                : txtTemplate.Text.Trim();

            // 递归
            config.RecurseSubdirectories = chkRecursive.Checked;

            // 搜索开关
            config.UseCRFSearch = chkSearch.Checked;

            // 度量模式
            config.MetricMode = cmbMetric.SelectedItem?.ToString()?.ToLower() ?? "vmaf";

            // 质量目标
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
                    _ => "vmaf"
                };
                config.MetricMode = metricMode;
                config.SetQualityTarget(rawValue, metricMode);
            }
            else
            {
                config.AdjustTargetForMetricMode(); // 使用预设自动调整
            }

            // CRF 模式
            if (!chkLossless.Checked)
            {
                if (rbCrfFix.Checked)
                {
                    config.BaseCRF = (int)numCrfFix.Value;
                    config.UseCRFSearch = false;
                }
                else
                {
                    config.MinCRF = (int)numCrfMin.Value;
                    config.MaxCRF = (int)numCrfMax.Value;
                    config.UseCRFSearch = true;
                }
            }

            // 色度采样
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

            // 位深：仅当选择具体值（非auto）时才覆盖，否则保持自适应
            string? bitDepthStr = cmbBitDepth.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(bitDepthStr) && bitDepthStr != "auto" && int.TryParse(bitDepthStr, out int bit))
            {
                config.BitDepth = bit;
                config.UserSetBitDepth = true;
                config.AutoSource = false;
                AvifPipeline.ApplyBitDepth(config);
            }
            // 选择 auto 时不修改，完全依赖预设的自适应行为

            // 无损
            config.Lossless = chkLossless.Checked;
            if (config.Lossless)
            {
                config.BaseCRF = 0;
                config.TargetSSIM = 1.0;
                config.UseCRFSearch = false;
            }

            // 最大分辨率
            config.MaxResolution = (int)numMaxRes.Value;
            config.ApplyScalingToOutput = !chkOutputFullRes.Checked;

            // 高级选项
            config.SerialEncode = chkSerialEncode.Checked;
            config.UsePriorSearch = chkPriorSearch.Checked;
            config.UseProxySearch = chkProxy.Checked;

            // ==================== 4. 创建日志（界面+文件） ====================
            var guiLogger = new GuiLogger(rtbLog);
            ILogger logger = guiLogger; // 默认只在界面显示
            try
            {
                // 尝试添加文件日志器（若失败则仅用界面日志）
                var fileLogger = new FileLogger(txtOutput.Text.Trim());
                logger = new CompositeLogger(guiLogger, fileLogger);
            }
            catch (Exception ex)
            {
                guiLogger.LogInfo($"无法创建文件日志器，将仅在界面显示日志: {ex.Message}");
            }
            Logger.SetInstance(logger);

            // 界面状态更新
            btnStart.Enabled = false;
            progressBar1.Style = ProgressBarStyle.Marquee;

            try
            {
                // 干运行模式
                if (chkDryRun.Checked)
                {
                    logger.LogInfo("===== Dry Run =====");
                    logger.LogInfo($"Input: {txtInput.Text}");
                    logger.LogInfo($"Output: {txtOutput.Text}");
                    logger.LogInfo($"Encoder: {config.Encoder}, Search: {config.UseCRFSearch}");
                    logger.LogInfo($"CRF: {config.BaseCRF}, PixFmt: {config.PixelFormat}, BitDepth: {config.BitDepth}");
                    logger.LogInfo($"Target: {config.TargetSSIM} (Metric: {config.MetricMode})");
                    logger.LogInfo($"MaxResolution: {config.MaxResolution}, OutputFullRes: {!config.ApplyScalingToOutput}");
                    logger.LogInfo($"Serial: {config.SerialEncode}, PriorSearch: {config.UsePriorSearch}, Proxy: {config.UseProxySearch}");
                    return;
                }

                // 实际运行流水线
                var pipeline = new AvifPipeline(
                    txtInput.Text, txtOutput.Text, config,
                    logger: logger,
                    processRunner: new RealProcessRunner(),
                    fileSystem: new RealFileSystem(),
                    cacheManager: new CacheManager());

                await Task.Run(() => pipeline.RunAsync());
                logger.LogInfo("全部完成！");
            }
            catch (Exception ex)
            {
                logger.LogError($"异常: {ex.Message}");
            }
            finally
            {
                btnStart.Enabled = true;
                progressBar1.Style = ProgressBarStyle.Blocks;
                progressBar1.Value = 0;
            }
        }
        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void lblOutput_Click(object sender, EventArgs e)
        {

        }


        



        







        private void progressBar1_Click(object sender, EventArgs e)
        {

        }

        private void cmbPreset_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void label8_Click(object sender, EventArgs e)
        {

        }
    }
}
