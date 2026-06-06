using System;
using System.Drawing;
using System.Windows.Forms;
using LakeUI;

namespace AvifEncoder.GuiLakeUI.选项窗口
{
    public partial class FormOptions : Form
    {
        public FormOptions()
        {
            InitializeComponent();
            txtExtensions.Text = ".jpg,.jpeg,.png,.webp,.gif";
            numTimeoutEncode!.Maximum = double.MaxValue;
            numTimeoutSearch!.Maximum = double.MaxValue;
            numTimeoutSafe!.Maximum = double.MaxValue;
            numTimeoutSsim!.Maximum = double.MaxValue;
            numTimeoutEncode!.Value = 0;
            numTimeoutSearch!.Value = 60;
            numTimeoutSafe!.Value = 180;
            numTimeoutSsim!.Value = 5;

            cmbRgbMode.Items.Clear();
            cmbRgbMode.Items.AddRange(["自动", "关闭", "gbrp (8位RGB)", "gbrap (8位RGBA)", "gbrp16le (16位RGB)"]);
            cmbRgbMode.SelectedIndex = 1;
            cmbRgbMode.ItemToolTips.Add(new LakeUI.ModernComboBox.ToolTipEntry("自动", "源文件为 RGB 时自动直通，YUV 时走常规流程"));
            cmbRgbMode.ItemToolTips.Add(new LakeUI.ModernComboBox.ToolTipEntry("关闭", "强制使用 YUV 色彩空间编码"));
            cmbRgbMode.ItemToolTips.Add(new LakeUI.ModernComboBox.ToolTipEntry("gbrp (8位RGB)", "RGB 直通，跳过 YUV 转换\n适合 UI 截图、图表、文字密集图片"));
            cmbRgbMode.ItemToolTips.Add(new LakeUI.ModernComboBox.ToolTipEntry("gbrap (8位RGBA)", "RGBA 直通，保留 Alpha 透明通道\n适合带透明度的 PNG 图标"));
            cmbRgbMode.ItemToolTips.Add(new LakeUI.ModernComboBox.ToolTipEntry("gbrp16le (16位RGB)", "高位深 RGB 直通\n适合摄影 RAW 导出、HDR 内容"));

            txtEncoderParams.TextChanged += TxtEncoderParams_TextChanged;
            btnResetEncoderParams!.Click += BtnResetEncoderParams_Click;
            btnCopyFfmpegCommand!.Click += BtnCopyFfmpegCommand_Click;
            btnResetExtensions!.Click += BtnResetExtensions_Click;
            txtAnimatedCommand!.TextChanged += TxtAnimatedCommand_TextChanged;
            btnAnimatedCommand!.Click += BtnAnimatedCommand_Click;
            numDenoise!.ValueChanged += (s, e) =>
            {
                UpdateEncoderParamsWithDenoise();
                RefreshFfmpegPreview();
            };
            cmbRgbMode!.SelectedIndexChanged += (s, e) => RefreshFfmpegPreview();
            chkArnrMaxFrames!.Text = "arnr-strength";
            chkArnrMaxFrames!.CheckedChanged += (s, e) =>
            {
                chkArnrMaxFrames.Text = chkArnrMaxFrames.Checked ? "arnr-max-frames" : "arnr-strength";
                int max = chkArnrMaxFrames.Checked ? 15 : 6;
                numDenoise.Maximum = max;
                if ((int)numDenoise.Value > max)
                    numDenoise.Value = max;
                UpdateEncoderParamsWithDenoise();
                RefreshFfmpegPreview();
            };
            UpdateParamsPreview();  // 初始加载时显示预览
            UpdateAnimatedCommand();  // 初始加载动图命令
        }

        public string GetExtensions() => txtExtensions.Text.Trim();
        public void SetExtensions(string v) => txtExtensions.Text = v ?? "";

        public int EncodeTimeout => (int)numTimeoutEncode.Value;
        public void SetEncodeTimeout(int v) => numTimeoutEncode.Value = v;
        public int SearchTimeout => (int)numTimeoutSearch.Value;
        public void SetSearchTimeout(int v) => numTimeoutSearch.Value = v;
        public int SafeTimeout => (int)numTimeoutSafe.Value;
        public void SetSafeTimeout(int v) => numTimeoutSafe.Value = v;
        public int SsimTimeout => (int)numTimeoutSsim.Value;
        public void SetSsimTimeout(int v) => numTimeoutSsim.Value = v;
        public bool DryRun => chkDryRun.Checked;
        public void SetDryRun(bool v) => chkDryRun.Checked = v;
        public bool VerboseOutput => chkVerbose.Checked;
        public void SetVerboseOutput(bool v) => chkVerbose.Checked = v;

        public int Denoise => (int)numDenoise.Value;
        public void SetDenoise(int v) => numDenoise.Value = Math.Clamp(v, 0, (int)numDenoise.Maximum);

        /// <summary>根据编码器类型调整降噪数字框的上限和启用状态</summary>
        public void SetDenoiseLimit(int max, bool enabled)
        {
            numDenoise.Maximum = max;
            numDenoise.Enabled = enabled;
            if ((int)numDenoise.Value > max)
                numDenoise.Value = max;
        }

        /// <summary>返回 chkArnrMaxFrames 状态</summary>
        public bool GetArNrUseMaxFrames() => chkArnrMaxFrames?.Checked ?? false;
        public void SetArNrUseMaxFrames(bool v) { if (chkArnrMaxFrames != null) chkArnrMaxFrames.Checked = v; }

