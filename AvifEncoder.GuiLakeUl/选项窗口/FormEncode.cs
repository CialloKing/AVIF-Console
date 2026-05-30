using AvifEncoder;
using LakeUI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static AvifEncoder.PresetConfig;

namespace AvifEncoder.GuiLakeUl.选项窗口
{
    public partial class FormEncode : Form
    {
        private const string CustomPresetName = "自定义";
        private bool _isApplyingPreset;
        private readonly Dictionary<string, CliPreset?> _presetMap = new()
        {
            { CustomPresetName, null },
            { "fast", CliPreset.Fast },
            { "balanced", CliPreset.Balanced },
            { "best", CliPreset.Best },
            { "extreme", CliPreset.Extreme }
        };

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public FormLog? LogPage { get; set; }

        /// <summary>缓存顶层 Form1 句柄用于任务栏进度（子 Form 的 Handle 不被任务栏识别）</summary>
        private IntPtr _topLevelHandle;
        public void SetTopLevelHandle(IntPtr h) => _topLevelHandle = h;

        private bool _isEncoding;
        private CancellationTokenSource? _cts;
        private bool _sweepPreviousCrfRangeMode;
        private bool _isResumeDetected;

        public FormEncode()
        {
            InitializeComponent();
            progressBar1.Maximum = 100;
            progressBar1.Value = 0;
            btnStop.Enabled = false;

            InitializeAllControls();
            ApplyPresetToUI(CliPreset.Balanced);
            SetComboBoxItem(cmbPreset, "balanced");
            AttachAllEvents();

            SetupDragDrop();   // ← 新增此行
            this.FormClosing += FormEncode_FormClosing;
        }

        private void SetupDragDrop()
        {
            // 为输入路径文本框启用拖放
            txtInput.AllowDrop = true;
            txtInput.DragEnter += TxtPath_DragEnter;
            txtInput.DragDrop += TxtPath_DragDrop;

            // 为输出路径文本框启用拖放
            txtOutput.AllowDrop = true;
            txtOutput.DragEnter += TxtPath_DragEnter;
            txtOutput.DragDrop += TxtPath_DragDrop;
        }

