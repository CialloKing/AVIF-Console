using System.Diagnostics;
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

            // ========== 1. 初始化所有控件可选项（原有代码，必须保留） ==========
            cmbPreset.Items.Clear();                         // ★ 先清空，后面会重新添加
            cmbPreset.Items.AddRange(new[] { "fast", "balanced", "best", "extreme" }); // 临时占位，后续会被覆盖
            cmbPreset.SelectedIndex = 1;

            cmbEncoder.Items.Clear();
            cmbEncoder.Items.AddRange(new[] { "libaom-av1", "libsvtav1", "librav1e",
                                      "av1_nvenc", "av1_qsv", "av1_amf", "av1_vaapi" });
            cmbEncoder.SelectedItem = "libaom-av1";

            numJobs.Value = 0;
            cmbEncoder.SelectedIndex = 0;
            // 设置设计器中已存在的 numSearchCpuUsed 控件属性
            numSearchCpuUsed.Minimum = 0;
            numSearchCpuUsed.Maximum = 8;
            numSearchCpuUsed.Value = 4;          // 默认值，后续 ApplyPresetToUI 会刷新
            numSearchCpuUsed.DecimalPlaces = 0;
            // 如果设计器中已有关联的 Label（例如“搜索速度”），无需再添加

            txtTemplate.Text = "covers-{index}.avif";

            cmbMetric.Items.Clear();
            cmbMetric.Items.AddRange(new[] { "vmaf", "xpsnr", "ssim", "psnr", "msssim", "mix",
                                 "ssimu2", "butter3", "gmsd" });   // ★ 新增高级指标
            cmbMetric.SelectedIndex = 0;

            cmbQualityMode.Items.Clear();
            cmbQualityMode.Items.AddRange(new[] { "无", "VMAF", "XPSNR", "SSIM", "PSNR-Y", "MS-SSIM",
                                      "Mix", "SSIMULACRA2", "Butteraugli 3-norm", "GMSD" });  // ★ 新增
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
            cmbBitDepth.SelectedIndex = 0;

            // CRF 数值控件的合法范围（预设可能用到 0‑63）
            numCrfFix.Minimum = 0; numCrfFix.Maximum = 63;
            numCrfMin.Minimum = 0; numCrfMin.Maximum = 63;
            numCrfMax.Minimum = 0; numCrfMax.Maximum = 63;

            numCrfFix.Enabled = true;
            numCrfMin.Enabled = false;
            numCrfMax.Enabled = false;
            rbCrfFix.Checked = true;

            cmbConflict.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbConflict.Items.Clear();
            cmbConflict.Items.AddRange(new[]
            {
        "自动重命名 (默认)",
        "覆盖已存在文件",
        "跳过已存在文件"
    });
            cmbConflict.SelectedIndex = 0;

            // 绑定原有事件
            chkLossless.CheckedChanged += chkLossless_CheckedChanged;
            cmbQualityMode.SelectedIndexChanged += cmbQualityMode_SelectedIndexChanged;
            rbCrfFix.CheckedChanged += rbCrfFix_CheckedChanged;
            rbCrfRange.CheckedChanged += rbCrfRange_CheckedChanged;
            btnStart.Click += btnStart_Click;

            // ========== 2. 预设联动改造 ==========
            // 重新设置 cmbPreset（覆盖上面的临时设置）
            cmbPreset.Items.Clear();
            cmbPreset.Items.AddRange(new[] { CustomPresetName, "fast", "balanced", "best", "extreme" });
            cmbPreset.SelectedItem = "fast";

            // 将 balanced 预设值填充到界面
            ApplyPresetToUI(CliPreset.Fast);

            // 绑定预设选择事件
            cmbPreset.SelectedIndexChanged += cmbPreset_SelectedIndexChanged;

            // 给所有可能被用户手动修改的控件挂上“标记自定义”事件
            AttachCustomMarkEvents();

            // ★ 新增：启动时异步检测编码器和外部工具，将结果输出到日志
            this.Load += async (s, e) => await PerformStartupCheckAsync();
        }





        private void ApplyPresetToUI(CliPreset preset)
        {
            _isApplyingPreset = true;
            try
            {
                var cfg = AvifPipeline.CreateFromPreset(preset);

                // 编码器（预设不修改编码器，保留用户选择）
                // cmbEncoder.SelectedItem = ...  // 预设本来不包含编码器字段，这里可以不管

                // CRF / 搜索
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

                // 色度采样：预设使用了 PixelFormat，需转换为 combo 值
                string chroma = "auto";
                if (!cfg.AutoSource)
                {
                    if (cfg.PixelFormat != null)
                    {
                        if (cfg.PixelFormat.Contains("444")) chroma = "444";
                        else if (cfg.PixelFormat.Contains("422")) chroma = "422";
                        else chroma = "420";
                    }
                    else chroma = "auto";
                }
                cmbChroma.SelectedItem = chroma;

                // 位深
                cmbBitDepth.SelectedItem = cfg.BitDepth == 10 ? "10" : (cfg.AutoSource ? "auto" : "8");

                // 质量目标（预设的 TargetSSIM 和 MetricMode）
                string metricMode = cfg.MetricMode ?? "vmaf";
                cmbMetric.SelectedItem = metricMode;   // 搜索度量模式
                                                       // 质量目标模式下拉框
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
                    double rawValue = metricMode switch
                    {
                        "vmaf" => cfg.TargetSSIM * 100.0,
                        "psnr" => cfg.TargetSSIM * 20 + 30,
                        _ => cfg.TargetSSIM
                    };
                    numQualityValue.Value = (decimal)rawValue;
                }

                // 无损
                chkLossless.Checked = cfg.Lossless;

                // 其他高级选项（预设默认不启用这些）
                chkSerialEncode.Checked = cfg.SerialEncode;
                chkPriorSearch.Checked = cfg.UsePriorSearch;
                chkProxy.Checked = cfg.UseProxySearch;


                // 搜索阶段 CPU used (速度)
                numSearchCpuUsed.Value = cfg.SearchCpuUsed;
                // 并行任务数
                numJobs.Value = cfg.MaxJobs;
            }
            finally
            {
                _isApplyingPreset = false;
            }
        }
        private void MarkCustomPreset()
        {
            if (_isApplyingPreset) return;                     // 程序设置跳过
            if (cmbPreset.SelectedItem?.ToString() == CustomPresetName) return; // 已经是自定义

            cmbPreset.SelectedItem = CustomPresetName;
        }

        // 为所有相关控件挂上事件
        private void AttachCustomMarkEvents()
        {
            // 编码器
            cmbEncoder.SelectedIndexChanged += (s, e) => MarkCustomPreset();
            // 无损
            chkLossless.CheckedChanged += (s, e) => MarkCustomPreset();
            // CRF 相关
            rbCrfFix.CheckedChanged += (s, e) => MarkCustomPreset();
            rbCrfRange.CheckedChanged += (s, e) => MarkCustomPreset();
            numCrfFix.ValueChanged += (s, e) => MarkCustomPreset();
            numCrfMin.ValueChanged += (s, e) => MarkCustomPreset();
            numCrfMax.ValueChanged += (s, e) => MarkCustomPreset();
            // 搜索选项
            chkSearch.CheckedChanged += (s, e) => MarkCustomPreset();
            // 色度 / 位深
            cmbChroma.SelectedIndexChanged += (s, e) => MarkCustomPreset();
            cmbBitDepth.SelectedIndexChanged += (s, e) => MarkCustomPreset();
            // 质量目标
            cmbMetric.SelectedIndexChanged += (s, e) => MarkCustomPreset();
            cmbQualityMode.SelectedIndexChanged += (s, e) => MarkCustomPreset();
            numQualityValue.ValueChanged += (s, e) => MarkCustomPreset();
            // 高级并行/极限
            chkSerialEncode.CheckedChanged += (s, e) => MarkCustomPreset();
            chkPriorSearch.CheckedChanged += (s, e) => MarkCustomPreset();
            chkProxy.CheckedChanged += (s, e) => MarkCustomPreset();
            // 搜索速度
            numSearchCpuUsed.ValueChanged += (s, e) => MarkCustomPreset();
            // 并行任务数
            numJobs.ValueChanged += (s, e) => MarkCustomPreset();
            // 其他可能影响编码质量的选项可按需添加
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
            // 原有的逻辑：禁用搜索、CRF 等
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
            finally
            {
                _isApplyingPreset = false;
            }

            // 标记自定义（除非正在应用预设）
            MarkCustomPreset();
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
                case "XPSNR":
                    numQualityValue.Minimum = 40;
                    numQualityValue.Maximum = 60;
                    numQualityValue.Value = 45;
                    numQualityValue.DecimalPlaces = 1;
                    break;
                case "SSIMULACRA2":                 // ★ 新增
                    numQualityValue.Minimum = -100;  // SSIMU2 可能为负值，范围宽泛
                    numQualityValue.Maximum = 100;
                    numQualityValue.Value = 90;
                    numQualityValue.DecimalPlaces = 2;
                    break;
                case "Butteraugli 3-norm":         // ★ 新增
                    numQualityValue.Minimum = 0;
                    numQualityValue.Maximum = 50;    // 通常小于 10，留出余量
                    numQualityValue.Value = 1;
                    numQualityValue.DecimalPlaces = 4;
                    break;
                case "GMSD":                       // ★ 新增
                    numQualityValue.Minimum = 0;
                    numQualityValue.Maximum = 1;
                    numQualityValue.Value = 0.2m;
                    numQualityValue.DecimalPlaces = 4;
                    break;
                default: // SSIM / MS-SSIM / Mix
                    numQualityValue.Minimum = 0;
                    numQualityValue.Maximum = 1;
                    numQualityValue.Value = 0.95m;
                    numQualityValue.DecimalPlaces = 4;
                    break;
            }
            numQualityValue.Enabled = true;

            // ========== 联动：搜索度量自动跟随目标类型 ==========
            // ========== 联动：搜索度量自动跟随目标类型 ==========
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
                    "ssimulacra2" => "ssimu2",         // ★ 新增
                    "butteraugli 3-norm" => "butter3", // ★ 新增
                    "gmsd" => "gmsd",                  // ★ 新增
                    _ => ""
                };
                if (!string.IsNullOrEmpty(metricMode))
                {
                    cmbMetric.SelectedItem = metricMode;
                }
            }
            // ==================================================
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
            // 验证必填路径
            if (string.IsNullOrWhiteSpace(txtInput.Text) || string.IsNullOrWhiteSpace(txtOutput.Text))
            {
                MessageBox.Show("请输入输入和输出目录");
                return;
            }

            // 直接从 UI 构建配置
            var config = new PresetConfig();

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

            // 无损
            config.Lossless = chkLossless.Checked;

            // CRF / 搜索
            config.UseCRFSearch = chkSearch.Checked;
            if (rbCrfFix.Checked)
            {
                config.BaseCRF = (int)numCrfFix.Value;
            }
            else
            {
                config.MinCRF = (int)numCrfMin.Value;
                config.MaxCRF = (int)numCrfMax.Value;
                config.UseCRFSearch = true;  // 范围模式下强制搜索
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

            // 位深
            string? bitDepthStr = cmbBitDepth.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(bitDepthStr) && bitDepthStr != "auto" && int.TryParse(bitDepthStr, out int bit))
            {
                config.BitDepth = bit;
                config.UserSetBitDepth = true;
                config.AutoSource = false;
                AvifPipeline.ApplyBitDepth(config);
            }

            // 度量模式（搜索用）
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
                    "xpsnr" => "xpsnr",
                    "ssimulacra2" => "ssimu2",         // ★ 新增
                    "butteraugli 3-norm" => "butter3", // ★ 新增
                    "gmsd" => "gmsd",                  // ★ 新增
                    _ => "vmaf"
                };
                config.MetricMode = metricMode;
                config.SetQualityTarget(rawValue, metricMode);
            }
            else
            {
                config.AdjustTargetForMetricMode();
            }

            // 最大分辨率
            config.MaxResolution = (int)numMaxRes.Value;
            config.ApplyScalingToOutput = !chkOutputFullRes.Checked;

            // 高级选项
            config.SerialEncode = chkSerialEncode.Checked;
            config.UsePriorSearch = chkPriorSearch.Checked;
            config.UseProxySearch = chkProxy.Checked;
            config.SearchCpuUsed = (int)numSearchCpuUsed.Value;   // ★ 新增

            // 冲突策略
            config.FileConflictStrategy = cmbConflict.SelectedIndex switch
            {
                1 => PresetConfig.ConflictStrategy.Overwrite,
                2 => PresetConfig.ConflictStrategy.Skip,
                _ => PresetConfig.ConflictStrategy.Rename
            };

            // ========== 以下是新增的运行与异常处理部分 ==========
            SetControlsEnabled(false);
            progressBar1.Style = ProgressBarStyle.Marquee;
            progressBar1.Value = 0;

            try
            {
                ILogger fileLogger = new FileLogger(txtOutput.Text, new PresetConfig.RealFileSystem());
                GuiLogger guiLogger = new GuiLogger(rtbLog);
                ILogger logger = new CompositeLogger(fileLogger, guiLogger);

                IProgress<int> progress = new Progress<int>(percent =>
                {
                    // 切换到非 Marquee 模式并更新进度
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
            // 可根据需要继续添加其他控件，例如：
            // chkLossless.Enabled = enabled;
            // numCrfFix.Enabled = enabled;
            // ...
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
        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void lblOutput_Click(object sender, EventArgs e)
        {

        }














        private void progressBar1_Click(object sender, EventArgs e)
        {

        }

        private void cmbPreset_SelectedIndexChanged(object? sender, EventArgs e)
        {
            string? select = cmbPreset.SelectedItem?.ToString();
            if (select == null || select == CustomPresetName)
                return;

            if (_presetMap.TryGetValue(select, out var preset) && preset.HasValue)
            {
                ApplyPresetToUI(preset.Value);
            }
        }

        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void label8_Click(object sender, EventArgs e)
        {

        }

        private void label5_Click(object sender, EventArgs e)
        {

        }





        // ========== 启动时编码器与外部工具检测 ==========
        private async Task PerformStartupCheckAsync()
        {
            AppendLog("===== 启动检测 =====");

            // 1. 检测 ffmpeg
            string? ffmpegPath = EncoderUtils.FindExecutable("ffmpeg");
            if (ffmpegPath == null)
            {
                AppendLog("[FAIL] 错误: ffmpeg 未找到，请确保 ffmpeg 在 PATH 或程序目录中。");
                return;
            }
            AppendLog("[OK] ffmpeg 已找到");

            // 2. 获取支持的 AV1 编码器列表
            var encoders = await GetAvailableEncodersListAsync();
            AppendLog($"检测到 AV1 编码器: {string.Join(", ", encoders)}");

            // 3. 测试每个编码器的实际可用性
            AppendLog("正在测试编码器可用性...");
            string testDir = Path.Combine(Path.GetTempPath(), "avif_gui_test");
            string testInput = Path.Combine(testDir, "test_input.bmp");
            Directory.CreateDirectory(testDir);
            byte[] testBmp = CreateTestBmp();
            File.WriteAllBytes(testInput, testBmp);

            var results = new List<(string name, bool available, string note)>();
            foreach (var enc in encoders)
            {
                var (name, avail, note) = await TestEncoderAsync(enc, testInput, testDir);
                results.Add((name, avail, note));
            }

            // 输出编码器测试结果
            AppendLog("编码器可用性测试结果:");
            AppendLog("----------------------------------------");
            var availList = results.Where(r => r.available).ToList();
            var unavailList = results.Where(r => !r.available).ToList();
            if (availList.Any())
            {
                AppendLog("[可用的编码器]");
                var softAvail = availList.Where(r => r.name.StartsWith("lib")).ToList();
                var hardAvail = availList.Where(r => !r.name.StartsWith("lib")).ToList();
                if (softAvail.Any())
                {
                    AppendLog("  -- 软件编码器（推荐） --");
                    foreach (var (name, _, _) in softAvail)
                        AppendLog($"  [OK] {name,-12}");
                }
                if (hardAvail.Any())
                {
                    AppendLog("  -- 硬件编码器 --");
                    foreach (var (name, _, _) in hardAvail)
                        AppendLog($"  [OK] {name,-12}");
                }
            }
            if (unavailList.Any())
            {
                AppendLog("\n[不可用的编码器]");
                foreach (var (name, _, note) in unavailList)
                    AppendLog($"  [FAIL] {name,-12} ({note})");
            }
            AppendLog("----------------------------------------");
            AppendLog("提示: 同一编码器可能因图片格式/尺寸在运行时降级或回退，属正常保护机制。");

            // 4. 检测高级指标外部工具
            // 4. 检测高级指标外部工具（名称不带扩展名，由 FindExecutable 自动补全）
            AppendLog("\n高级指标工具可用性检测");
            AppendLog("----------------------------------------");
            bool hasSsimu2 = EncoderUtils.FindExecutable("ssimulacra2") != null;
            bool hasButter = EncoderUtils.FindExecutable("butteraugli_main") != null;
            AppendLog($"  SSIMULACRA2: {(hasSsimu2 ? "[OK] 已找到" : "[FAIL] 未找到")} (ssimulacra2.exe)");
            AppendLog($"  Butteraugli: {(hasButter ? "[OK] 已找到" : "[FAIL] 未找到")} (butteraugli_main.exe)");
            AppendLog("未找到的工具无法计算相应的指标，请不要设置为目标质量");
            AppendLog("\n----------------------------------------");
            if (!hasSsimu2 || !hasButter)
            {
                AppendLog("提示: 将 ssimulacra2.exe / butteraugli_main.exe 放到程序所在目录或 PATH 中即可使对应指标可用。");
            }

            AppendLog("===== 启动检测完成 =====");

            // 清理临时文件
            try { if (File.Exists(testInput)) File.Delete(testInput); } catch { }
            if (Directory.Exists(testDir) && !Directory.EnumerateFileSystemEntries(testDir).Any())
                Directory.Delete(testDir);
        }

        // 从 ffmpeg 获取支持的 AV1 编码器列表
        private async Task<List<string>> GetAvailableEncodersListAsync()
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

        // 测试单个编码器是否可用
        private async Task<(string name, bool available, string note)> TestEncoderAsync(
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

        // 解析 ffmpeg 的常见错误信息
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

        // 生成 256x256 纯红色 BMP（测试用）
        private static byte[] CreateTestBmp()
        {
            int width = 256, height = 256;
            int rowSize = ((width * 3 + 3) / 4) * 4;
            int pixelDataSize = rowSize * height;
            int fileSize = 54 + pixelDataSize;

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            bw.Write((ushort)0x4D42);
            bw.Write(fileSize);
            bw.Write(0);
            bw.Write(54);

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

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bw.Write((byte)0x00); // B
                    bw.Write((byte)0x00); // G
                    bw.Write((byte)0xFF); // R
                }
                for (int p = width * 3; p < rowSize; p++)
                    bw.Write((byte)0);
            }

            bw.Flush();
            return ms.ToArray();
        }

        private void label13_Click(object sender, EventArgs e)
        {

        }

        private void label11_Click(object sender, EventArgs e)
        {

        }

        private void numSearchCpuUsed_ValueChanged(object sender, EventArgs e)
        {

        }
    }
}