        /// <summary>返回 RGB 直通模式（gbrp/gbrap/gbrp16le/""/null）</summary>
        public string? GetRgbMode()
        {
            // "" = 强制关闭, null = 自动检测, 其余 = 指定格式
            if (cmbRgbMode == null || cmbRgbMode.SelectedIndex < 0)
                return null;
            if (cmbRgbMode.SelectedIndex == 0)   // "自动"
                return null;
            if (cmbRgbMode.SelectedIndex == 1)   // "关闭"
                return "";
            string? item = cmbRgbMode.Items[cmbRgbMode.SelectedIndex]?.ToString();
            if (item == null) return null;
            int sp = item.IndexOf(' ');
            return sp >= 0 ? item.Substring(0, sp) : item;
        }
        public void SetRgbMode(string? mode)
        {
            if (cmbRgbMode == null) return;
            if (mode == null) { cmbRgbMode.SelectedIndex = 0; return; }   // "自动"
            if (mode == "") { cmbRgbMode.SelectedIndex = 1; return; }      // "关闭"
            for (int i = 2; i < cmbRgbMode.Items.Count; i++)
            {
                string? item = cmbRgbMode.Items[i]?.ToString();
                if (item != null && item.StartsWith(mode))
                {
                    cmbRgbMode.SelectedIndex = i;
                    return;
                }
            }
        }

        /// <summary>启用/禁用 RGB 直通下拉框</summary>
        public void SetRgbModeEnabled(bool enabled)
        {
            if (cmbRgbMode != null)
            {
                cmbRgbMode.Enabled = enabled;
                if (!enabled) cmbRgbMode.SelectedIndex = 1;  // 非 libaom → "关闭"
            }
        }

        /// <summary>启用/禁用 arnr 复选框</summary>
        public void SetArnrEnabled(bool enabled)
        {
            if (chkArnrMaxFrames != null)
            {
                chkArnrMaxFrames.Enabled = enabled;
                if (!enabled)
                {
                    chkArnrMaxFrames.Checked = false;
                    chkArnrMaxFrames.Text = "arnr-strength";
                }
            }
        }

        /// <summary>获取用户自定义的编码器参数文本（对应 CLI --enc-params）</summary>
        public string GetEncoderCustomParams() => txtEncoderParams.Text.Trim();

        /// <summary>设置用户自定义的编码器参数文本</summary>
        public void SetEncoderCustomParams(string? v)
        {
            txtEncoderParams.Text = v ?? "";
        }

        private ModernButton btnCopyFfmpegCommand;
        private ModernButton btnResetExtensions;
        private ModernNumericUpDown numDenoise;
        private ModernCheckBox chkArnrMaxFrames;
        private Label label1 = null!;
        private ModernComboBox cmbRgbMode = null!;
        private Label label2 = null!;
        private ModernTextBox txtAnimatedCommand;
        private string _currentEncoder = "libaom-av1";
        private ModernButton btnAnimatedCommand;
        private FormEncode? _encodePage;

        /// <summary>由 Form1 调用，注入编码页引用。</summary>
        public void SetEncodePage(FormEncode encodePage) => _encodePage = encodePage;

        /// <summary>将降噪参数合并到 txtEncoderParams 中</summary>
        private void UpdateEncoderParamsWithDenoise()
        {
            string defaults = GetDefaultPrivateParams(_currentEncoder);
            int denoise = (int)numDenoise.Value;
            if (denoise > 0 && _currentEncoder.StartsWith("libaom", StringComparison.OrdinalIgnoreCase))
            {
                bool useMaxFrames = chkArnrMaxFrames?.Checked ?? false;
                if (useMaxFrames)
                {
                    int f = Math.Clamp(denoise, 1, 15);
                    defaults += defaults.Length > 0
                        ? $":arnr-max-frames={f}:arnr-strength=4"
                        : $"-aom-params arnr-max-frames={f}:arnr-strength=4";
                }
                else
                {
                    int s = Math.Clamp(denoise, 1, 6);
                    int f = s <= 2 ? 3 : s <= 4 ? 7 : 15;
                    defaults += defaults.Length > 0
                        ? $":arnr-strength={s}:arnr-max-frames={f}"
                        : $"-aom-params arnr-strength={s}:arnr-max-frames={f}";
                }
            }
            else if (denoise > 0 && _currentEncoder.StartsWith("libsvtav1", StringComparison.OrdinalIgnoreCase))
            {
                int grain = Math.Clamp(denoise, 1, 15);
                int lastQ = defaults.LastIndexOf('"');
                if (lastQ >= 0)
                    defaults = defaults.Insert(lastQ, $":film-grain={grain}:film-grain-denoise=1");
                else
                    defaults += $" -svtav1-params \"film-grain={grain}:film-grain-denoise=1\"";
            }
            txtEncoderParams.Text = defaults;
        }

        /// <summary>当编码器切换时，自动填入该编码器的默认私有参数</summary>
        public void UpdateEncoderDefaultParams(string? encoder)
        {
            if (string.IsNullOrEmpty(encoder))
            {
                return;
            }
            _currentEncoder = encoder;
            string defaults = GetDefaultPrivateParams(encoder);
            txtEncoderParams.Text = defaults;
            UpdateParamsPreview();  // 显式刷新预览
        }

        /// <summary>返回各编码器的默认私有参数字符串（含 CLI 前缀）</summary>
        private static string GetDefaultPrivateParams(string encoder)
        {
            return encoder switch
            {
                "libaom-av1" => $"-aom-params {new PresetConfig().AomParams}",
                "libsvtav1"  => "-svtav1-params \"tune=3:keyint=1:avif=1:film-grain=0:enable-qm=1:qm-min=0:qm-max=8\"",
                "librav1e"   => "-rav1e-params tune=psychovisual",
                "av1_nvenc"  => "-tune hq",
                _            => ""
            };
        }

        /// <summary>公开的预览刷新入口，供 FormEncode 控件变化时调用</summary>
        public void RefreshFfmpegPreview()
        {
            UpdateParamsPreview();
            UpdateAnimatedCommand();
        }

        /// <summary>获取用户自定义的动图完整命令</summary>
        public string GetAnimatedCommand() => txtAnimatedCommand?.Text.Trim() ?? "";

        /// <summary>设置动图命令文本</summary>
        public void SetAnimatedCommand(string v) { if (txtAnimatedCommand != null) txtAnimatedCommand.Text = v ?? ""; }

