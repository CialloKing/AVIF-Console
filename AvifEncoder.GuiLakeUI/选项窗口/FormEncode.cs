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

namespace AvifEncoder.GuiLakeUI.选项窗口
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

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public FormOptions? OptionsPage { get; set; }

        /// <summary>缓存顶层 Form1 句柄用于任务栏进度（子 Form 的 Handle 不被任务栏识别）</summary>
        private IntPtr _topLevelHandle;
        public void SetTopLevelHandle(IntPtr h) => _topLevelHandle = h;

        private bool _isEncoding;
        private CancellationTokenSource? _cts;
        private bool _sweepPreviousCrfRangeMode;
        private bool _isResumeDetected;
        private bool _stopping;  // 停止中，冻结进度
        private AvifPipeline? _pipeline;  // 运行时动态调整并发的引用
        private Task? _runTask;  // RunAsync 返回的 Task，供 FormClosing 等待安全退出

        private static readonly string[] _presetNames = ["自定义", "fast", "balanced", "best", "extreme"];
        private static readonly string[] _allEncoderNames = ["libaom-av1", "libsvtav1", "librav1e", "av1_nvenc", "av1_qsv", "av1_amf", "av1_vaapi"];
        private static readonly Dictionary<string, string> _encoderTips = new()
        {
            ["libaom-av1"] = "AOMedia 官方参考编码器\n• 压缩率最高，同画质下文件最小\n• 支持完整的 -aom-params 高级参数调优\n• 支持无损模式、still-picture、tile 多线程\n• 速度范围：cpu-used 0(最慢/最高质量)~8(最快)\n• 推荐用于最终归档编码",
            ["libsvtav1"] = "Intel 主导的 SVT-AV1 编码器\n• 编码速度显著快于 libaom（3~10倍）\n• 多线程效率高，适合批量处理\n• 速度范围：preset 0(最慢/最高质量)~13(最快)\n• 不支持 -aom-params 高级参数\n• 推荐用于高频日常编码",
            ["librav1e"] = "Rust 编写的 AV1 编码器（由 Xiph.Org 维护）\n• 质量与速度均衡，心理视觉调优出色\n• 速度范围：speed 0(最慢)~10(最快)\n• 不支持 tile 分片并行\n• 推荐用于对心理视觉质量有要求的场景",
            ["av1_nvenc"] = "NVIDIA GPU 硬件加速 (RTX 30 系列及以上)\n• 编码速度极快（实时），CPU 占用极低\n• 画质略低于软件编码（同等码率下）\n• 仅支持 4:2:0 色度采样\n• 推荐用于快速预览或低延迟需求",
            ["av1_qsv"] = "Intel 核显硬件加速 (Quick Sync Video)\n• 第 11 代酷睿及以上支持 AV1 硬件编码\n• 编码速度快，CPU 占用低\n• 画质和参数可控性低于软件编码\n• 推荐用于笔记本低功耗场景",
            ["av1_amf"] = "AMD GPU 硬件加速 (Advanced Media Framework)\n• RX 7000 系列及以上支持 AV1 硬件编码\n• 编码速度快，适合游戏录屏/直播\n• 画质和参数可控性低于软件编码\n• 推荐用于 AMD 显卡用户快速转码",
            ["av1_vaapi"] = "Linux 通用硬件加速接口 (VA-API)\n• 统一的 Linux 显卡加速框架\n• 支持 Intel/AMD 核显和独显\n• 编码速度取决于具体硬件\n• 推荐用于 Linux 桌面/服务器环境",
        };
        private static readonly string[] _chromaNames = ["auto", "420", "422", "444"];
        private static readonly string[] _bitDepthNames = ["auto", "8", "10", "12"];
        private static readonly string[] _conflictNames = ["自动重命名", "覆盖已存在文件", "跳过已存在文件"];
        private static readonly Dictionary<string, string> _metricTips = new()
        {
            ["vmaf"] = "Netflix 感知视频质量评估\n• 合法范围 0~100，越高越好\n• 基于机器学习模型，最接近人眼感知\n• 计算开销较大，但准确性最高\n• 推荐作为默认搜索/质量指标",

            ["ssim"] = "结构相似性指数 (Structural SIMilarity)\n• 合法范围 0~1，越高越好\n• 完全一致的图像 = 1.0\n• 经典图像质量指标，计算快速\n• 关注亮度、对比度和结构三个维度\n• 适合快速评估和对比",

            ["psnr"] = "峰值信噪比 Y 通道 (Peak Signal-to-Noise Ratio)\n• 合法范围 0 ~ +∞ dB，越高越好\n• 完全一致的图像 = +∞ (正无穷)\n• ≥ 60 dB 时程序会自动用独立滤镜重算（libvmaf 有 60dB 上限）\n• 最传统、计算最快的指标\n• 与人眼感知一致性较差，不推荐作为唯一标准\n• 适合技术对比和基准测试",

            ["msssim"] = "多尺度结构相似性 (Multi-Scale SSIM)\n• 合法范围 0~1，越高越好\n• SSIM 的改进版，在多个分辨率下评估\n• 比单尺度 SSIM 更准确地反映感知质量\n• 计算开销略高于 SSIM",

            ["xpsnr"] = "加权 XPSNR (Weighted eXtended PSNR)\n• 合法范围 -∞ ~ +∞ dB，越高越好\n• 完全一致的图像 = +∞ (正无穷)\n• 专为 HDR 内容设计的感知质量指标\n• 权重 Y:U:V = 6:1:1\n• 需要 ffmpeg 4.4+ 内置支持\n• 推荐用于 HDR 图像评估",

            ["xpsnr_y"] = "XPSNR 亮度通道 (Y)\n• 合法范围 -∞ ~ +∞ dB，越高越好\n• 仅评估亮度（明暗）分量\n• 适合黑白图像或亮度对比场景",

            ["xpsnr_u"] = "XPSNR 色度通道 (U)\n• 合法范围 -∞ ~ +∞ dB，越高越好\n• 仅评估蓝色差分量 (Cb)",

            ["xpsnr_v"] = "XPSNR 色度通道 (V)\n• 合法范围 -∞ ~ +∞ dB，越高越好\n• 仅评估红色差分量 (Cr)",

            ["xpsnr_w"] = "加权 XPSNR (同 xpsnr)\n• Y:U:V = 6:1:1 加权\n• 合法范围 -∞ ~ +∞ dB，越高越好",

            ["ssimu2"] = "SSIMULACRA 2\n• 合法范围 -∞ ~ +∞，越高越好\n• 高质量编码典型值 50~90\n• 极低质量编码可出现负数\n• 需要外部工具 ssimulacra2.exe\n• 高度准确的感知质量评估\n• 可检测传统指标难以发现的伪影\n• 对模糊、振铃、色块等伪影高度敏感",

            ["butter3"] = "Butteraugli 3-norm\n• 合法范围 0 ~ +∞，越小越好\n• 完全一致的图像 = 0\n• 需要外部工具 butteraugli_main.exe\n• Google 开发的感知差异度量\n• 对 JPEG/AVIF 压缩伪影高度敏感\n• 高质量编码典型值 0.5~3.0\n• 极差质量可达数十甚至数百",

            ["gmsd"] = "梯度幅值相似度偏差 (Gradient Magnitude Similarity Deviation)\n• 合法范围 0~1（可能略超 1），越小越好\n• 完全一致的图像 = 0\n• 内置实现，无需外部工具\n• 基于图像梯度的感知质量评估\n• 对模糊和边缘失真敏感\n• 计算基于 ffmpeg 解码灰度原始数据",

            ["mix"] = "综合加权评分 (MixScore)\n• 合法范围 0~1，越高越好\n• 融合 VMAF + SSIM + MS-SSIM + PSNR-Y (+ XPSNR)\n• 无 XPSNR 时：VMAF 80% + MS-SSIM 10% + SSIM 5% + PSNR 5%\n• 有 XPSNR 时：VMAF 50% + XPSNR 32% + MS-SSIM 8% + SSIM 5% + PSNR 5%\n• 推荐用于多维度综合评估和自动决策",
        };

        public FormEncode()
        {
            InitializeComponent();

            // ★ ExFloatingTip 主题：暗色背景+纯白文字
            LakeUI.ExFloatingTipTheme.Current.CardBackColor = Color.FromArgb(44, 44, 44);
            LakeUI.ExFloatingTipTheme.Current.CardBorderColor = Color.FromArgb(70, 70, 70);
            LakeUI.ExFloatingTipTheme.Current.MessageForeColor = Color.White;

            progressBar1.Maximum = 100;
            progressBar1.Value = 0;
            btnStop.Enabled = false;

            InitializeAllControls();
            ApplyPresetToUI(CliPreset.Balanced);
            SetComboBoxItem(cmbPreset, "balanced");
            AttachAllEvents();

            SetupDragDrop();   // ← 新增此行
            this.FormClosing += FormEncode_FormClosing;

            _resumePollTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _resumePollTimer.Tick += (s, e) =>
            {
                if (!_isEncoding && !string.IsNullOrEmpty(txtOutput.Text))
                {
                    string dir = txtOutput.Text.Trim('"').Trim();
                    if (Directory.Exists(dir)) CheckResumeStatus(dir);
                }
            };
            _resumePollTimer.Start();
        }

        private System.Windows.Forms.Timer? _resumePollTimer;

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
            txtOutput.TextChanged += TxtOutput_TextChanged;
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
        private static void SetComboBoxItem(ModernComboBox combo, string item)
        {
            int idx = combo.Items.IndexOf(item);
            combo.SelectedIndex = idx >= 0 ? idx : -1;
        }

        private void InitializeAllControls()
        {
            cmbPreset.Items.Clear();
            cmbPreset.Items.AddRange(_presetNames);

            cmbEncoder.Items.Clear();
            cmbEncoder.ItemToolTips.Clear();
            foreach (var name in _allEncoderNames)
            {
                cmbEncoder.Items.Add(name);
                cmbEncoder.ItemToolTips.Add(new LakeUI.ModernComboBox.ToolTipEntry(name, _encoderTips.GetValueOrDefault(name, name)));
            }
            SetComboBoxItem(cmbEncoder, "libaom-av1");
            // 如果有缓存的环境检测结果，用它刷新下拉框
            RefreshEncodersFromDetection();
            UpdateCpuUsedLimits();   // 添加此行，使初始上限与编码器匹配

            numJobs.Minimum = 0; numJobs.Maximum = 128; numJobs.Value = 0;

            numSearchCpuUsed.Minimum = 0; numSearchCpuUsed.Maximum = 8;
            numSearchCpuUsed.Value = 4; numSearchCpuUsed.DecimalPlaces = 0;

            numFinalCpuUsed.Minimum = 0; numFinalCpuUsed.Maximum = 8;
            numFinalCpuUsed.Value = 0; numFinalCpuUsed.DecimalPlaces = 0;

            txtTemplate.Text = "covers-{index}.avif";

            // 模板预设下拉框
            cmbTemplate.Items.Clear();
            cmbTemplate.Items.Clear();
            cmbTemplate.ItemToolTips.Clear();
            var templates = new (string template, string tip)[]
            {
                ("covers-{index}.avif",
                    "简洁数字序号\n示例: covers-01.avif\n• {index} = 文件序号（支持 {index:000} 补零格式）"),
                ("{name}.avif",
                    "保留原始文件名\n示例: photo.avif\n• {name} = 输入文件名（不含扩展名）\n⚠ 同名文件会按冲突策略处理"),
                ("{index:000}_{name}.avif",
                    "序号+文件名组合\n示例: 001_photo.avif\n• 兼顾排序和可读性"),
                ("{name}_{crf}.avif",
                    "文件名+CRF值\n示例: photo_30.avif\n• {crf} = 实际使用的 CRF 值"),
                ("{name}_{encoder}_crf{crf}.avif",
                    "文件名+编码器+CRF\n示例: photo_libaom-av1_crf30.avif\n• {encoder} = 编码器名称"),
                ("{name}_{encoder}_crf{crf}_s{speed}.avif",
                    "文件名+编码器+CRF+速度\n示例: photo_libaom-av1_crf30_s4.avif\n• {speed} = 最终编码速度参数"),
                ("{name}_{encoder}_crf{crf}_{pixfmt}.avif",
                    "文件名+编码器+CRF+像素格式\n示例: photo_libaom-av1_crf30_yuv444p.avif\n• {pixfmt} = 实际像素格式"),
                ("{name}_{encoder}_crf{crf}_s{speed}_{pixfmt}.avif",
                    "文件名+编码器+CRF+速度+格式（全参数）\n示例: photo_libaom-av1_crf30_s4_yuv444p.avif\n• 最完整的参数记录"),
                ("{date}/{name}_{crf}.avif",
                    "按日期归档\n示例: 2026-06-03/photo_30.avif\n• {date} = 当前日期 (yyyy-MM-dd)"),
                ("{dir}/{name}.avif",
                    "保持原始子目录结构\n示例: 子文件夹/photo.avif\n• {dir} = 输入文件所在子目录名\n• 递归模式下保留完整目录层级"),
                ("{name}_lossless.avif",
                    "标注无损模式\n示例: photo_lossless.avif\n• {lossless} = 有损时 'lossy'，无损时 'lossless'"),
                ("{name}_{bitdepth}bit.avif",
                    "标注位深\n示例: photo_10bit.avif\n• {bitdepth} = 8 / 10 / 12"),
                ("自定义...",
                    "手动输入任意模板\n• 支持占位符: {name} {index} {crf} {encoder} {speed} {pixfmt} {lossless} {bitdepth} {dir} {date} {time} {datetime} {ext}\n• {index} 支持补零: {index:000} → 001"),
            };
            foreach (var (template, tip) in templates)
            {
                cmbTemplate.Items.Add(template);
                cmbTemplate.ItemToolTips.Add(new LakeUI.ModernComboBox.ToolTipEntry(template, tip));
            }
            cmbTemplate.SelectedIndex = 0;

            numCrfFix.Minimum = 0; numCrfFix.Maximum = 63;
            numCrfMin.Minimum = 0; numCrfMin.Maximum = 63;
            numCrfMax.Minimum = 0; numCrfMax.Maximum = 63;
            rbCrfFix.Checked = true;
            chkSearch.Checked = false;

            cmbQualityMode.Items.Clear();
            cmbQualityMode.ItemToolTips.Clear();
            foreach (var key in MetricRegistry.AllKeys)
            {
                var displayName = MetricRegistry.Get(key)?.DisplayName ?? key;
                cmbQualityMode.Items.Add(displayName);
                cmbQualityMode.ItemToolTips.Add(new LakeUI.ModernComboBox.ToolTipEntry(displayName, _metricTips.GetValueOrDefault(key, key)));
            }
            cmbQualityMode.SelectedIndex = 0;
            numQualityValue.Minimum = 0; numQualityValue.Maximum = 1;
            numQualityValue.Value = 0.95; numQualityValue.DecimalPlaces = 4;
            numQualityValue.Enabled = false;

            cmbChroma.Items.Clear();
            cmbChroma.Items.AddRange(_chromaNames);
            cmbChroma.SelectedIndex = 0;

            cmbBitDepth.Items.Clear();
            cmbBitDepth.Items.AddRange(_bitDepthNames);
            cmbBitDepth.SelectedIndex = 0;

            chkLossless.Checked = false;
            chkRecursive.Checked = false;
            numMaxRes.Minimum = 0; numMaxRes.Maximum = 10000; numMaxRes.Value = 0;
            chkOutputFullRes.Checked = false;
            cmbConflict.Items.Clear();
            cmbConflict.Items.AddRange(_conflictNames);
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
            btnUpdateJobs.Click += BtnUpdateJobs_Click;
            txtOutput.TextChanged += TxtOutput_TextChanged;
        }

        private void AttachCustomMarkEvents()
        {
            cmbEncoder.SelectedIndexChanged += (s, e) =>
            {
                MarkCustom(s, e);
                UpdateCpuUsedLimits();
                UpdateDenoiseLimit();
                UpdateRgbModeEnabled();
                OptionsPage?.UpdateEncoderDefaultParams(cmbEncoder.SelectedItem?.ToString());
            };
            numJobs.ValueChanged += MarkCustom;
            numSearchCpuUsed.ValueChanged += MarkCustom;
            numFinalCpuUsed.ValueChanged += (s, e) => { MarkCustom(s, e); OptionsPage?.RefreshFfmpegPreview(); };
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
            chkSearch.CheckedChanged += (s, e) => { UpdateSearchDependentControls(); MarkCustom(s, e); OptionsPage?.RefreshFfmpegPreview(); };
            rbCrfFix.CheckedChanged += (s, e) => { MarkCustom(s, e); OptionsPage?.RefreshFfmpegPreview(); };
            rbCrfRange.CheckedChanged += (s, e) => { MarkCustom(s, e); OptionsPage?.RefreshFfmpegPreview(); };
            numCrfFix.ValueChanged += (s, e) => { MarkCustom(s, e); OptionsPage?.RefreshFfmpegPreview(); };
            numCrfMin.ValueChanged += (s, e) => { MarkCustom(s, e); OptionsPage?.RefreshFfmpegPreview(); };
            numCrfMax.ValueChanged += (s, e) => { MarkCustom(s, e); OptionsPage?.RefreshFfmpegPreview(); };
            cmbQualityMode.SelectedIndexChanged += (s, e) => { MarkCustom(s, e); OptionsPage?.RefreshFfmpegPreview(); };
            numQualityValue.ValueChanged += (s, e) => { MarkCustom(s, e); OptionsPage?.RefreshFfmpegPreview(); };
            cmbChroma.SelectedIndexChanged += (s, e) => { MarkCustom(s, e); OptionsPage?.RefreshFfmpegPreview(); };
            cmbBitDepth.SelectedIndexChanged += (s, e) => { MarkCustom(s, e); OptionsPage?.RefreshFfmpegPreview(); };
            chkLossless.CheckedChanged += (s, e) => { MarkCustom(s, e); OptionsPage?.RefreshFfmpegPreview(); };
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
                case "XPSNR (W)":
                    numQualityValue.Minimum = 40; numQualityValue.Maximum = 60;
                    numQualityValue.DecimalPlaces = 15;
                    numQualityValue.Increment = 0.1;
                    break;
                case "SSIMULACRA2":
                    numQualityValue.Minimum = -100; numQualityValue.Maximum = 100;
                    numQualityValue.DecimalPlaces = 15;
                    numQualityValue.Increment = 1;
                    break;
                case "Butteraugli 3norm":
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
            numQualityValue.Enabled = true;
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

                // --- 质量目标 ---
                if (!string.IsNullOrEmpty(metricMode))
                {
                    string qMode = MetricRegistry.Get(metricMode)?.DisplayName ?? "VMAF";
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
            if (cmbQualityMode.SelectedIndex < 0 || cmbQualityMode.Items.Count == 0) return;
            string? mode = cmbQualityMode.Items[cmbQualityMode.SelectedIndex]?.ToString();
            if (mode == null) return;

            SetQualityRange(mode);
            // 根据模式设置默认值（使用 MetricRegistry.DisplayName 保持一致）
            numQualityValue.Value = mode switch
            {
                "VMAF" => 95,
                "PSNR-Y" => 40,
                "XPSNR (W)" => 45,
                "SSIMULACRA2" => 90,
                "Butteraugli 3norm" => 1,
                "GMSD" => 0.2,
                _ => 0.95,
            };
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
            UpdateSearchDependentControls();
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
            UpdateSearchDependentControls();
        }

        private void UpdateSearchDependentControls()
        {
            bool searchOn = chkSearch.Checked && chkSearch.Enabled;
            chkPriorSearch.Enabled = searchOn;
            chkProxy.Enabled = searchOn;
            cmbQualityMode.Enabled = searchOn;
            numQualityValue.Enabled = searchOn && cmbQualityMode.Items.Count > 0 && cmbQualityMode.SelectedIndex >= 0;
            lblQuality.Enabled = searchOn;
            numSearchCpuUsed.Enabled = searchOn;
            // 搜索关闭且遍历也关闭时，强制切回固定 CRF 并禁用范围模式
            bool sweepOn = chkSweep.Checked && chkSweep.Enabled;
            if (!searchOn && !sweepOn)
            {
                rbCrfRange.Enabled = false;
                if (rbCrfRange.Checked)
                {
                    rbCrfFix.Checked = true;
                }
            }
            else
            {
                rbCrfRange.Enabled = true;
            }
        }

        /// <summary>从 cmbQualityMode 的显示名反查 MetricRegistry 的 key（如 "VMAF" → "vmaf"）</summary>
        private string? ResolveMetricKeyFromQualityMode()
        {
            string? qMode = cmbQualityMode.Items[cmbQualityMode.SelectedIndex]?.ToString();
            if (string.IsNullOrEmpty(qMode)) return null;
            var def = MetricRegistry.AllKeys
                .Select(k => MetricRegistry.Get(k))
                .FirstOrDefault(d => d != null &&
                    string.Equals(d.DisplayName, qMode, StringComparison.OrdinalIgnoreCase));
            return def?.Key;
        }

        private void UpdateRgbModeEnabled()
        {
            string? encoder = cmbEncoder.SelectedItem?.ToString();
            bool isLibAom = encoder != null && encoder.StartsWith("libaom", StringComparison.OrdinalIgnoreCase);
            OptionsPage?.SetRgbModeEnabled(isLibAom);
        }

        public void UpdateDenoiseLimit()
        {
            string? encoder = cmbEncoder.SelectedItem?.ToString();
            bool isLibAom = encoder != null && encoder.StartsWith("libaom", StringComparison.OrdinalIgnoreCase);
            bool isSvtAv1 = encoder != null && encoder.StartsWith("libsvtav1", StringComparison.OrdinalIgnoreCase);

            if (isLibAom)
            {
                bool useMaxFrames = OptionsPage?.GetArNrUseMaxFrames() ?? false;
                int max = useMaxFrames ? 15 : 6;
                OptionsPage?.SetDenoiseLimit(max, true);
                OptionsPage?.SetArnrEnabled(true);
            }
            else if (isSvtAv1)
            {
                OptionsPage?.SetDenoiseLimit(15, true);
                OptionsPage?.SetArnrEnabled(false);
            }
            else
            {
                OptionsPage?.SetDenoiseLimit(0, false);
                OptionsPage?.SetArnrEnabled(false);
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
            {
                txtOutput.Text = dlg.SelectedPath;
                // 手动触发续传检测 (确保 TextChanged 已处理)
                CheckResumeStatus(dlg.SelectedPath);
            }
        }

        private void CheckResumeStatus(string outputDir)
        {
            if (_isEncoding || string.IsNullOrEmpty(outputDir)) return;
            string snapshot = Path.Combine(outputDir, ".session", "snapshot.json");
            if (File.Exists(snapshot))
            {
                var (_, configJson, inputPath) = LoadConfigFromSnapshot(snapshot);
                if (configJson != null)
                {
                    if (!string.IsNullOrEmpty(inputPath))
                        txtInput.Text = inputPath;
                    EnterResumeMode(configJson);
                }
            }
        }

        private async void btnStart_Click(object? sender, EventArgs e)
        {
            try
            {
                await btnStart_ClickCore(sender, e);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"编码过程发生未处理错误:\n{ex.Message}", "编码异常",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                System.Diagnostics.Trace.WriteLine($"? 编码异常: {ex}");
            }
        }

        private async Task btnStart_ClickCore(object? sender, EventArgs e)
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

            var extensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
            var searchOption = chkRecursive.Checked ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.EnumerateFiles(inputDir, "*.*", searchOption)
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
            _resumePollTimer?.Stop();
            btnStart.Enabled = false;
            btnStop.Enabled = true;
            btnUpdateJobs.Enabled = true;
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
                        _pipeline = pipeline;
                        _runTask = pipeline.RunAsync(_cts.Token);
                        await _runTask;
                    }
                    catch (OperationCanceledException)
                    {
                        LogPage?.AppendLog("编码已被用户取消。");
                        MessageBox.Show("编码已取消。", "信息", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                }

                LogPage?.AppendLog("===== 全部完成 =====");
                _completedNormally = !_stopping;
            }
            catch (Exception ex)
            {
                LogPage?.AppendLog($"严重错误: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isEncoding = false;
                _isResumeDetected = false;
                _resumePollTimer?.Start();
                btnStart.Enabled = true;
                btnStop.Enabled = false;
                btnUpdateJobs.Enabled = false;
                _pipeline = null;
                _cts = null;  // using 块已自动 Dispose，此处仅清空引用
                if (_topLevelHandle != IntPtr.Zero)
                    SysTaskBarProgress.Clear(_topLevelHandle);
                _stopping = false;
            }

            // 用户停止时检测中断续传
            if (!_completedNormally && !string.IsNullOrEmpty(txtOutput.Text))
                CheckResumeStatus(txtOutput.Text.Trim('"').Trim());

            if (_completedNormally)
            {
                // 恢复控件
                SetEncodingControlsEnabled(true);
                btnResume.Enabled = false;
                btnAbandon.Enabled = false;
                // 进度条100%
                progressBar1.Value = 100;
                // 删除快照,防止轮询检测到resume状态
                if (!string.IsNullOrEmpty(txtOutput.Text))
                {
                    string sessionDir = Path.Combine(txtOutput.Text.Trim('"').Trim(), ".session");
                    try { if (Directory.Exists(sessionDir)) Directory.Delete(sessionDir, true); } catch { }
                }
                // 等待UI线程处理完所有待处理消息(包括绘制)
                await Task.Delay(50);
                // 弹窗
                MessageBox.Show("转换完成！", "信息", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        private bool _completedNormally;

        private void FormEncode_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (_isEncoding && _cts != null && !_cts.IsCancellationRequested)
            {
                e.Cancel = true;  // 阻止关闭
                _stopping = true;  // ★ 标记为中断，防止完成后删除 .session
                LogPage?.AppendLog("正在安全停止编码...");
                _cts.Cancel();
                // 仅终止本 Pipeline 追踪的进程，不影响系统其他 ffmpeg
                _pipeline?.KillTrackedProcesses();
                // 异步等待 Pipeline 完成后再关闭窗口
                _ = CloseAfterPipelineAsync();
                return;
            }
            // ★ 正常关闭时释放轮询计时器
            _resumePollTimer?.Stop();
            _resumePollTimer?.Dispose();
            _resumePollTimer = null;
        }

        private async Task CloseAfterPipelineAsync()
        {
            try
            {
                // 等待 _runTask 完成（最多 60 秒，确保 Pipeline 完全清理）
                if (_runTask != null)
                    await Task.WhenAny(_runTask, Task.Delay(TimeSpan.FromSeconds(60)));
            }
            catch { }
            _isEncoding = false;
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired)
                Invoke(new Action(() => { if (!IsDisposed && IsHandleCreated) this.Close(); }));
            else
                this.Close();
        }
        private void BtnUpdateJobs_Click(object? sender, EventArgs e)
        {
            int newJobs = (int)numJobs.Value;
            if (newJobs < 1) newJobs = 1;
            if (_pipeline != null)
            {
                int result = _pipeline.SetMaxJobs(newJobs);
                if (numJobs.Value != result) numJobs.Value = result;
                LogPage?.AppendLog($"[并发] 实时更新为 {result}");
                LakeUI.ExFloatingTipModule.ExFloatingTip(btnUpdateJobs, $"并发已实时更新为 {result}");
            }
            else
            {
                LogPage?.AppendLog($"[并发] 已设为 {newJobs}");
                LakeUI.ExFloatingTipModule.ExFloatingTip(btnUpdateJobs, $"并发数已设为 {newJobs}");
            }
        }

        private void btnStop_Click(object? sender, EventArgs e)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                LogPage?.AppendLog("正在停止编码...");
                _cts.Cancel();
                // 仅终止本 Pipeline 追踪的进程，不影响系统其他 ffmpeg
                _pipeline?.KillTrackedProcesses();
                btnStop.Enabled = false;
                btnUpdateJobs.Enabled = false;
                _stopping = true;  // 冻结进度条
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

            config.MetricMode = ResolveMetricKeyFromQualityMode() ?? "vmaf";

            string? qMode = cmbQualityMode
                .Items[cmbQualityMode.SelectedIndex]?.ToString();
            if (!string.IsNullOrEmpty(qMode))
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
            config.Resume = true;  // 始终启用断点续传日志

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
                    config.EncoderCustomParams = string.IsNullOrWhiteSpace(optsPage.GetEncoderCustomParams())
                        ? null : optsPage.GetEncoderCustomParams();
                    config.Denoise = optsPage.Denoise;
                    config.ArNrUseMaxFrames = optsPage.GetArNrUseMaxFrames();
                    config.RgbMode = optsPage.GetRgbMode();
                    config.AnimatedCommand = string.IsNullOrWhiteSpace(optsPage.GetAnimatedCommand())
                        ? null : optsPage.GetAnimatedCommand();
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
            if (_stopping) return;  // 停止中，冻结进度
            if (InvokeRequired) { BeginInvoke(new Action(() => UpdateProgress(percent))); return; }
            int clamped = Math.Max(0, Math.Min(percent, 100));
            progressBar1.Value = clamped;
            // 同步任务栏进度
            if (_topLevelHandle != IntPtr.Zero)
                SysTaskBarProgress.SetProgress(_topLevelHandle, SysTaskBarProgress.TaskBarProgressState.Normal, (ulong)clamped, 100u);
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
                if (cfg.EncodeRgbMode != null)
                {
                    OptionsPage?.SetRgbMode(cfg.EncodeRgbMode);
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
                    if (cfg.EncodeEncoderParams != null)
                        optsPage.SetEncoderCustomParams(cfg.EncodeEncoderParams);
                    optsPage.SetDenoise(cfg.EncodeDenoise);
                    optsPage.SetArNrUseMaxFrames(cfg.EncodeArNrUseMaxFrames);
                    if (cfg.EncodeRgbMode != null)
                        OptionsPage?.SetRgbMode(cfg.EncodeRgbMode);
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

        /// <summary>返回当前选择的编码器名称</summary>
        public string GetSelectedEncoder()
            => cmbEncoder.SelectedItem?.ToString() ?? "libaom-av1";

        /// <summary>供 FormOptions 预览 ffmpeg 命令的上下文</summary>
        public readonly record struct PreviewContext(
            string Encoder,
            string Chroma,      // "auto" / "420" / "422" / "444"
            string BitDepth,    // "auto" / "8" / "10" / "12"
            int Crf,            // 固定 CRF 值
            int CrfMin,         // CRF 范围下限
            int CrfMax,         // CRF 范围上限
            bool CrfFixed,      // true=固定CRF, false=范围搜索
            int FinalCpuUsed,
            int SearchCpuUsed,
            string QualityMode, // 质量模式
            double QualityValue,
            bool Lossless,
            bool EnableSearch,
            int Denoise,
            bool ArNrUseMaxFrames,
            string? RgbMode
        );

        private static string SafeComboItem(ModernComboBox cb, string def) =>
            cb.SelectedIndex >= 0 && cb.SelectedIndex < cb.Items.Count
                ? cb.Items[cb.SelectedIndex]?.ToString() ?? def : def;

        /// <summary>返回当前 UI 控件状态，供 ffmpeg 命令预览使用</summary>
        public PreviewContext GetPreviewContext()
        {
            return new PreviewContext
            {
                Encoder = GetSelectedEncoder(),
                Chroma = SafeComboItem(cmbChroma, "auto"),
                BitDepth = SafeComboItem(cmbBitDepth, "auto"),
                Crf = (int)numCrfFix.Value,
                CrfMin = (int)numCrfMin.Value,
                CrfMax = (int)numCrfMax.Value,
                CrfFixed = rbCrfFix.Checked,
                FinalCpuUsed = (int)numFinalCpuUsed.Value,
                SearchCpuUsed = (int)numSearchCpuUsed.Value,
                QualityMode = SafeComboItem(cmbQualityMode, "vmaf"),
                QualityValue = (double)numQualityValue.Value,
                Lossless = chkLossless.Checked,
                EnableSearch = chkSearch.Checked,
                Denoise = OptionsPage?.Denoise ?? 0,
                ArNrUseMaxFrames = OptionsPage?.GetArNrUseMaxFrames() ?? false,
                RgbMode = OptionsPage?.GetRgbMode()
            };
        }

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
            cfg.EncodeMetric = ResolveMetricKeyFromQualityMode();
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
                    cfg.EncodeEncoderParams = optsPage.GetEncoderCustomParams();
                    cfg.EncodeAnimatedCommand = optsPage.GetAnimatedCommand();
                    cfg.EncodeDenoise = optsPage.Denoise;
                    cfg.EncodeArNrUseMaxFrames = optsPage.GetArNrUseMaxFrames();
                    cfg.EncodeRgbMode = OptionsPage?.GetRgbMode();
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
                    if (!string.IsNullOrEmpty(inputPath))
                    {
                        txtInput.Text = inputPath;  // 总是恢复输入路径（用户可覆盖）
                    }
                    else
                    {
                        LogPage?.AppendLog("[RESUME] 快照中无输入路径（可能是旧版本快照，请重新运行一次 --resume 以更新）");
                    }
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
                if (cfg.TryGetProperty("MetricMode", out var mm)) { /* cmbMetric 已合并到 cmbQualityMode */ }
                // 恢复质量目标
                double? nativeTarget = null;
                if (cfg.TryGetProperty("NativeTargetValue", out var ntv) && ntv.ValueKind != System.Text.Json.JsonValueKind.Null)
                    nativeTarget = ntv.GetDouble();
                if (cfg.TryGetProperty("XpsnrTargetValue", out var xptv) && xptv.ValueKind != System.Text.Json.JsonValueKind.Null)
                { SetComboBoxItem(cmbQualityMode, "XPSNR (W)"); numQualityValue.Value = Math.Max(numQualityValue.Minimum, Math.Min(numQualityValue.Maximum, xptv.GetDouble())); }
                else if (cfg.TryGetProperty("Ssimu2TargetValue", out var s2tv) && s2tv.ValueKind != System.Text.Json.JsonValueKind.Null)
                { SetComboBoxItem(cmbQualityMode, "SSIMULACRA2"); numQualityValue.Value = Math.Max(numQualityValue.Minimum, Math.Min(numQualityValue.Maximum, s2tv.GetDouble())); }
                else if (cfg.TryGetProperty("Butteraugli3TargetValue", out var b3tv) && b3tv.ValueKind != System.Text.Json.JsonValueKind.Null)
                { SetComboBoxItem(cmbQualityMode, "Butteraugli 3norm"); numQualityValue.Value = Math.Max(numQualityValue.Minimum, Math.Min(numQualityValue.Maximum, b3tv.GetDouble())); }
                else if (cfg.TryGetProperty("GmsdTargetValue", out var gtv) && gtv.ValueKind != System.Text.Json.JsonValueKind.Null)
                { SetComboBoxItem(cmbQualityMode, "GMSD"); numQualityValue.Value = Math.Max(numQualityValue.Minimum, Math.Min(numQualityValue.Maximum, gtv.GetDouble())); }
                else if (nativeTarget.HasValue)
                {
                    string mmStr = cfg.TryGetProperty("MetricMode", out var mm2) ? (mm2.GetString() ?? "vmaf") : "vmaf";
                    string modeName = MetricRegistry.Get(mmStr)?.DisplayName ?? "VMAF";
                    SetComboBoxItem(cmbQualityMode, modeName);
                    numQualityValue.Value = Math.Max(numQualityValue.Minimum, Math.Min(numQualityValue.Maximum, nativeTarget.Value));
                }
                // 恢复 AutoSource 状态：auto 模式下 chroma/bitDepth 显示 "auto"
                bool autoSource = true;
                if (cfg.TryGetProperty("AutoSource", out var asrc)) autoSource = asrc.GetBoolean();
                if (cfg.TryGetProperty("PixelFormat", out var pf))
                {
                    string pfStr = pf.GetString() ?? "";
                    if (autoSource && string.IsNullOrEmpty(pfStr))
                        SetComboBoxItem(cmbChroma, "auto");
                    else if (pfStr.Contains("444")) SetComboBoxItem(cmbChroma, "444");
                    else if (pfStr.Contains("422")) SetComboBoxItem(cmbChroma, "422");
                    else SetComboBoxItem(cmbChroma, "420");
                }
                if (cfg.TryGetProperty("BitDepth", out var bd))
                {
                    if (autoSource && !(cfg.TryGetProperty("UserSetBitDepth", out var usb) && usb.GetBoolean()))
                        SetComboBoxItem(cmbBitDepth, "auto");
                    else
                        SetComboBoxItem(cmbBitDepth, bd.GetInt32() >= 12 ? "12" : bd.GetInt32() >= 10 ? "10" : "8");
                }
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
                // ★ 恢复新增字段（EncoderCustomParams / Denoise / ArNrUseMaxFrames / RgbMode）
                {
                    var optsPage = Application.OpenForms["Form1"] is Form1 mf ? mf.GetOptionsPage() : null;
                    if (optsPage != null)
                    {
                        if (cfg.TryGetProperty("EncoderCustomParams", out var ecp) && ecp.ValueKind != System.Text.Json.JsonValueKind.Null)
                            optsPage.SetEncoderCustomParams(ecp.GetString());
                        if (cfg.TryGetProperty("Denoise", out var dn))
                            optsPage.SetDenoise(dn.GetInt32());
                        if (cfg.TryGetProperty("ArNrUseMaxFrames", out var auf) && auf.ValueKind != System.Text.Json.JsonValueKind.Null)
                            optsPage.SetArNrUseMaxFrames(auf.GetBoolean());
                        if (cfg.TryGetProperty("RgbMode", out var rgb) && rgb.ValueKind != System.Text.Json.JsonValueKind.Null)
                            optsPage.SetRgbMode(rgb.GetString());
                        if (cfg.TryGetProperty("AnimatedCommand", out var ac) && ac.ValueKind != System.Text.Json.JsonValueKind.Null)
                            optsPage.SetAnimatedCommand(ac.GetString() ?? "");
                    }
                }
                if (cfg.TryGetProperty("FileConflictStrategy", out var fcs))
                {
                    string fcsStr = fcs.GetString() ?? "Rename";
                    cmbConflict.SelectedIndex = fcsStr switch
                    {
                        "Overwrite" => 1,
                        "Skip" => 2,
                        _ => 0
                    };
                }
                if (cfg.TryGetProperty("SweepMode", out var sw)) chkSweep.Checked = sw.GetBoolean();
                _isApplyingPreset = false;
            }
            catch { _isApplyingPreset = false; }

            // 锁定所有控件
            SetEncodingControlsEnabled(false);

            // ★ Resume 模式下允许修改并行数（恢复任务前可调整）
            numJobs.Enabled = true;
            btnUpdateJobs.Enabled = true;

            // 按钮切换
            btnStart.Enabled = false;
            btnResume.Enabled = true;
            btnAbandon.Enabled = true;

            _isResumeDetected = true;
            LogPage?.AppendLog("[RESUME] 检测到中断任务，已恢复编码配置，点击 [恢复任务] 继续");
        }

        private void ExitResumeMode()
        {
            SetEncodingControlsEnabled(true);
            btnStart.Enabled = true;
            btnResume.Enabled = false;
            btnAbandon.Enabled = false;
            _isResumeDetected = false;

            // 正常完成时保留 .session（供后续使用），不再自动删除
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
            _stopping = false;  // 确保进度条解锁
            progressBar1.Value = 0;  // 重置进度条
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

        // ========== 环境检测联动 ==========
        /// <summary>根据环境检测结果刷新编码器下拉框，只显示可用的编码器</summary>
        public void RefreshEncodersFromDetection()
        {
            var result = AvifEnvironmentChecker.LastResult;
            if (result == null || !result.FfmpegAvailable) return;

            cmbEncoder.Items.Clear();
            cmbEncoder.ItemToolTips.Clear();
            // 优先显示可用编码器，按固定顺序排列
            var preferredOrder = new[] { "libaom-av1", "libsvtav1", "librav1e",
                                     "av1_nvenc", "av1_qsv", "av1_amf", "av1_vaapi" };
            foreach (var name in preferredOrder)
            {
                if (result.Encoders.Any(e => e.Name == name && e.Available))
                {
                    cmbEncoder.Items.Add(name);
                    cmbEncoder.ItemToolTips.Add(new LakeUI.ModernComboBox.ToolTipEntry(name, _encoderTips.GetValueOrDefault(name, name)));
                }
            }
            // 补充检测到的额外编码器（不在预设列表中的）
            foreach (var enc in result.Encoders)
            {
                if (enc.Available && !preferredOrder.Contains(enc.Name) && !cmbEncoder.Items.Contains(enc.Name))
                {
                    cmbEncoder.Items.Add(enc.Name);
                    cmbEncoder.ItemToolTips.Add(new LakeUI.ModernComboBox.ToolTipEntry(enc.Name, _encoderTips.GetValueOrDefault(enc.Name, enc.Name)));
                }
            }

            if (cmbEncoder.Items.Count > 0)
                cmbEncoder.SelectedIndex = 0;
        }

        /// <summary>根据环境检测结果刷新质量指标下拉框</summary>
        public void RefreshMetricsFromDetection()
        {
            _isApplyingPreset = true;
            try
            {
                var result = AvifEnvironmentChecker.LastResult;
                cmbQualityMode.Items.Clear();
                cmbQualityMode.ItemToolTips.Clear();
                foreach (var key in MetricRegistry.AllKeys)
                {
                    var displayName = MetricRegistry.Get(key)?.DisplayName ?? key;
                    cmbQualityMode.Items.Add(displayName);
                    cmbQualityMode.ItemToolTips.Add(new LakeUI.ModernComboBox.ToolTipEntry(displayName, _metricTips.GetValueOrDefault(key, key)));
                }
                if (cmbQualityMode.Items.Count > 0) cmbQualityMode.SelectedIndex = 0;
                numQualityValue.Enabled = true;
            }
            finally { _isApplyingPreset = false; }
        }
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