        private void TxtPath_DragEnter(object? sender, DragEventArgs e)
        {
            // 仅当拖放的是文件夹时显示复制光标
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length == 1 && Directory.Exists(files[0]))
                    e.Effect = DragDropEffects.Copy;
                else
                    e.Effect = DragDropEffects.None;
            }
            else
                e.Effect = DragDropEffects.None;
        }

        private void TxtPath_DragDrop(object? sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length == 1 && Directory.Exists(files[0]))
                {
                    // 修改点：使用 Control 基类设置路径，兼容自定义控件
                    if (sender is Control control)
                        control.Text = files[0];
                    // 或者使用：((dynamic)sender).Text = files[0];  但建议用 Control
                }
            }
        }

        /// <summary>辅助：根据字符串设置 ModernComboBox 选中项</summary>
        private void SetComboBoxItem(ModernComboBox combo, string item)
        {
            int idx = combo.Items.IndexOf(item);
            combo.SelectedIndex = idx >= 0 ? idx : -1;
        }

        private void InitializeAllControls()
        {
            cmbPreset.Items.Clear();
            cmbPreset.Items.AddRange(new string[] { CustomPresetName, "fast", "balanced", "best", "extreme" });

            cmbEncoder.Items.Clear();
            cmbEncoder.Items.AddRange(new string[] { "libaom-av1", "libsvtav1", "librav1e",
                                                      "av1_nvenc", "av1_qsv", "av1_amf", "av1_vaapi" });
            SetComboBoxItem(cmbEncoder, "libaom-av1");
            UpdateCpuUsedLimits();   // 添加此行，使初始上限与编码器匹配

            numJobs.Minimum = 0; numJobs.Maximum = 128; numJobs.Value = 0;

            numSearchCpuUsed.Minimum = 0; numSearchCpuUsed.Maximum = 8;
            numSearchCpuUsed.Value = 4; numSearchCpuUsed.DecimalPlaces = 0;

            numFinalCpuUsed.Minimum = 0; numFinalCpuUsed.Maximum = 8;
            numFinalCpuUsed.Value = 0; numFinalCpuUsed.DecimalPlaces = 0;

            txtTemplate.Text = "covers-{index}.avif";

            // 模板预设下拉框
            cmbTemplate.Items.Clear();
            cmbTemplate.Items.AddRange(new[]
            {
                // 简洁模式
                "covers-{index}.avif",
                "{name}.avif",
                "{index:000}_{name}.avif",
                // 含编码参数
                "{name}_{crf}.avif",
                "{name}_{encoder}_crf{crf}.avif",
                "{name}_{encoder}_crf{crf}_s{speed}.avif",
                "{name}_{encoder}_crf{crf}_{pixfmt}.avif",
                "{name}_{encoder}_crf{crf}_s{speed}_{pixfmt}.avif",
                // 目录归档
                "{date}/{name}_{crf}.avif",
                "{dir}/{name}.avif",
                // 特殊模式
                "{name}_lossless.avif",
                "{name}_{bitdepth}bit.avif",
                "自定义..."
            });
            cmbTemplate.SelectedIndex = 0;

            numCrfFix.Minimum = 0; numCrfFix.Maximum = 63;
            numCrfMin.Minimum = 0; numCrfMin.Maximum = 63;
            numCrfMax.Minimum = 0; numCrfMax.Maximum = 63;
            rbCrfFix.Checked = true;
            chkSearch.Checked = false;

            cmbMetric.Items.Clear();
            cmbMetric.Items.AddRange(MetricRegistry.AllKeys.ToArray());
            cmbMetric.SelectedIndex = 0;

            cmbQualityMode.Items.Clear();
            cmbQualityMode.Items.Add("无");
            cmbQualityMode.Items.AddRange(MetricRegistry.AllKeys
                .Select(k => MetricRegistry.Get(k)?.DisplayName ?? k)
                .Where(n => n.Length > 0).ToArray());
            cmbQualityMode.SelectedIndex = 0;
            numQualityValue.Minimum = 0; numQualityValue.Maximum = 1;
            numQualityValue.Value = 0.95; numQualityValue.DecimalPlaces = 4;
            numQualityValue.Enabled = false;

            cmbChroma.Items.Clear();
            cmbChroma.Items.AddRange(new string[] { "auto", "420", "422", "444" });
            cmbChroma.SelectedIndex = 0;

            cmbBitDepth.Items.Clear();
            cmbBitDepth.Items.AddRange(new string[] { "auto", "8", "10" });
            cmbBitDepth.SelectedIndex = 0;

            chkLossless.Checked = false;
            chkRecursive.Checked = false;
            numMaxRes.Minimum = 0; numMaxRes.Maximum = 10000; numMaxRes.Value = 0;
            chkOutputFullRes.Checked = false;
            cmbConflict.Items.Clear();
            cmbConflict.Items.AddRange(new string[] { "自动重命名", "覆盖已存在文件", "跳过已存在文件" });
            cmbConflict.SelectedIndex = 0;
            chkSerialEncode.Checked = false;
            chkPriorSearch.Checked = false;
            chkProxy.Checked = false;
            // 遍历模式开关
            chkSweep.Checked = false;

            // 恢复任务按钮（初始可见但不可用，检测到中断时才启用）
            btnResume.Visible = true;
            btnResume.Enabled = false;
            btnAbandon.Visible = true;
            btnAbandon.Enabled = false;
        }

        private void AttachAllEvents()
        {
            cmbPreset.SelectedIndexChanged += CmbPreset_SelectedIndexChanged;
            AttachCustomMarkEvents();
            cmbQualityMode.SelectedIndexChanged += CmbQualityMode_SelectedIndexChanged;
            chkLossless.CheckedChanged += ChkLossless_CheckedChanged;
            rbCrfFix.CheckedChanged += (s, e) =>
            {
                numCrfFix.Enabled = rbCrfFix.Checked;
                numCrfMin.Enabled = numCrfMax.Enabled = !rbCrfFix.Checked;
            };
            rbCrfRange.CheckedChanged += (s, e) =>
            {
                numCrfMin.Enabled = numCrfMax.Enabled = rbCrfRange.Checked;
                numCrfFix.Enabled = !rbCrfRange.Checked;
            };
            chkSweep.CheckedChanged += ChkSweep_CheckedChanged;
            btnResume.Click += BtnResume_Click;
            btnAbandon.Click += BtnAbandon_Click;
            txtOutput.TextChanged += TxtOutput_TextChanged;
        }

        private void AttachCustomMarkEvents()
        {
            cmbEncoder.SelectedIndexChanged += (s, e) => { MarkCustom(s, e); UpdateCpuUsedLimits(); };
            numJobs.ValueChanged += MarkCustom;
            numSearchCpuUsed.ValueChanged += MarkCustom;
            numFinalCpuUsed.ValueChanged += MarkCustom;
            txtTemplate.TextChanged += MarkCustom;

            // cmbTemplate 选择 → 同步到 txtTemplate
            cmbTemplate.SelectedIndexChanged += (s, e) =>
            {
                if (cmbTemplate.SelectedIndex >= 0 &&
                    cmbTemplate.SelectedIndex < cmbTemplate.Items.Count - 1)
                {
                    txtTemplate.Text = cmbTemplate.SelectedItem?.ToString() ?? "";
                }
            };

            // 用户手动改 txtTemplate → 下拉自动切到"自定义..."
            txtTemplate.TextChanged += (s, e) =>
            {
                string current = txtTemplate.Text.Trim();
                bool found = false;
                for (int i = 0; i < cmbTemplate.Items.Count - 1; i++)
                {
                    if (string.Equals(cmbTemplate.Items[i].ToString(), current,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        if (cmbTemplate.SelectedIndex != i)
                            cmbTemplate.SelectedIndex = i;
                        found = true;
                        break;
                    }
                }
                if (!found && cmbTemplate.SelectedIndex != cmbTemplate.Items.Count - 1)
                    cmbTemplate.SelectedIndex = cmbTemplate.Items.Count - 1;
            };
            chkSearch.CheckedChanged += MarkCustom;
            rbCrfFix.CheckedChanged += MarkCustom;
            rbCrfRange.CheckedChanged += MarkCustom;
            numCrfFix.ValueChanged += MarkCustom;
            numCrfMin.ValueChanged += MarkCustom;
            numCrfMax.ValueChanged += MarkCustom;
            cmbMetric.SelectedIndexChanged += MarkCustom;
            cmbQualityMode.SelectedIndexChanged += MarkCustom;
            numQualityValue.ValueChanged += MarkCustom;
            cmbChroma.SelectedIndexChanged += MarkCustom;
            cmbBitDepth.SelectedIndexChanged += MarkCustom;
            chkLossless.CheckedChanged += MarkCustom;
            chkRecursive.CheckedChanged += MarkCustom;
            numMaxRes.ValueChanged += MarkCustom;
            chkOutputFullRes.CheckedChanged += MarkCustom;
            cmbConflict.SelectedIndexChanged += MarkCustom;
            chkSerialEncode.CheckedChanged += MarkCustom;
            chkPriorSearch.CheckedChanged += MarkCustom;
            chkProxy.CheckedChanged += MarkCustom;
            chkSweep.CheckedChanged += MarkCustom;
        }

        private void MarkCustom(object? sender, EventArgs e)
        {
            if (_isApplyingPreset)
            {
                return;
            }
            if (cmbPreset.SelectedIndex < 0
                || cmbPreset.SelectedIndex >= cmbPreset.Items.Count)
            {
                return;
            }
            if (cmbPreset.Items[cmbPreset.SelectedIndex]?.ToString()
                == CustomPresetName)
            {
                return;
            }
            SetComboBoxItem(cmbPreset, CustomPresetName);
        }

        // ------------------------------------------------
        // 关键修复：设置 numQualityValue 的有效范围
        // ------------------------------------------------
        private void SetQualityRange(string mode)
        {
            switch (mode)
            {
                case "VMAF":
                    numQualityValue.Minimum = 0; numQualityValue.Maximum = 100;
                    numQualityValue.DecimalPlaces = 15;
                    numQualityValue.Increment = 0.1;
                    break;
                case "PSNR-Y":
                    numQualityValue.Minimum = 30; numQualityValue.Maximum = 50;
                    numQualityValue.DecimalPlaces = 15;
                    numQualityValue.Increment = 0.1;
                    break;
                case "XPSNR":
                    numQualityValue.Minimum = 40; numQualityValue.Maximum = 60;
                    numQualityValue.DecimalPlaces = 15;
                    numQualityValue.Increment = 0.1;
                    break;
                case "SSIMULACRA2":
                    numQualityValue.Minimum = -100; numQualityValue.Maximum = 100;
                    numQualityValue.DecimalPlaces = 15;
                    numQualityValue.Increment = 1;
                    break;
                case "Butteraugli 3-norm":
                    numQualityValue.Minimum = 0; numQualityValue.Maximum = 50;
                    numQualityValue.DecimalPlaces = 15;
                    numQualityValue.Increment = 0.01;
                    break;
                case "GMSD":
                    numQualityValue.Minimum = 0; numQualityValue.Maximum = 1;
                    numQualityValue.DecimalPlaces = 15;
                    numQualityValue.Increment = 0.001;
                    break;
                // case "CAMBI": ...  // 暂不可用
                // case "ADM": ...    // 暂不可用
                default:
                    numQualityValue.Minimum = 0; numQualityValue.Maximum = 1;
                    numQualityValue.DecimalPlaces = 15;
                    numQualityValue.Increment = 0.001;
                    break;
            }
            numQualityValue.Enabled = mode != "无";
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
                SetComboBoxItem(cmbChroma, chroma);
                SetComboBoxItem(cmbBitDepth, cfg.BitDepth == 10 ? "10" : (cfg.AutoSource ? "auto" : "8"));

                string metricMode = cfg.MetricMode ?? "vmaf";
                SetComboBoxItem(cmbMetric, metricMode);

                // --- 质量目标 ---
                if (!string.IsNullOrEmpty(metricMode))
                {
                    string qMode = MetricRegistry.Get(metricMode)?.DisplayName ?? "无";
                    SetComboBoxItem(cmbQualityMode, qMode);
                    // ★ 手动同步范围，防止 combo 事件延迟导致越界
                    SetQualityRange(qMode);

                    double rawVal = metricMode switch
                    {
                        "vmaf" => cfg.TargetSSIM * 100.0,
                        "psnr" => cfg.TargetSSIM * 20 + 30,
                        _ => cfg.TargetSSIM
                    };
                    // 安全调整控件范围（Maximum / Minimum 为 double 类型）
                    // 安全赋值（均为 double 类型）
                    if (rawVal > numQualityValue.Maximum)
                        numQualityValue.Maximum = rawVal;
                    if (rawVal < numQualityValue.Minimum)
                        numQualityValue.Minimum = rawVal;
                    numQualityValue.Value = rawVal;
                }

                chkLossless.Checked = cfg.Lossless;
                chkSerialEncode.Checked = cfg.SerialEncode;
                chkPriorSearch.Checked = cfg.UsePriorSearch;
                chkProxy.Checked = cfg.UseProxySearch;
                numSearchCpuUsed.Value = cfg.SearchCpuUsed;
                numFinalCpuUsed.Value = cfg.FinalCpuUsed;
                numJobs.Value = cfg.MaxJobs;
                chkSweep.Checked = false;
                UpdateSweepControlsState(chkSweep.Checked);
            }
            finally { _isApplyingPreset = false; }
        }

        private void CmbPreset_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_isApplyingPreset) return;
            string? sel = cmbPreset.Items[cmbPreset.SelectedIndex]?.ToString();
            if (sel == null || sel == CustomPresetName) return;
            if (_presetMap.TryGetValue(sel, out var preset) && preset.HasValue)
            {
                ApplyPresetToUI(preset.Value);
            }
        }

        private void CmbQualityMode_SelectedIndexChanged(object? sender, EventArgs e)
        {
            string? mode = cmbQualityMode.Items[cmbQualityMode.SelectedIndex]?.ToString();
            if (mode == null) return;

            SetQualityRange(mode);
            // 根据模式设置默认值（仅当手动切换时使用）
            numQualityValue.Value = mode switch
            {
                "VMAF" => 95,
                "PSNR-Y" => 40,
                "XPSNR" => 45,
                "SSIMULACRA2" => 90,
                "Butteraugli 3norm" => 1,
                "GMSD" => 0.2,
                // "CAMBI" => 2,    // 暂不可用
                // "ADM" => 1,      // 暂不可用
                _ => 0.95,
            };
            // 通过注册表反向查找 → 自动同步搜索度量
            var def = MetricRegistry.AllKeys
                .Select(k => MetricRegistry.Get(k))
                .FirstOrDefault(d => d != null &&
                    string.Equals(d.DisplayName, mode, StringComparison.OrdinalIgnoreCase));
            if (def != null)
                SetComboBoxItem(cmbMetric, def.Key);
        }

        private void ChkLossless_CheckedChanged(object? sender, EventArgs e)
        {
            _isApplyingPreset = true;
            try
            {
                chkSearch.Enabled = !chkLossless.Checked;
                grpCrfMode.Enabled = !chkLossless.Checked;
                if (chkLossless.Checked)
                {
                    chkSearch.Checked = false;
                    rbCrfFix.Checked = true;
                    numCrfFix.Value = 0;
                }
            }
            finally { _isApplyingPreset = false; }
            // 无损模式勾选时自动关闭遍历模式
            if (chkLossless.Checked && chkSweep.Checked)
                chkSweep.Checked = false;
            MarkCustom(sender, e);
        }

        /// <summary> 根据遍历模式开关状态更新相关控件的启用/禁用 </summary>
        private void UpdateSweepControlsState(bool sweepEnabled)
        {
            bool lossless = chkLossless.Checked;
            chkSearch.Enabled = !lossless && !sweepEnabled;
            grpCrfMode.Enabled = !lossless && !sweepEnabled;

            if (sweepEnabled)
            {
                chkSearch.Checked = false;
                // 强制切换到范围模式（若还没切）
                if (!rbCrfRange.Checked)
                {
                    numCrfMin.Value = numCrfFix.Value;
                    numCrfMax.Value = numCrfFix.Value;
                    rbCrfRange.Checked = true;
                }
            }
        }

        private void UpdateCpuUsedLimits()
        {
            string? encoder = cmbEncoder.SelectedItem?.ToString();
            int maxCpu = encoder switch
            {
                "libsvtav1" => 13,
                "librav1e" => 10,
                "libaom-av1" => 8,
                _ => 8 // 硬件编码器等默认仍为 8，后续可针对性禁用控件
            };
            numSearchCpuUsed.Maximum = maxCpu;
            numFinalCpuUsed.Maximum = maxCpu;

            // 当前值若超出新上限则强制拉回
            if (numSearchCpuUsed.Value > maxCpu)
                numSearchCpuUsed.Value = maxCpu;
            if (numFinalCpuUsed.Value > maxCpu)
                numFinalCpuUsed.Value = maxCpu;
        }

        private void ChkSweep_CheckedChanged(object? sender, EventArgs e)
        {
            if (_isApplyingPreset) return;

            if (chkSweep.Checked)
            {
                if (chkLossless.Checked)
                {
                    MessageBox.Show("无损模式下无法使用遍历模式。", "提示",
                                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                    chkSweep.Checked = false;
                    return;
                }

                // 记录原CRF模式，供取消遍历时恢复
                _sweepPreviousCrfRangeMode = rbCrfRange.Checked;
                chkSweep.Text = "遍历模式 (搜索已禁用)";
            }
            else
            {
                chkSweep.Text = "遍历模式 (--sweep)";
                // 恢复切换前的CRF模式（若上次记录为固定模式）
                if (!_sweepPreviousCrfRangeMode)
                    rbCrfFix.Checked = true;
            }

            UpdateSweepControlsState(chkSweep.Checked);
            MarkCustom(sender, e);
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

        private async void btnStart_Click(object? sender, EventArgs e)
        {
            string inputDir = txtInput.Text.Trim('"').Trim();
            string outputDir = txtOutput.Text.Trim('"').Trim();
            if (string.IsNullOrWhiteSpace(inputDir) || string.IsNullOrWhiteSpace(outputDir))
            {
                MessageBox.Show("请输入输入和输出目录", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_isEncoding)
            {
                MessageBox.Show("编码正在进行中…", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!Directory.Exists(inputDir))
            {
                MessageBox.Show($"输入目录不存在:\n{inputDir}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 防呆：输入输出同目录时自动创建子目录（仅当存在 .avif 源文件时）
            if (string.Equals(
                Path.GetFullPath(inputDir),
                Path.GetFullPath(outputDir),
                StringComparison.OrdinalIgnoreCase))
            {
                bool hasAvif = false;
                try
                {
                    hasAvif = Directory.EnumerateFiles(inputDir, "*.avif",
                        SearchOption.TopDirectoryOnly).Any();
                }
                catch { }

                if (hasAvif)
                {
                    string subDir = Path.Combine(outputDir, "Avifoutput");
                    var result = MessageBox.Show(
                        $"输入和输出目录相同，且存在 .avif 源文件。\n" +
                        $"是否将输出自动重定向到子目录？\n\n{subDir}",
                        "同目录警告", MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (result == DialogResult.Yes)
                    {
                        outputDir = subDir;
                        txtOutput.Text = outputDir;
                    }
                    else
                    {
                        return;
                    }
                }
            }

            var extensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var files = Directory.EnumerateFiles(inputDir, "*.*", SearchOption.AllDirectories)
                                 .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                                 .ToList();
            if (files.Count == 0)
            {
                MessageBox.Show($"输入目录中没有支持的图片文件:\n{inputDir}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 防呆：输出目录已有 avif 文件时确认
            try
            {
                var existingAvif = Directory.EnumerateFiles(outputDir, "*.avif",
                    SearchOption.TopDirectoryOnly).Take(1).ToList();
                if (existingAvif.Count > 0)
                {
                    var existingCount = Directory.EnumerateFiles(outputDir, "*.avif",
                        SearchOption.TopDirectoryOnly).Count();
                    var confirm = MessageBox.Show(
                        $"输出目录中已有 {existingCount} 个 avif 文件，\n" +
                        "继续编码可能会覆盖同名文件。\n\n是否继续？",
                        "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (confirm != DialogResult.Yes) return;
                }
            }
            catch { }

            _isEncoding = true;
            btnStart.Enabled = false;
            btnStop.Enabled = true;
            progressBar1.Value = 0;
            // 任务栏进度：初始 Normal 状态，进度 0/100
            if (_topLevelHandle != IntPtr.Zero)
                SysTaskBarProgress.SetProgress(_topLevelHandle, SysTaskBarProgress.TaskBarProgressState.Normal, 0u, 100u);

            try
            {
                LogPage?.AppendLog("===== 开始编码 =====");
                LogPage?.AppendLog($"输入目录: {inputDir}");
                LogPage?.AppendLog($"输出目录: {outputDir}");
                LogPage?.AppendLog($"发现图片: {files.Count} 张");



                var config = BuildConfigFromUI();

                var guiLogger = new GuiLogger(LogPage);










                var fileLogger = new FileLogger(outputDir);
                var logger = new CompositeLogger(guiLogger, fileLogger);

                var progress = new Progress<int>(p =>
                {
                    if (InvokeRequired) BeginInvoke(new Action(() => UpdateProgress(p)));
                    else UpdateProgress(p);
                });

                using (_cts = new CancellationTokenSource())
                {
                    try
                    {
                        using var pipeline = new AvifPipeline(
                            inputDir, outputDir, config,
                            logger: logger,
                            progress: progress);
                        await pipeline.RunAsync(_cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        LogPage?.AppendLog("编码已被用户取消。");
                        MessageBox.Show("编码已取消。", "信息", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                }

                LogPage?.AppendLog("===== 全部完成 =====");
                MessageBox.Show("转换完成！", "信息", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LogPage?.AppendLog($"严重错误: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isEncoding = false;
                btnStart.Enabled = true;
                btnStop.Enabled = false;
                _cts?.Dispose(); _cts = null;
                progressBar1.Value = 100;
                // 清除任务栏进度（恢复无进度状态）
                if (_topLevelHandle != IntPtr.Zero)
                    SysTaskBarProgress.Clear(_topLevelHandle);
            }
        }
        private void FormEncode_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (_isEncoding)
            {
                // 主动取消编码任务（内部已通过 Job Object 兜底，此处只是提前通知）
                _cts?.Cancel();
            }
        }
        private void btnStop_Click(object? sender, EventArgs e)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                LogPage?.AppendLog("正在请求取消编码...");
                _cts.Cancel();
                btnStop.Enabled = false;
                // 任务栏进度设为暂停状态（可选）
                if (_topLevelHandle != IntPtr.Zero)
                    SysTaskBarProgress.SetProgress(_topLevelHandle, SysTaskBarProgress.TaskBarProgressState.Paused, (ulong)progressBar1.Value, 100u);
            }
        }

        /// <summary>
        /// 从 UI 控件收集编码参数，构建 PresetConfig。
        /// </summary>
        private PresetConfig BuildConfigFromUI()
        {
            var config = new PresetConfig
            {
                Encoder = cmbEncoder.SelectedIndex >= 0
                    ? cmbEncoder.Items[cmbEncoder.SelectedIndex]?.ToString()
                        ?? "libaom-av1"
                    : "libaom-av1"
            };

            int jobs = (int)numJobs.Value;
            if (jobs > 0)
            {
                config.MaxJobs = jobs;
                config.UserSpecifiedMaxJobs = true;
            }

            config.OutputNameFormat =
                string.IsNullOrWhiteSpace(txtTemplate.Text)
                    ? "covers-{index}.avif"
                    : txtTemplate.Text.Trim();
            config.RecurseSubdirectories = chkRecursive.Checked;
            config.Lossless = chkLossless.Checked;

            // 固定CRF模式下搜索无效，范围模式下搜索才启用
            if (rbCrfFix.Checked)
            {
                config.UseCRFSearch = false;
                config.BaseCRF = (int)numCrfFix.Value;
            }
            else
            {
                config.UseCRFSearch = chkSearch.Checked;
                config.MinCRF = (int)numCrfMin.Value;
                config.MaxCRF = (int)numCrfMax.Value;
            }

            if (chkSweep.Checked)
            {
                if (rbCrfFix.Checked)
                {
                    config.MinCRF = config.MaxCRF =
                        (int)numCrfFix.Value;
                }
                config.SweepMode = true;
                config.UseCRFSearch = false;
            }

            string chroma = cmbChroma
                .Items[cmbChroma.SelectedIndex]?.ToString()
                ?.ToLower() ?? "auto";
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

            string? bitStr = cmbBitDepth
                .Items[cmbBitDepth.SelectedIndex]?.ToString();
            if (!string.IsNullOrEmpty(bitStr)
                && bitStr != "auto"
                && int.TryParse(bitStr, out int b))
            {
                config.BitDepth = b;
                config.UserSetBitDepth = true;
                config.AutoSource = false;
                AvifPipeline.ApplyBitDepth(config);
            }

            config.MetricMode = cmbMetric
                .Items[cmbMetric.SelectedIndex]?.ToString()
                ?.ToLower() ?? "vmaf";

            string? qMode = cmbQualityMode
                .Items[cmbQualityMode.SelectedIndex]?.ToString();
            if (!string.IsNullOrEmpty(qMode) && qMode != "无")
            {
                double rawValue = (double)numQualityValue.Value;
                var def = MetricRegistry.AllKeys
                    .Select(k => MetricRegistry.Get(k))
                    .FirstOrDefault(d => d != null &&
                        string.Equals(d.DisplayName, qMode, StringComparison.OrdinalIgnoreCase));
                if (def != null)
                {
                    config.MetricMode = def.Key;
                    config.SetQualityTarget(rawValue, def.Key);
                }
            }

            config.MaxResolution = (int)numMaxRes.Value;
            config.ApplyScalingToOutput =
                !chkOutputFullRes.Checked;

            config.SerialEncode = chkSerialEncode.Checked;
            // 恢复模式
            config.Resume = _isResumeDetected;

            // 从选项页读取自定义后缀、超时等
            if (Application.OpenForms["Form1"] is Form1 mainForm)
            {
                var optsPage = mainForm.GetOptionsPage();
                if (optsPage != null)
                {
                    string ext = optsPage.GetExtensions();
                    if (!string.IsNullOrWhiteSpace(ext))
                        config.InputExtensions = ext;

                    if (optsPage.EncodeTimeout >= 0)
                        config.EncodeTimeoutMinutes = optsPage.EncodeTimeout == 0 ? -1 : optsPage.EncodeTimeout;
                    if (optsPage.SearchTimeout > 0)
                        config.SearchTimeoutMinutes = optsPage.SearchTimeout;
                    if (optsPage.SafeTimeout > 0)
                        config.SafeTimeoutMinutes = optsPage.SafeTimeout;
                    if (optsPage.SsimTimeout > 0)
                        config.SsimTimeoutMinutes = optsPage.SsimTimeout;

                    config.DryRun = optsPage.DryRun;
                    config.Verbose = optsPage.VerboseOutput;
                }
            }
            config.UsePriorSearch = chkPriorSearch.Checked;
            config.UseProxySearch = chkProxy.Checked;
            config.SearchCpuUsed =
                (int)numSearchCpuUsed.Value;
            config.FinalCpuUsed =
                (int)numFinalCpuUsed.Value;

            config.FileConflictStrategy =
                cmbConflict.SelectedIndex switch
                {
                    1 => PresetConfig.ConflictStrategy.Overwrite,
                    2 => PresetConfig.ConflictStrategy.Skip,
                    _ => PresetConfig.ConflictStrategy.Rename
                };

            return config;
        }


        private void UpdateProgress(int percent)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => UpdateProgress(percent))); return; }
            int clamped = Math.Max(0, Math.Min(percent, 100));
            progressBar1.Value = clamped;
            // 同步任务栏进度
            if (_topLevelHandle != IntPtr.Zero)
                SysTaskBarProgress.SetProgress(_topLevelHandle, SysTaskBarProgress.TaskBarProgressState.Normal, (ulong)clamped, 100u);
        }

        private void label4_Click(object sender, EventArgs e)
        {

        }

        private void label5_Click(object sender, EventArgs e)
        {

        }

        /// <summary>
        /// 从 AppConfig 恢复编码设置到 UI 控件。
        /// </summary>
        public void ApplyConfig(AppConfig cfg)
        {
            _isApplyingPreset = true;
            try
            {
                if (cfg.EncodePreset != null)
                {
                    SetComboBoxItem(cmbPreset, cfg.EncodePreset);
                }
                if (cfg.EncodeEncoder != null)
                {
                    SetComboBoxItem(cmbEncoder, cfg.EncodeEncoder);
                }
                numJobs.Value = cfg.EncodeJobs;
                numSearchCpuUsed.Value = cfg.EncodeSearchCpuUsed;
                numFinalCpuUsed.Value = cfg.EncodeFinalCpuUsed;
                if (cfg.EncodeTemplate != null)
                {
                    txtTemplate.Text = cfg.EncodeTemplate;
                }
                chkSearch.Checked = cfg.EncodeSearch;
                if (cfg.EncodeCrfRangeMode)
                {
                    rbCrfRange.Checked = true;
                    numCrfMin.Value = cfg.EncodeCrfMin;
                    numCrfMax.Value = cfg.EncodeCrfMax;
                }
                else
                {
                    rbCrfFix.Checked = true;
                    numCrfFix.Value = cfg.EncodeCrfFix;
                }
                if (cfg.EncodeMetric != null)
                {
                    SetComboBoxItem(cmbMetric, cfg.EncodeMetric);
                }
                if (cfg.EncodeQualityMode != null)
                {
                    SetComboBoxItem(cmbQualityMode, cfg.EncodeQualityMode);
                }

                // 显式同步范围再赋值，避免恢复时越界
                string? qMode =
                    cmbQualityMode.Items[cmbQualityMode.SelectedIndex]
                        ?.ToString();
                if (qMode != null)
                {
                    SetQualityRange(qMode);
                }
                if (cfg.EncodeQualityValue <= numQualityValue.Maximum
                    && cfg.EncodeQualityValue
                        >= numQualityValue.Minimum)
                {
                    numQualityValue.Value =
                        cfg.EncodeQualityValue;
                }

                if (cfg.EncodeChroma != null)
                {
                    SetComboBoxItem(cmbChroma, cfg.EncodeChroma);
                }
                if (cfg.EncodeBitDepth != null)
                {
                    SetComboBoxItem(cmbBitDepth, cfg.EncodeBitDepth);
                }
                chkLossless.Checked = cfg.EncodeLossless;
                chkRecursive.Checked = cfg.EncodeRecursive;
                numMaxRes.Value = cfg.EncodeMaxRes;
                chkOutputFullRes.Checked = cfg.EncodeOutputFullRes;
                if (cfg.EncodeConflict >= 0
                    && cfg.EncodeConflict < cmbConflict.Items.Count)
                {
                    cmbConflict.SelectedIndex = cfg.EncodeConflict;
                }
                chkSerialEncode.Checked = cfg.EncodeSerialEncode;
                chkPriorSearch.Checked = cfg.EncodePriorSearch;
                chkProxy.Checked = cfg.EncodeProxy;
                chkSweep.Checked = cfg.EncodeSweep;

                if (cfg.EncodeInput != null)
                {
                    txtInput.Text = cfg.EncodeInput;
                }
                if (cfg.EncodeOutput != null)
                {
                    txtOutput.Text = cfg.EncodeOutput;
                }

                // 选项页数据回填
                var optsPage = Application.OpenForms["Form1"] is Form1 mf ? mf.GetOptionsPage() : null;
                if (optsPage != null)
                {
                    if (cfg.EncodeExtensions != null)
                        optsPage.SetExtensions(cfg.EncodeExtensions);
                    optsPage.SetEncodeTimeout(cfg.EncodeTimeoutEncode);
                    optsPage.SetSearchTimeout(cfg.EncodeTimeoutSearch);
                    optsPage.SetSafeTimeout(cfg.EncodeTimeoutSafe);
                    optsPage.SetSsimTimeout(cfg.EncodeTimeoutSsim);
                    optsPage.SetDryRun(cfg.EncodeDryRun);
                    optsPage.SetVerboseOutput(cfg.EncodeVerbose);
                }
            }
            finally
            {
                _isApplyingPreset = false;
            }
        }

        /// <summary>
        /// 从 UI 控件收集编码设置到 AppConfig。
        /// </summary>
        public string GetOutputDir() => txtOutput.Text.Trim();

        public void ResetToDefaults()
        {
            ApplyPresetToUI(CliPreset.Balanced);
            SetComboBoxItem(cmbPreset, "balanced");
        }

        public void BuildConfig(AppConfig cfg)
        {
            cfg.EncodePreset =
                cmbPreset.Items[cmbPreset.SelectedIndex]?.ToString();
            cfg.EncodeEncoder =
                cmbEncoder.Items[cmbEncoder.SelectedIndex]?.ToString();
            cfg.EncodeJobs = (int)numJobs.Value;
            cfg.EncodeSearchCpuUsed = (int)numSearchCpuUsed.Value;
            cfg.EncodeFinalCpuUsed = (int)numFinalCpuUsed.Value;
            cfg.EncodeTemplate = txtTemplate.Text;
            cfg.EncodeSearch = chkSearch.Checked;
            cfg.EncodeCrfRangeMode = rbCrfRange.Checked;
            cfg.EncodeCrfFix = (int)numCrfFix.Value;
            cfg.EncodeCrfMin = (int)numCrfMin.Value;
            cfg.EncodeCrfMax = (int)numCrfMax.Value;
            cfg.EncodeMetric =
                cmbMetric.Items[cmbMetric.SelectedIndex]?.ToString();
            cfg.EncodeQualityMode =
                cmbQualityMode.Items[cmbQualityMode.SelectedIndex]
                    ?.ToString();
            cfg.EncodeQualityValue = (double)numQualityValue.Value;
            cfg.EncodeChroma =
                cmbChroma.Items[cmbChroma.SelectedIndex]?.ToString();
            cfg.EncodeBitDepth =
                cmbBitDepth.Items[cmbBitDepth.SelectedIndex]?.ToString();
            cfg.EncodeLossless = chkLossless.Checked;
            cfg.EncodeRecursive = chkRecursive.Checked;
            cfg.EncodeMaxRes = (int)numMaxRes.Value;
            cfg.EncodeOutputFullRes = chkOutputFullRes.Checked;
            cfg.EncodeConflict = cmbConflict.SelectedIndex;
            cfg.EncodeSerialEncode = chkSerialEncode.Checked;
            cfg.EncodePriorSearch = chkPriorSearch.Checked;
            cfg.EncodeProxy = chkProxy.Checked;
            cfg.EncodeSweep = chkSweep.Checked;

            cfg.EncodeInput = txtInput.Text;
            cfg.EncodeOutput = txtOutput.Text;

            // 选项页数据
            if (Application.OpenForms["Form1"] is Form1 mainForm)
            {
                var optsPage = mainForm.GetOptionsPage();
                if (optsPage != null)
                {
                    cfg.EncodeExtensions = optsPage.GetExtensions();
                    cfg.EncodeTimeoutEncode = optsPage.EncodeTimeout;
                    cfg.EncodeTimeoutSearch = optsPage.SearchTimeout;
                    cfg.EncodeTimeoutSafe = optsPage.SafeTimeout;
                    cfg.EncodeTimeoutSsim = optsPage.SsimTimeout;
                    cfg.EncodeDryRun = optsPage.DryRun;
                    cfg.EncodeVerbose = optsPage.VerboseOutput;
                }
            }
        }

        #region 断点续传 UI

        private void TxtOutput_TextChanged(object? sender, EventArgs e)
        {
            if (_isEncoding) return;
            string outputDir = txtOutput.Text.Trim('"').Trim();
            if (string.IsNullOrEmpty(outputDir)) return;

            string snapshot = Path.Combine(outputDir, ".session", "snapshot.json");
            if (File.Exists(snapshot))
            {
                var (_, configJson, inputPath) = LoadConfigFromSnapshot(snapshot);
                if (configJson != null)
                {
                    if (!string.IsNullOrEmpty(inputPath) && string.IsNullOrEmpty(txtInput.Text.Trim()))
                        txtInput.Text = inputPath;
                    EnterResumeMode(configJson);
                    return;
                }
            }
            ExitResumeMode();
        }

        private static (HashSet<string>?, string?, string?) LoadConfigFromSnapshot(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                string? cfgJson = null, inputPath = null;
                if (root.TryGetProperty("config", out var cfgEl))
                    cfgJson = cfgEl.GetRawText();
                if (root.TryGetProperty("inputDir", out var idEl))
                    inputPath = idEl.GetString();
                return (null, cfgJson, inputPath);
            }
            catch { return (null, null, null); }
        }

        private void EnterResumeMode(string configJson)
        {
            if (_isResumeDetected) return;

            try
            {
                // 从 JSON 回填所有控件
                using var doc = System.Text.Json.JsonDocument.Parse(configJson);
                var cfg = doc.RootElement;

                _isApplyingPreset = true;
                if (cfg.TryGetProperty("Encoder", out var enc)) SetComboBoxItem(cmbEncoder, enc.GetString()!);
                if (cfg.TryGetProperty("Lossless", out var ll)) chkLossless.Checked = ll.GetBoolean();
                if (cfg.TryGetProperty("BaseCRF", out var bcrf)) numCrfFix.Value = bcrf.GetInt32();
                if (cfg.TryGetProperty("MinCRF", out var mn)) numCrfMin.Value = mn.GetInt32();
                if (cfg.TryGetProperty("MaxCRF", out var mx)) numCrfMax.Value = mx.GetInt32();
                if (cfg.TryGetProperty("UseCRFSearch", out var sr))
                {
                    if (sr.GetBoolean()) { rbCrfRange.Checked = true; }
                    else { rbCrfFix.Checked = true; }
                }
                if (cfg.TryGetProperty("MetricMode", out var mm)) SetComboBoxItem(cmbMetric, mm.GetString()!);
                if (cfg.TryGetProperty("PixelFormat", out var pf))
                {
                    string pfStr = pf.GetString() ?? "";
                    if (pfStr.Contains("444")) SetComboBoxItem(cmbChroma, "444");
                    else if (pfStr.Contains("422")) SetComboBoxItem(cmbChroma, "422");
                    else if (pfStr == "") SetComboBoxItem(cmbChroma, "auto");
                    else SetComboBoxItem(cmbChroma, "420");
                }
                if (cfg.TryGetProperty("BitDepth", out var bd))
                    SetComboBoxItem(cmbBitDepth, bd.GetInt32() >= 10 ? "10" : "8");
                if (cfg.TryGetProperty("OutputNameFormat", out var ot)) txtTemplate.Text = ot.GetString()!;
                if (cfg.TryGetProperty("RecurseSubdirectories", out var rc)) chkRecursive.Checked = rc.GetBoolean();
                if (cfg.TryGetProperty("SerialEncode", out var se)) chkSerialEncode.Checked = se.GetBoolean();
                if (cfg.TryGetProperty("UsePriorSearch", out var ps)) chkPriorSearch.Checked = ps.GetBoolean();
                if (cfg.TryGetProperty("UseProxySearch", out var px)) chkProxy.Checked = px.GetBoolean();
                if (cfg.TryGetProperty("SearchCpuUsed", out var sc)) numSearchCpuUsed.Value = sc.GetInt32();
                if (cfg.TryGetProperty("FinalCpuUsed", out var fc)) numFinalCpuUsed.Value = fc.GetInt32();
                if (cfg.TryGetProperty("MaxResolution", out var mr)) numMaxRes.Value = mr.GetInt32();
                if (cfg.TryGetProperty("MaxJobs", out var mj)) numJobs.Value = mj.GetInt32();
                if (cfg.TryGetProperty("InputExtensions", out var ie))
                {
                    var optsPage = Application.OpenForms["Form1"] is Form1 mf ? mf.GetOptionsPage() : null;
                    optsPage?.SetExtensions(ie.GetString() ?? "");
                }
                if (cfg.TryGetProperty("EncodeTimeoutMinutes", out var et))
                {
                    var optsPage = Application.OpenForms["Form1"] is Form1 mf ? mf.GetOptionsPage() : null;
                    optsPage?.SetEncodeTimeout(et.GetInt32());
                }
                if (cfg.TryGetProperty("SearchTimeoutMinutes", out var st))
                {
                    var optsPage = Application.OpenForms["Form1"] is Form1 mf ? mf.GetOptionsPage() : null;
                    optsPage?.SetSearchTimeout(st.GetInt32());
                }
                if (cfg.TryGetProperty("SafeTimeoutMinutes", out var sf))
                {
                    var optsPage = Application.OpenForms["Form1"] is Form1 mf ? mf.GetOptionsPage() : null;
                    optsPage?.SetSafeTimeout(sf.GetInt32());
                }
                if (cfg.TryGetProperty("SsimTimeoutMinutes", out var ss))
                {
                    var optsPage = Application.OpenForms["Form1"] is Form1 mf ? mf.GetOptionsPage() : null;
                    optsPage?.SetSsimTimeout(ss.GetInt32());
                }
                if (cfg.TryGetProperty("SweepMode", out var sw)) chkSweep.Checked = sw.GetBoolean();
                _isApplyingPreset = false;
            }
            catch { _isApplyingPreset = false; }

            // 锁定所有控件
            SetEncodingControlsEnabled(false);

            // 按钮切换
            btnStart.Enabled = false;
            btnResume.Enabled = true;
            btnAbandon.Enabled = true;

            _isResumeDetected = true;
            LogPage?.AppendLog("[RESUME] 检测到中断任务，已恢复编码配置，点击 [恢复任务] 继续");
        }

        private void ExitResumeMode()
        {
            if (!_isResumeDetected) return;

            SetEncodingControlsEnabled(true);
            btnStart.Enabled = true;
            btnResume.Enabled = false;
            btnAbandon.Enabled = false;

            _isResumeDetected = false;
        }

        private void SetEncodingControlsEnabled(bool enabled)
        {
            cmbPreset.Enabled = enabled;
            cmbEncoder.Enabled = enabled;
            numJobs.Enabled = enabled;
            chkSearch.Enabled = enabled;
            numCrfFix.Enabled = enabled && rbCrfFix.Checked;
            numCrfMin.Enabled = enabled && rbCrfRange.Checked;
            numCrfMax.Enabled = enabled && rbCrfRange.Checked;
            rbCrfFix.Enabled = enabled;
            rbCrfRange.Enabled = enabled;
            cmbMetric.Enabled = enabled;
            cmbQualityMode.Enabled = enabled;
            numQualityValue.Enabled = enabled;
            cmbChroma.Enabled = enabled;
            cmbBitDepth.Enabled = enabled;
            chkLossless.Enabled = enabled;
            grpCrfMode.Enabled = enabled;
            txtTemplate.Enabled = enabled;
            cmbTemplate.Enabled = enabled;
            chkRecursive.Enabled = enabled;
            chkSerialEncode.Enabled = enabled;
            chkPriorSearch.Enabled = enabled;
            chkProxy.Enabled = enabled;
            numSearchCpuUsed.Enabled = enabled;
            numFinalCpuUsed.Enabled = enabled;
            numMaxRes.Enabled = enabled;
            chkOutputFullRes.Enabled = enabled;
            cmbConflict.Enabled = enabled;
            chkSweep.Enabled = enabled;
        }

        private void BtnResume_Click(object? sender, EventArgs e)
        {
            btnStart_Click(sender, e);
        }

        private void BtnAbandon_Click(object? sender, EventArgs e)
        {
            if (MessageBox.Show(
                "确定放弃上次中断的任务吗？\n这将删除恢复数据并重新开始。",
                "确认放弃", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                string outputDir = txtOutput.Text.Trim('"').Trim();
                string sessionDir = Path.Combine(outputDir, ".session");
                try
                {
                    if (Directory.Exists(sessionDir))
                        Directory.Delete(sessionDir, true);
                }
                catch { }
                ExitResumeMode();
                LogPage?.AppendLog("[RESUME] 已放弃中断任务，恢复数据已删除");
            }
        }

        #endregion

    }

    // ========== 日志适配器 ==========
    public class GuiLogger : ILogger
    {
        private readonly FormLog? _logForm;
        public GuiLogger(FormLog? logForm) => _logForm = logForm;
        public void LogInfo(string msg) => AppendSafe(msg);
        public void LogError(string msg) => AppendSafe("[ERROR] " + msg);
        public void LogMetric(string m, string msg) => AppendSafe($"[{m}] {msg}");
        public void LogSearch(string msg) => AppendSafe("[SEARCH] " + msg);
        private void AppendSafe(string message)
        {
            if (_logForm == null) return;
            _logForm.AppendLog(message);
        }
    }


}