        /// <summary>构建默认动图命令（有 Alpha 路径）</summary>
        internal static string BuildDefaultAnimatedCommand(FormEncode.PreviewContext ctx)
        {
            string enc = ctx.Encoder;
            var encDef = Av1EncoderFactory.Get(enc);
            bool isNvenc = enc.Contains("nvenc", StringComparison.OrdinalIgnoreCase);
            bool isHardware = !encDef.SupportsLossless;

            // ── 像素格式（auto 时用占位符） ──
            string pixFmt;
            if (!string.IsNullOrEmpty(ctx.RgbMode))
            {
                pixFmt = ctx.RgbMode;
            }
            else if (ctx.Lossless)
            {
                pixFmt = ctx.BitDepth == "10" || ctx.BitDepth == "12" ? "yuv444p10le" : "yuv444p";
            }
            else if (isNvenc)
            {
                pixFmt = ctx.BitDepth == "10" || ctx.BitDepth == "12" ? "p010le" : "yuv420p";
            }
            else
            {
                string chroma = ctx.Chroma == "auto" ? "444" : ctx.Chroma;
                string bd = ctx.BitDepth == "auto" ? "10" : ctx.BitDepth;
                string suffix = bd == "10" || bd == "12" ? $"{bd}le" : "";
                pixFmt = $"yuv{chroma}p{suffix}";
                if (ctx.Chroma == "auto" || ctx.BitDepth == "auto")
                    pixFmt = "{PIXFMT}";
            }

            // ── CRF ──
            string crfPart;
            if (ctx.Lossless)
            {
                crfPart = "-crf 0";
            }
            else if (ctx.CrfFixed)
            {
                crfPart = $"-crf {ctx.Crf}";
            }
            else
            {
                crfPart = "-crf {CRF}";
            }

            // ── 速度 ──
            string speedPart = encDef.BuildSpeedArg(ctx.FinalCpuUsed);

            // ── tile / row-mt ──
            string tilePart = encDef.SupportsTiles ? "-tile-columns 2 -tile-rows 0" : "";
            string rowMtPart = encDef.SupportsRowMt ? "-row-mt 1" : "";

            // ── AOM 参数 ──
            string aomPart = encDef.SupportsAomParams
                ? $"-aom-params aq-mode=3:sharpness=2:arnr-strength=2"
                : "";

            // ── 拼接 ──
            var parts = new System.Collections.Generic.List<string>
            {
                "ffmpeg -loglevel info -hide_banner",
                "-i \"INPUT.gif\"",
                "-filter_complex \"[0:v]format=yuva444p10le,split=2[c][a];[a]alphaextract[alpha]\"",
                $"-map \"[c]\" -c:v:0 {enc}",
                $"{crfPart} -b:v 0",
                "{COLORMETA}",
                $"{speedPart} {tilePart} {rowMtPart}".Trim(),
                $"-map \"[alpha]\" -c:v:1 {enc}",
                "-still-picture 0 -vsync vfr",
                aomPart,
                "-y \"OUTPUT.avif\"",
            };

            return string.Join(" ", parts.Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        /// <summary>刷新动图命令文本框（控件变化时重新生成，行为与 txtParamsPreview 一致）</summary>
        private void UpdateAnimatedCommand()
        {
            if (txtAnimatedCommand == null) return;
            FormEncode.PreviewContext ctx;
            try { ctx = _encodePage?.GetPreviewContext() ?? CreateDefaultContext(); }
            catch { ctx = CreateDefaultContext(); }
            string cmd = BuildDefaultAnimatedCommand(ctx);
            if (txtAnimatedCommand.Text != cmd)
            {
                txtAnimatedCommand.Text = cmd;
            }
        }

        /// <summary>外部调用的动图命令重置入口</summary>
        public void ResetAnimatedCommand()
        {
            if (txtAnimatedCommand == null) return;
            FormEncode.PreviewContext ctx;
            try { ctx = _encodePage?.GetPreviewContext() ?? CreateDefaultContext(); }
            catch { ctx = CreateDefaultContext(); }
            txtAnimatedCommand.Text = BuildDefaultAnimatedCommand(ctx);
        }

        private void TxtAnimatedCommand_TextChanged(object? sender, EventArgs e)
        {
            // 用户编辑动图命令时无需联动其他控件
        }

        private void BtnAnimatedCommand_Click(object? sender, EventArgs e)
        {
            ResetAnimatedCommand();
            LakeUI.ExFloatingTipModule.ExFloatingTip(btnAnimatedCommand, "已恢复为默认动图命令");
        }

        /// <summary>实时拼接完整的 ffmpeg 命令行预览</summary>
        private void UpdateParamsPreview()
        {
            FormEncode.PreviewContext ctx;
            try { ctx = _encodePage?.GetPreviewContext() ?? CreateDefaultContext(); }
            catch { ctx = CreateDefaultContext(); }
            string custom = txtEncoderParams.Text.Trim();
            string preview = BuildFullFfmpegPreview(ctx, custom);
            if (txtParamsPreview.Text != preview)
            {
                txtParamsPreview.Text = preview;
                txtParamsPreview.Refresh();
            }
        }

        /// <summary>拼接完整的 ffmpeg 命令行预览（用占位符表示输入/输出文件）</summary>
        internal static string BuildFullFfmpegPreview(FormEncode.PreviewContext ctx, string custom)
        {
            string enc = ctx.Encoder;
            var encDef = Av1EncoderFactory.Get(enc);
            bool isNvenc = enc.Contains("nvenc", StringComparison.OrdinalIgnoreCase);
            bool isHardware = !encDef.SupportsLossless;  // 硬件编码器

            // ── 像素格式（反映 auto / 手动设置） ──
            string pixFmt;
            string chroma = ctx.Chroma;
            string bitDepth = ctx.BitDepth;
            if (!string.IsNullOrEmpty(ctx.RgbMode))
            {
                pixFmt = ctx.RgbMode + "  ← RGB 直通";
            }
            else if (ctx.Lossless)
            {
                pixFmt = bitDepth == "10" || bitDepth == "12" ? "yuv444p10le" : "yuv444p";
            }
            else if (isNvenc)
            {
                pixFmt = bitDepth == "10" || bitDepth == "12" ? "p010le" : "yuv420p";
            }
            else
            {
                // 基础格式
                string fmt = isHardware ? "yuv420p" : $"yuv{(chroma == "auto" ? "420" : chroma)}p";
                if (bitDepth == "10")
                {
                    fmt += "10le";
                }
                else if (bitDepth == "12")
                {
                    fmt += "12le";
                }
                // 标注自动检测的部分
                string note = "";
                if (chroma == "auto" && bitDepth == "auto")
                {
                    note = "  ← 色度 & 位深由源文件决定";
                }
                else if (chroma == "auto")
                {
                    note = "  ← 色度由源文件决定";
                }
                else if (bitDepth == "auto")
                {
                    note = "  ← 位深由源文件决定";
                }
                pixFmt = fmt + note;
            }

            // ── 质量参数（反映 CRF 设置） ──
            string crfPart;
            if (ctx.Lossless)
            {
                crfPart = enc.StartsWith("libaom", StringComparison.OrdinalIgnoreCase) ? "-crf 0（无损）"
                    : enc.StartsWith("libsvtav1", StringComparison.OrdinalIgnoreCase) ? "-svtav1-params lossless=1"
                    : enc.StartsWith("librav1e", StringComparison.OrdinalIgnoreCase) ? "-rav1e-params lossless=1"
                    : "";
            }
            else if (ctx.CrfFixed)
            {
                string val = isNvenc ? $"-cq {ctx.Crf}"
                    : enc.Contains("qsv", StringComparison.OrdinalIgnoreCase) || enc.Contains("vaapi", StringComparison.OrdinalIgnoreCase) ? $"-global_quality {ctx.Crf}"
                    : enc.Contains("amf", StringComparison.OrdinalIgnoreCase) ? $"-qp_i {ctx.Crf} -qp_p {ctx.Crf}"
                    : $"-crf {ctx.Crf}";
                crfPart = val;
            }
            else
            {
                // CRF 搜索范围：显示搜索区间
                string val = isNvenc ? $"-cq 搜索: {ctx.CrfMin}~{ctx.CrfMax}"
                    : $"-crf 搜索: {ctx.CrfMin}~{ctx.CrfMax}";
                crfPart = val;
            }

            // ── 速度参数（反映用户设置的 FinalCpuUsed） ──
            int cpu = ctx.FinalCpuUsed;
            string speedPart = enc.StartsWith("libaom", StringComparison.OrdinalIgnoreCase) ? $"-cpu-used {cpu}"
                : enc.StartsWith("libsvtav1", StringComparison.OrdinalIgnoreCase) ? $"-preset {Math.Clamp(13 - cpu, 0, 13)}"
                : enc.StartsWith("librav1e", StringComparison.OrdinalIgnoreCase) ? $"-speed {cpu}"
                : enc.Contains("nvenc", StringComparison.OrdinalIgnoreCase) ? $"-preset p{Math.Max(1, 7 - Math.Clamp(cpu, 0, 7))}"
                : "";

            // ── 搜索 / 质量目标标注 ──
            string searchNote = "";
            if (!ctx.Lossless && ctx.EnableSearch && ctx.QualityValue > 0)
            {
                searchNote = $"  ← 搜索目标: {ctx.QualityMode.ToUpper()}={ctx.QualityValue}";
            }

            // ── 默认 tile / row-mt ──
            string tilePart = encDef.SupportsTiles ? "-tile-columns 2 -tile-rows 0" : "";
            string rowMtPart = encDef.SupportsRowMt ? "-row-mt 1" : "";

            // ── still-picture ──
            string stillPic = encDef.SupportsStillPicture ? "-still-picture 1" : "";

            // ── 编码器私有参数段（用户自定义，含 CLI 前缀） ──
            string encParams = string.IsNullOrEmpty(custom) ? "" : custom;

            // ── 拼接完整命令 ──
            return string.Join(" ", new[]
            {
                "ffmpeg",
                "-loglevel info -hide_banner",
                "-i \"INPUT.jpg\"",
                $"-c:v {enc}",
                $"-pix_fmt {pixFmt}",
                $"{(string.IsNullOrEmpty(ctx.RgbMode) ? "-color_range pc -color_primaries bt709 -color_trc iec61966-2-1 -colorspace bt709" : "-color_range pc -colorspace rgb")}",
                crfPart,
                searchNote,
                "-b:v 0",
                speedPart,
                tilePart,
                rowMtPart,
                stillPic,
                "-frames:v 1",
                encParams,
                "-y \"OUTPUT.avif\"",
            }.Where(s => s.Length > 0));
        }

        /// <summary>构建默认预览上下文（FormEncode 未就绪时使用）</summary>
        private static FormEncode.PreviewContext CreateDefaultContext()
        {
            return new FormEncode.PreviewContext
            {
                Encoder = "libaom-av1",
                Chroma = "auto",
                BitDepth = "auto",
                Crf = 30,
                CrfMin = 20,
                CrfMax = 40,
                CrfFixed = true,
                FinalCpuUsed = 0,
                SearchCpuUsed = 4,
                QualityMode = "vmaf",
                QualityValue = 95,
                Lossless = false,
                EnableSearch = true,
                Denoise = 0,
                ArNrUseMaxFrames = false,
                RgbMode = null,
            };
        }

        private void TxtEncoderParams_TextChanged(object? sender, EventArgs e)
        {
            UpdateParamsPreview();
        }

        private void InitializeComponent()
        {
            modernPanel1 = new ModernPanel();
            btnAnimatedCommand = new ModernButton();
            txtAnimatedCommand = new ModernTextBox();
            label2 = new Label();
            cmbRgbMode = new ModernComboBox();
            label1 = new Label();
            chkArnrMaxFrames = new ModernCheckBox();
            numDenoise = new ModernNumericUpDown();
            btnResetExtensions = new ModernButton();
            btnCopyFfmpegCommand = new ModernButton();
            btnResetEncoderParams = new ModernButton();
            txtParamsPreview = new ModernTextBox();
            txtEncoderParams = new ModernTextBox();
            chkDryRun = new ModernCheckBox();
            chkVerbose = new ModernCheckBox();
            numTimeoutSsim = new ModernNumericUpDown();
            numTimeoutSafe = new ModernNumericUpDown();
            numTimeoutSearch = new ModernNumericUpDown();
            numTimeoutEncode = new ModernNumericUpDown();
            txtExtensions = new ModernTextBox();
            lblTimeout = new Label();
            lblTimeoutEncode = new Label();
            lblTimeoutSearch = new Label();
            lblTimeoutSafe = new Label();
            lblTimeoutSsim = new Label();
            modernPanel1.SuspendLayout();
            SuspendLayout();
            // 
            // modernPanel1
            // 
            modernPanel1.BackColor = Color.Transparent;
            modernPanel1.BackColor1 = Color.Transparent;
            modernPanel1.BorderColor = Color.Transparent;
            modernPanel1.Controls.Add(btnAnimatedCommand);
            modernPanel1.Controls.Add(txtAnimatedCommand);
            modernPanel1.Controls.Add(label2);
            modernPanel1.Controls.Add(cmbRgbMode);
            modernPanel1.Controls.Add(label1);
            modernPanel1.Controls.Add(chkArnrMaxFrames);
            modernPanel1.Controls.Add(numDenoise);
            modernPanel1.Controls.Add(btnResetExtensions);
            modernPanel1.Controls.Add(btnCopyFfmpegCommand);
            modernPanel1.Controls.Add(btnResetEncoderParams);
            modernPanel1.Controls.Add(txtParamsPreview);
            modernPanel1.Controls.Add(txtEncoderParams);
            modernPanel1.Controls.Add(chkDryRun);
            modernPanel1.Controls.Add(chkVerbose);
            modernPanel1.Controls.Add(numTimeoutSsim);
            modernPanel1.Controls.Add(numTimeoutSafe);
            modernPanel1.Controls.Add(numTimeoutSearch);
            modernPanel1.Controls.Add(numTimeoutEncode);
            modernPanel1.Controls.Add(txtExtensions);
            modernPanel1.Controls.Add(lblTimeout);
            modernPanel1.Controls.Add(lblTimeoutEncode);
            modernPanel1.Controls.Add(lblTimeoutSearch);
            modernPanel1.Controls.Add(lblTimeoutSafe);
            modernPanel1.Controls.Add(lblTimeoutSsim);
            modernPanel1.Dock = DockStyle.Fill;
            modernPanel1.ForeColor = Color.Transparent;
            modernPanel1.Location = new Point(0, 0);
            modernPanel1.Name = "modernPanel1";
            modernPanel1.Size = new Size(1099, 763);
            modernPanel1.TabIndex = 0;
            // 
            // btnAnimatedCommand
            // 
            btnAnimatedCommand.AnimationFPS = 0;
            btnAnimatedCommand.BackColor1 = Color.Transparent;
            btnAnimatedCommand.BorderColor = Color.Gainsboro;
            btnAnimatedCommand.BorderRadius = 10;
            btnAnimatedCommand.ForeColor = Color.WhiteSmoke;
            btnAnimatedCommand.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            btnAnimatedCommand.Location = new Point(16, 299);
            btnAnimatedCommand.Margin = new Padding(2);
            btnAnimatedCommand.Name = "btnAnimatedCommand";
            btnAnimatedCommand.PressedBackColor1 = Color.White;
            btnAnimatedCommand.Size = new Size(282, 40);
            btnAnimatedCommand.TabIndex = 79;
            btnAnimatedCommand.Text = "动图编码完整命令，可直接编辑";
            btnAnimatedCommand.TextAlign = ModernButton.TextAlignEnum.Left;
            // 
            // txtAnimatedCommand
            // 
            txtAnimatedCommand.AllowDrop = true;
            txtAnimatedCommand.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtAnimatedCommand.AnimationFPS = 0;
            txtAnimatedCommand.BackColor1 = Color.Transparent;
            txtAnimatedCommand.BorderColorFocus = Color.White;
            txtAnimatedCommand.ForeColor = Color.WhiteSmoke;
            txtAnimatedCommand.Location = new Point(16, 343);
            txtAnimatedCommand.Margin = new Padding(2);
            txtAnimatedCommand.Name = "txtAnimatedCommand";
            txtAnimatedCommand.SelectionColor = Color.FromArgb(180, 128, 128, 128);
            txtAnimatedCommand.Size = new Size(1036, 32);
            txtAnimatedCommand.TabIndex = 78;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(217, 410);
            label2.Name = "label2";
            label2.Size = new Size(81, 17);
            label2.TabIndex = 77;
            label2.Text = "RGB色彩格式";
            // 
            // cmbRgbMode
            // 
            cmbRgbMode.BackColor1 = Color.Transparent;
            cmbRgbMode.BorderColor = Color.Gainsboro;
            cmbRgbMode.BorderColorFocus = Color.White;
            cmbRgbMode.DropDownAnimationFPS = 0;
            cmbRgbMode.DropDownBackColor = Color.Transparent;
            cmbRgbMode.DropDownBackdropBlurPasses = 2;
            cmbRgbMode.DropDownBackdropBlurRadius = 5;
            cmbRgbMode.DropDownBackdropMode = PopupBackdropMode.Auto;
            cmbRgbMode.DropDownBorderColor = Color.White;
            cmbRgbMode.DropDownHoverColor = Color.FromArgb(128, 255, 255, 255);
            cmbRgbMode.DropDownMode = ModernComboBox.DropDownDisplayMode.Overlay;
            cmbRgbMode.DropDownPadding = new Padding(3, 0, 0, 0);
            cmbRgbMode.DropDownScrollBarColor = Color.Gainsboro;
            cmbRgbMode.DropDownScrollBarHoverColor = Color.White;
            cmbRgbMode.DropDownSelectedColor = Color.Transparent;
            cmbRgbMode.DropDownSelectedForeColor = Color.White;
            cmbRgbMode.ForeColor = Color.WhiteSmoke;
            cmbRgbMode.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            cmbRgbMode.Location = new Point(217, 440);
            cmbRgbMode.Margin = new Padding(2, 2, 2, 2);
            cmbRgbMode.Name = "cmbRgbMode";
            cmbRgbMode.Padding = new Padding(6, 0, 0, 0);
            cmbRgbMode.SelectionColor = Color.Transparent;
            cmbRgbMode.Size = new Size(160, 32);
            cmbRgbMode.TabIndex = 76;
            cmbRgbMode.Text = "RGB直通";
            cmbRgbMode.ToolTipBackColor = Color.DimGray;
            cmbRgbMode.ToolTipForeColor = Color.White;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(16, 390);
            label1.Name = "label1";
            label1.Size = new Size(120, 17);
            label1.TabIndex = 75;
            label1.Text = "编码器降噪 (0=关闭)";
            // 
            // chkArnrMaxFrames
            // 
            chkArnrMaxFrames.AnimationFPS = 0;
            chkArnrMaxFrames.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkArnrMaxFrames.BoxUncheckedBackColor = Color.FromArgb(30, 50, 50, 50);
            chkArnrMaxFrames.ForeColor = Color.WhiteSmoke;
            chkArnrMaxFrames.Location = new Point(16, 410);
            chkArnrMaxFrames.Name = "chkArnrMaxFrames";
            chkArnrMaxFrames.Size = new Size(150, 24);
            chkArnrMaxFrames.TabIndex = 74;
            chkArnrMaxFrames.Text = "aom降噪切换";
            // 
            // numDenoise
            // 
            numDenoise.AllowDrop = true;
            numDenoise.BackColor1 = Color.Transparent;
            numDenoise.BorderColor = Color.DarkGray;
            numDenoise.BorderColorFocus = Color.White;
            numDenoise.CaretColor = Color.FromArgb(220, 220, 220);
            numDenoise.DecimalPlaces = 15;
            numDenoise.ForeColor = Color.White;
            numDenoise.HoverArrowColor = Color.Gray;
            numDenoise.HoverButtonBackColor1 = Color.FromArgb(200, 255, 255, 255);
            numDenoise.Location = new Point(16, 440);
            numDenoise.Name = "numDenoise";
            numDenoise.Padding = new Padding(6, 0, 0, 0);
            numDenoise.Size = new Size(160, 32);
            numDenoise.TabIndex = 72;
            // 
            // btnResetExtensions
            // 
            btnResetExtensions.AnimationFPS = 0;
            btnResetExtensions.BackColor1 = Color.Transparent;
            btnResetExtensions.BorderColor = Color.Gainsboro;
            btnResetExtensions.BorderRadius = 10;
            btnResetExtensions.ForeColor = Color.WhiteSmoke;
            btnResetExtensions.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            btnResetExtensions.Location = new Point(16, 23);
            btnResetExtensions.Margin = new Padding(2);
            btnResetExtensions.Name = "btnResetExtensions";
            btnResetExtensions.PressedBackColor1 = Color.White;
            btnResetExtensions.Size = new Size(611, 40);
            btnResetExtensions.TabIndex = 71;
            btnResetExtensions.Text = "图片后缀名，使用英文逗号分隔，默认为.jpg,.jpeg,.png,.webp,.gif这5种，可按需添加";
            btnResetExtensions.TextAlign = ModernButton.TextAlignEnum.Left;
            // 
            // btnCopyFfmpegCommand
            // 
            btnCopyFfmpegCommand.AnimationFPS = 0;
            btnCopyFfmpegCommand.BackColor1 = Color.Transparent;
            btnCopyFfmpegCommand.BorderColor = Color.Gainsboro;
            btnCopyFfmpegCommand.BorderRadius = 10;
            btnCopyFfmpegCommand.ForeColor = Color.WhiteSmoke;
            btnCopyFfmpegCommand.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            btnCopyFfmpegCommand.Location = new Point(16, 201);
            btnCopyFfmpegCommand.Margin = new Padding(2);
            btnCopyFfmpegCommand.Name = "btnCopyFfmpegCommand";
            btnCopyFfmpegCommand.PressedBackColor1 = Color.White;
            btnCopyFfmpegCommand.Size = new Size(282, 40);
            btnCopyFfmpegCommand.TabIndex = 70;
            btnCopyFfmpegCommand.Text = "实际使用ffmpeg完整命令只读展示";
            btnCopyFfmpegCommand.TextAlign = ModernButton.TextAlignEnum.Left;
            // 
            // btnResetEncoderParams
            // 
            btnResetEncoderParams.AnimationFPS = 0;
            btnResetEncoderParams.BackColor1 = Color.Transparent;
            btnResetEncoderParams.BorderColor = Color.Gainsboro;
            btnResetEncoderParams.BorderRadius = 10;
            btnResetEncoderParams.ForeColor = Color.WhiteSmoke;
            btnResetEncoderParams.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            btnResetEncoderParams.Location = new Point(16, 114);
            btnResetEncoderParams.Margin = new Padding(2);
            btnResetEncoderParams.Name = "btnResetEncoderParams";
            btnResetEncoderParams.PressedBackColor1 = Color.White;
            btnResetEncoderParams.Size = new Size(282, 40);
            btnResetEncoderParams.TabIndex = 69;
            btnResetEncoderParams.Text = "自定义ffmpeg命令行高级参数，可直接编辑";
            btnResetEncoderParams.TextAlign = ModernButton.TextAlignEnum.Left;
            // 
            // txtParamsPreview
            // 
            txtParamsPreview.AllowDrop = true;
            txtParamsPreview.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtParamsPreview.AnimationFPS = 0;
            txtParamsPreview.BackColor1 = Color.Transparent;
            txtParamsPreview.BorderColor = Color.White;
            txtParamsPreview.BorderColorFocus = Color.White;
            txtParamsPreview.ForeColor = Color.WhiteSmoke;
            txtParamsPreview.Location = new Point(16, 245);
            txtParamsPreview.Margin = new Padding(2);
            txtParamsPreview.Name = "txtParamsPreview";
            txtParamsPreview.ReadOnly = true;
            txtParamsPreview.SelectionColor = Color.FromArgb(180, 128, 128, 128);
            txtParamsPreview.Size = new Size(1036, 32);
            txtParamsPreview.TabIndex = 67;
            // 
            // txtEncoderParams
            // 
            txtEncoderParams.AllowDrop = true;
            txtEncoderParams.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtEncoderParams.BackColor1 = Color.Transparent;
            txtEncoderParams.BorderColorFocus = Color.White;
            txtEncoderParams.ForeColor = Color.WhiteSmoke;
            txtEncoderParams.Location = new Point(16, 158);
            txtEncoderParams.Margin = new Padding(2);
            txtEncoderParams.Name = "txtEncoderParams";
            txtEncoderParams.SelectionColor = Color.FromArgb(180, 128, 128, 128);
            txtEncoderParams.Size = new Size(1036, 32);
            txtEncoderParams.TabIndex = 65;
            // 
            // chkDryRun
            // 
            chkDryRun.AnimationFPS = 0;
            chkDryRun.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkDryRun.BoxUncheckedBackColor = Color.FromArgb(30, 50, 50, 50);
            chkDryRun.ForeColor = Color.WhiteSmoke;
            chkDryRun.Location = new Point(731, 727);
            chkDryRun.Name = "chkDryRun";
            chkDryRun.Size = new Size(150, 24);
            chkDryRun.TabIndex = 64;
            chkDryRun.Text = "仅模拟运行 (--dry-run)";
            // 
            // chkVerbose
            // 
            chkVerbose.AnimationFPS = 0;
            chkVerbose.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkVerbose.BoxUncheckedBackColor = Color.FromArgb(30, 50, 50, 50);
            chkVerbose.ForeColor = Color.WhiteSmoke;
            chkVerbose.Location = new Point(731, 697);
            chkVerbose.Name = "chkVerbose";
            chkVerbose.Size = new Size(150, 24);
            chkVerbose.TabIndex = 63;
            chkVerbose.Text = "详细输出 (--verbose)";
            // 
            // numTimeoutSsim
            // 
            numTimeoutSsim.AllowDrop = true;
            numTimeoutSsim.BackColor1 = Color.Transparent;
            numTimeoutSsim.BorderColor = Color.DarkGray;
            numTimeoutSsim.BorderColorFocus = Color.White;
            numTimeoutSsim.CaretColor = Color.FromArgb(220, 220, 220);
            numTimeoutSsim.DecimalPlaces = 15;
            numTimeoutSsim.ForeColor = Color.White;
            numTimeoutSsim.HoverArrowColor = Color.Gray;
            numTimeoutSsim.HoverButtonBackColor1 = Color.FromArgb(200, 255, 255, 255);
            numTimeoutSsim.Location = new Point(8, 719);
            numTimeoutSsim.Name = "numTimeoutSsim";
            numTimeoutSsim.Padding = new Padding(6, 0, 0, 0);
            numTimeoutSsim.Size = new Size(160, 32);
            numTimeoutSsim.TabIndex = 62;
            numTimeoutSsim.ValueChanged += numTimeoutSsim_ValueChanged;
            // 
            // numTimeoutSafe
            // 
            numTimeoutSafe.AllowDrop = true;
            numTimeoutSafe.BackColor1 = Color.Transparent;
            numTimeoutSafe.BorderColor = Color.DarkGray;
            numTimeoutSafe.BorderColorFocus = Color.White;
            numTimeoutSafe.CaretColor = Color.FromArgb(220, 220, 220);
            numTimeoutSafe.DecimalPlaces = 15;
            numTimeoutSafe.ForeColor = Color.White;
            numTimeoutSafe.HoverArrowColor = Color.Gray;
            numTimeoutSafe.HoverButtonBackColor1 = Color.FromArgb(200, 255, 255, 255);
            numTimeoutSafe.Location = new Point(8, 661);
            numTimeoutSafe.Name = "numTimeoutSafe";
            numTimeoutSafe.Padding = new Padding(6, 0, 0, 0);
            numTimeoutSafe.Size = new Size(160, 32);
            numTimeoutSafe.TabIndex = 61;
            // 
            // numTimeoutSearch
            // 
            numTimeoutSearch.AllowDrop = true;
            numTimeoutSearch.BackColor1 = Color.Transparent;
            numTimeoutSearch.BorderColor = Color.DarkGray;
            numTimeoutSearch.BorderColorFocus = Color.White;
            numTimeoutSearch.CaretColor = Color.FromArgb(220, 220, 220);
            numTimeoutSearch.DecimalPlaces = 15;
            numTimeoutSearch.ForeColor = Color.White;
            numTimeoutSearch.HoverArrowColor = Color.Gray;
            numTimeoutSearch.HoverButtonBackColor1 = Color.FromArgb(200, 255, 255, 255);
            numTimeoutSearch.Location = new Point(8, 603);
            numTimeoutSearch.Name = "numTimeoutSearch";
            numTimeoutSearch.Padding = new Padding(6, 0, 0, 0);
            numTimeoutSearch.Size = new Size(160, 32);
            numTimeoutSearch.TabIndex = 60;
            // 
            // numTimeoutEncode
            // 
            numTimeoutEncode.AllowDrop = true;
            numTimeoutEncode.BackColor1 = Color.Transparent;
            numTimeoutEncode.BorderColor = Color.DarkGray;
            numTimeoutEncode.BorderColorFocus = Color.White;
            numTimeoutEncode.CaretColor = Color.FromArgb(220, 220, 220);
            numTimeoutEncode.DecimalPlaces = 15;
            numTimeoutEncode.ForeColor = Color.White;
            numTimeoutEncode.HoverArrowColor = Color.Gray;
            numTimeoutEncode.HoverButtonBackColor1 = Color.FromArgb(200, 255, 255, 255);
            numTimeoutEncode.Location = new Point(8, 545);
            numTimeoutEncode.Name = "numTimeoutEncode";
            numTimeoutEncode.Padding = new Padding(6, 0, 0, 0);
            numTimeoutEncode.Size = new Size(160, 32);
            numTimeoutEncode.TabIndex = 59;
            // 
            // txtExtensions
            // 
            txtExtensions.AllowDrop = true;
            txtExtensions.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtExtensions.BackColor1 = Color.Transparent;
            txtExtensions.BorderColorFocus = Color.White;
            txtExtensions.ForeColor = Color.WhiteSmoke;
            txtExtensions.Location = new Point(16, 67);
            txtExtensions.Margin = new Padding(2);
            txtExtensions.Name = "txtExtensions";
            txtExtensions.SelectionColor = Color.FromArgb(180, 128, 128, 128);
            txtExtensions.Size = new Size(1036, 32);
            txtExtensions.TabIndex = 14;
            // 
            // lblTimeout
            // 
            lblTimeout.AutoSize = true;
            lblTimeout.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold, GraphicsUnit.Point, 134);
            lblTimeout.ForeColor = Color.WhiteSmoke;
            lblTimeout.Location = new Point(8, 496);
            lblTimeout.Name = "lblTimeout";
            lblTimeout.Size = new Size(88, 26);
            lblTimeout.TabIndex = 3;
            lblTimeout.Text = "超时设置";
            // 
            // lblTimeoutEncode
            // 
            lblTimeoutEncode.AutoSize = true;
            lblTimeoutEncode.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 134);
            lblTimeoutEncode.ForeColor = Color.WhiteSmoke;
            lblTimeoutEncode.Location = new Point(8, 522);
            lblTimeoutEncode.Name = "lblTimeoutEncode";
            lblTimeoutEncode.Size = new Size(156, 20);
            lblTimeoutEncode.TabIndex = 4;
            lblTimeoutEncode.Text = "单次编码超时 (0=自动):";
            // 
            // lblTimeoutSearch
            // 
            lblTimeoutSearch.AutoSize = true;
            lblTimeoutSearch.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 134);
            lblTimeoutSearch.ForeColor = Color.WhiteSmoke;
            lblTimeoutSearch.Location = new Point(8, 580);
            lblTimeoutSearch.Name = "lblTimeoutSearch";
            lblTimeoutSearch.Size = new Size(96, 20);
            lblTimeoutSearch.TabIndex = 6;
            lblTimeoutSearch.Text = "搜索全局超时:";
            // 
            // lblTimeoutSafe
            // 
            lblTimeoutSafe.AutoSize = true;
            lblTimeoutSafe.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 134);
            lblTimeoutSafe.ForeColor = Color.WhiteSmoke;
            lblTimeoutSafe.Location = new Point(8, 638);
            lblTimeoutSafe.Name = "lblTimeoutSafe";
            lblTimeoutSafe.Size = new Size(96, 20);
            lblTimeoutSafe.TabIndex = 8;
            lblTimeoutSafe.Text = "安全模式超时:";
            // 
            // lblTimeoutSsim
            // 
            lblTimeoutSsim.AutoSize = true;
            lblTimeoutSsim.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 134);
            lblTimeoutSsim.ForeColor = Color.WhiteSmoke;
            lblTimeoutSsim.Location = new Point(8, 697);
            lblTimeoutSsim.Name = "lblTimeoutSsim";
            lblTimeoutSsim.Size = new Size(106, 20);
            lblTimeoutSsim.TabIndex = 10;
            lblTimeoutSsim.Text = "SSIM 计算超时:";
            // 
            // FormOptions
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.Black;
            ClientSize = new Size(1099, 763);
            Controls.Add(modernPanel1);
            Name = "FormOptions";
            Text = "FormOptions";
            modernPanel1.ResumeLayout(false);
            modernPanel1.PerformLayout();
            ResumeLayout(false);
        }

        public ModernPanel modernPanel1 = null!;
        private Label lblTimeoutEncode = null!;
        private Label lblTimeoutSearch = null!;
        private Label lblTimeoutSafe = null!;
        private Label lblTimeoutSsim = null!;
        private ModernTextBox txtExtensions = null!;
        private ModernNumericUpDown numTimeoutEncode = null!;
        private ModernNumericUpDown numTimeoutSearch = null!;
        private ModernNumericUpDown numTimeoutSafe = null!;
        private ModernNumericUpDown numTimeoutSsim = null!;
        private Label lblTimeout = null!;
        private ModernCheckBox chkDryRun = null!;
        private ModernTextBox txtEncoderParams = null!;
        private ModernTextBox txtParamsPreview = null!;
        private ModernButton btnResetEncoderParams;
        private ModernCheckBox chkVerbose = null!;

        private void BtnResetEncoderParams_Click(object? sender, EventArgs e)
        {
            txtEncoderParams.Text = GetDefaultPrivateParams(_currentEncoder);
            LakeUI.ExFloatingTipModule.ExFloatingTip(btnResetEncoderParams, "已恢复为默认参数");
        }

        private void BtnCopyFfmpegCommand_Click(object? sender, EventArgs e)
        {
            string cmd = txtParamsPreview.Text;
            if (!string.IsNullOrEmpty(cmd))
            {
                Clipboard.SetText(cmd);
                LakeUI.ExFloatingTipModule.ExFloatingTip(btnCopyFfmpegCommand, "ffmpeg 命令已复制到剪贴板");
            }
        }

        private void BtnResetExtensions_Click(object? sender, EventArgs e)
        {
            txtExtensions.Text = ".jpg,.jpeg,.png,.webp,.gif";
            LakeUI.ExFloatingTipModule.ExFloatingTip(btnResetExtensions, "已恢复为默认后缀名");
        }

        private void numTimeoutSsim_ValueChanged(object? sender, EventArgs e)
        {

        }

    }
}
