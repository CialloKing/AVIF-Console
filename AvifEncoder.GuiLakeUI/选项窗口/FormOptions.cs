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

            numDenoise!.ValueChanged += (s, e) =>
            {
                UpdateEncoderParamsWithDenoise();
                RefreshFfmpegPreview();
            };

            // --skip-metrics 复选框默认全部勾选（即默认计算所有指标）
            chkMetricXpsnr!.Checked = true;
            chkMetricSsimu2!.Checked = true;
            chkMetricButter3!.Checked = true;
            chkMetricGmsd!.Checked = true;
            chkMetricPsnrUncapped!.Checked = true;
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

        public string GetExtensions() => _commandsPage?.GetExtensions() ?? "";
        public void SetExtensions(string v) => _commandsPage?.SetExtensions(v);

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

        // ═══════════════════════════════════════
        // --skip-metrics 复选框读写
        // ═══════════════════════════════════════

        /// <summary>收集 UI 中被取消勾选的指标键，写入 PresetConfig.SkippedMetrics（含搜索目标保护）</summary>
        public void ApplySkippedMetrics(PresetConfig config)
        {
            if (chkMetricXpsnr == null) return;
            var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!chkMetricXpsnr.Checked)       skipped.Add("xpsnr");
            if (!chkMetricSsimu2.Checked)      skipped.Add("ssimu2");
            if (!chkMetricButter3.Checked)     skipped.Add("butter3");
            if (!chkMetricGmsd.Checked)        skipped.Add("gmsd");
            if (!chkMetricPsnrUncapped.Checked) skipped.Add("psnr_uncapped");
            if (skipped.Count > 0)
            {
                // ★ 搜索目标指标保护（与 CLI BuildPresetConfig 逻辑一致）
                //    仅在搜索启用时执行 — 固定CRF/无损/遍历模式下无需保护, 避免误导性弹窗
                if (config.UseCRFSearch)
                {
                    string searchMetric = config.MetricMode ?? "vmaf";
                    string? protectedKey = searchMetric.ToLowerInvariant() switch
                    {
                        "xpsnr" or "xpsnr_y" or "xpsnr_u" or "xpsnr_v" or "xpsnr_w" => "xpsnr",
                        "ssimu2" => "ssimu2",
                        "butter3" => "butter3",
                        "gmsd" => "gmsd",
                        _ => null
                    };
                    if (protectedKey != null && skipped.Contains(protectedKey))
                    {
                        skipped.Remove(protectedKey);
                        MessageBox.Show(
                            $"搜索目标指标 '{searchMetric}' 为搜索必需，无法跳过。已自动恢复计算。",
                            "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }

                if (skipped.Count > 0)
                {
                    config.SkippedMetrics = skipped;
                }
            }
        }

        /// <summary>从 PresetConfig 恢复复选框勾选状态（勾选=计算，取消=跳过）</summary>
        public void LoadSkippedMetrics(PresetConfig config)
        {
            if (chkMetricXpsnr == null) return;
            chkMetricXpsnr.Checked       = !config.IsMetricSkipped("xpsnr");
            chkMetricSsimu2.Checked      = !config.IsMetricSkipped("ssimu2");
            chkMetricButter3.Checked     = !config.IsMetricSkipped("butter3");
            chkMetricGmsd.Checked        = !config.IsMetricSkipped("gmsd");
            chkMetricPsnrUncapped.Checked = !config.IsMetricSkipped("psnr_uncapped");
        }

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

        private ModernNumericUpDown numDenoise;
        private ModernCheckBox chkArnrMaxFrames;
        private Label label1 = null!;
        private ModernComboBox cmbRgbMode = null!;
        private Label label2 = null!;
        private string _currentEncoder = "libaom-av1";
        private FormEncode? _encodePage;
        private ModernCheckBox chkMetricXpsnr;
        private ModernCheckBox chkMetricSsimu2;
        private ModernCheckBox chkMetricButter3;
        private ModernCheckBox chkMetricGmsd;
        private ModernCheckBox chkMetricPsnrUncapped;
        private Label label3;
        private FormCommands? _commandsPage;

        /// <summary>由 Form1 调用，注入编码页引用。</summary>
        public void SetEncodePage(FormEncode encodePage) => _encodePage = encodePage;
        public void SetCommandsPage(FormCommands p) => _commandsPage = p;

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
            _commandsPage?.SetEncoderCustomParams(defaults);
        }

        /// <summary>返回各编码器的默认私有参数字符串（含 CLI 前缀）</summary>
        internal static string GetDefaultPrivateParams(string encoder)
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
            // ★ 预设应用期间每个控件赋值都触发此方法，11个控件=11次无谓刷新。
            //    检查 _encodePage.IsApplyingPreset 跳过，仅最终一次刷新有效。
            if (_encodePage?.IsApplyingPreset == true) return;
            UpdateParamsPreview();
            _commandsPage?.UpdateAnimatedCommand();
        }

        /// <summary>获取用户自定义的动图完整命令</summary>
        public string GetAnimatedCommand() => _commandsPage?.GetAnimatedCommand() ?? "";

        /// <summary>设置动图命令文本</summary>
        public void SetAnimatedCommand(string v) => _commandsPage?.SetAnimatedCommand(v);

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
            if (_commandsPage == null) return;
            FormEncode.PreviewContext ctx;
            try { ctx = _encodePage?.GetPreviewContext() ?? CreateDefaultContext(); }
            catch { ctx = CreateDefaultContext(); }
            string cmd = BuildDefaultAnimatedCommand(ctx);
            _commandsPage.SetAnimatedCommand(cmd);
        }

        /// <summary>外部调用的动图命令重置入口</summary>
        public void ResetAnimatedCommand()
        {
            if (_commandsPage == null) return;
            FormEncode.PreviewContext ctx;
            try { ctx = _encodePage?.GetPreviewContext() ?? CreateDefaultContext(); }
            catch { ctx = CreateDefaultContext(); }
            _commandsPage.SetAnimatedCommand(BuildDefaultAnimatedCommand(ctx));
        }

        private void TxtAnimatedCommand_TextChanged(object? sender, EventArgs e)
        {
            // 用户编辑动图命令时无需联动其他控件
        }

        private void BtnAnimatedCommand_Click(object? sender, EventArgs e)
        {
            ResetAnimatedCommand();
            LakeUI.ExFloatingTipModule.ExFloatingTip(_commandsPage?.btnAnimatedCommand, "已恢复为默认动图命令");
        }

        /// <summary>实时拼接完整的 ffmpeg 命令行预览</summary>
        private void UpdateParamsPreview()
        {
            FormEncode.PreviewContext ctx;
            try { ctx = _encodePage?.GetPreviewContext() ?? CreateDefaultContext(); }
            catch { ctx = CreateDefaultContext(); }
            string custom = _commandsPage?.GetEncoderCustomParams() ?? "";
            string preview = BuildFullFfmpegPreview(ctx, custom);
            if (_commandsPage?.txtParamsPreview.Text != preview)
            {
                if (_commandsPage != null) { _commandsPage.txtParamsPreview.Text = preview; _commandsPage.txtParamsPreview.Refresh(); }
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
            label3 = new Label();
            chkMetricPsnrUncapped = new ModernCheckBox();
            chkMetricGmsd = new ModernCheckBox();
            chkMetricButter3 = new ModernCheckBox();
            chkMetricSsimu2 = new ModernCheckBox();
            chkMetricXpsnr = new ModernCheckBox();
            label2 = new Label();
            cmbRgbMode = new ModernComboBox();
            label1 = new Label();
            chkArnrMaxFrames = new ModernCheckBox();
            numDenoise = new ModernNumericUpDown();
            chkDryRun = new ModernCheckBox();
            chkVerbose = new ModernCheckBox();
            numTimeoutSsim = new ModernNumericUpDown();
            numTimeoutSafe = new ModernNumericUpDown();
            numTimeoutSearch = new ModernNumericUpDown();
            numTimeoutEncode = new ModernNumericUpDown();
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
            modernPanel1.Controls.Add(label3);
            modernPanel1.Controls.Add(chkMetricPsnrUncapped);
            modernPanel1.Controls.Add(chkMetricGmsd);
            modernPanel1.Controls.Add(chkMetricButter3);
            modernPanel1.Controls.Add(chkMetricSsimu2);
            modernPanel1.Controls.Add(chkMetricXpsnr);
            modernPanel1.Controls.Add(label2);
            modernPanel1.Controls.Add(cmbRgbMode);
            modernPanel1.Controls.Add(label1);
            modernPanel1.Controls.Add(chkArnrMaxFrames);
            modernPanel1.Controls.Add(numDenoise);
            modernPanel1.Controls.Add(chkDryRun);
            modernPanel1.Controls.Add(chkVerbose);
            modernPanel1.Controls.Add(numTimeoutSsim);
            modernPanel1.Controls.Add(numTimeoutSafe);
            modernPanel1.Controls.Add(numTimeoutSearch);
            modernPanel1.Controls.Add(numTimeoutEncode);
            modernPanel1.Controls.Add(lblTimeout);
            modernPanel1.Controls.Add(lblTimeoutEncode);
            modernPanel1.Controls.Add(lblTimeoutSearch);
            modernPanel1.Controls.Add(lblTimeoutSafe);
            modernPanel1.Controls.Add(lblTimeoutSsim);
            modernPanel1.Dock = DockStyle.Fill;
            modernPanel1.ForeColor = Color.Transparent;
            modernPanel1.Location = new Point(0, 0);
            modernPanel1.Name = "modernPanel1";
            modernPanel1.Size = new Size(1114, 681);
            modernPanel1.TabIndex = 0;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold, GraphicsUnit.Point, 134);
            label3.ForeColor = Color.WhiteSmoke;
            label3.Location = new Point(264, 129);
            label3.Name = "label3";
            label3.Size = new Size(240, 26);
            label3.TabIndex = 83;
            label3.Text = "CSV导出质量指标计算设置";
            // 
            // chkMetricPsnrUncapped
            // 
            chkMetricPsnrUncapped.AnimationFPS = 0;
            chkMetricPsnrUncapped.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkMetricPsnrUncapped.BoxUncheckedBackColor = Color.FromArgb(30, 50, 50, 50);
            chkMetricPsnrUncapped.ForeColor = Color.WhiteSmoke;
            chkMetricPsnrUncapped.Location = new Point(269, 293);
            chkMetricPsnrUncapped.Name = "chkMetricPsnrUncapped";
            chkMetricPsnrUncapped.Size = new Size(363, 24);
            chkMetricPsnrUncapped.TabIndex = 82;
            chkMetricPsnrUncapped.Text = "PSNR上限突破重计算（仅 PSNR≥59.5dB 时触发）";
            // 
            // chkMetricGmsd
            // 
            chkMetricGmsd.AnimationFPS = 0;
            chkMetricGmsd.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkMetricGmsd.BoxUncheckedBackColor = Color.FromArgb(30, 50, 50, 50);
            chkMetricGmsd.ForeColor = Color.WhiteSmoke;
            chkMetricGmsd.Location = new Point(269, 262);
            chkMetricGmsd.Name = "chkMetricGmsd";
            chkMetricGmsd.Size = new Size(150, 24);
            chkMetricGmsd.TabIndex = 81;
            chkMetricGmsd.Text = "GMSD";
            // 
            // chkMetricButter3
            // 
            chkMetricButter3.AnimationFPS = 0;
            chkMetricButter3.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkMetricButter3.BoxUncheckedBackColor = Color.FromArgb(30, 50, 50, 50);
            chkMetricButter3.ForeColor = Color.WhiteSmoke;
            chkMetricButter3.Location = new Point(269, 232);
            chkMetricButter3.Name = "chkMetricButter3";
            chkMetricButter3.Size = new Size(150, 24);
            chkMetricButter3.TabIndex = 80;
            chkMetricButter3.Text = "Butteraugli";
            // 
            // chkMetricSsimu2
            // 
            chkMetricSsimu2.AnimationFPS = 0;
            chkMetricSsimu2.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkMetricSsimu2.BoxUncheckedBackColor = Color.FromArgb(30, 50, 50, 50);
            chkMetricSsimu2.ForeColor = Color.WhiteSmoke;
            chkMetricSsimu2.Location = new Point(269, 202);
            chkMetricSsimu2.Name = "chkMetricSsimu2";
            chkMetricSsimu2.Size = new Size(150, 24);
            chkMetricSsimu2.TabIndex = 79;
            chkMetricSsimu2.Text = "SSIMULACRA2";
            // 
            // chkMetricXpsnr
            // 
            chkMetricXpsnr.AnimationFPS = 0;
            chkMetricXpsnr.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkMetricXpsnr.BoxUncheckedBackColor = Color.FromArgb(30, 50, 50, 50);
            chkMetricXpsnr.ForeColor = Color.WhiteSmoke;
            chkMetricXpsnr.Location = new Point(269, 167);
            chkMetricXpsnr.Name = "chkMetricXpsnr";
            chkMetricXpsnr.Size = new Size(150, 24);
            chkMetricXpsnr.TabIndex = 78;
            chkMetricXpsnr.Text = "XPSNR";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(269, 57);
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
            cmbRgbMode.Location = new Point(269, 80);
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
            label1.Location = new Point(30, 30);
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
            chkArnrMaxFrames.Location = new Point(30, 50);
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
            numDenoise.Location = new Point(30, 80);
            numDenoise.Name = "numDenoise";
            numDenoise.Padding = new Padding(6, 0, 0, 0);
            numDenoise.Size = new Size(160, 32);
            numDenoise.TabIndex = 72;
            // 
            // chkDryRun
            // 
            chkDryRun.AnimationFPS = 0;
            chkDryRun.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkDryRun.BoxUncheckedBackColor = Color.FromArgb(30, 50, 50, 50);
            chkDryRun.ForeColor = Color.WhiteSmoke;
            chkDryRun.Location = new Point(634, 57);
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
            chkVerbose.Location = new Point(634, 88);
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
            numTimeoutSsim.Location = new Point(30, 377);
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
            numTimeoutSafe.Location = new Point(30, 316);
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
            numTimeoutSearch.Location = new Point(30, 255);
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
            numTimeoutEncode.Location = new Point(30, 194);
            numTimeoutEncode.Name = "numTimeoutEncode";
            numTimeoutEncode.Padding = new Padding(6, 0, 0, 0);
            numTimeoutEncode.Size = new Size(160, 32);
            numTimeoutEncode.TabIndex = 59;
            // 
            // lblTimeout
            // 
            lblTimeout.AutoSize = true;
            lblTimeout.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold, GraphicsUnit.Point, 134);
            lblTimeout.ForeColor = Color.WhiteSmoke;
            lblTimeout.Location = new Point(30, 129);
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
            lblTimeoutEncode.Location = new Point(30, 171);
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
            lblTimeoutSearch.Location = new Point(30, 232);
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
            lblTimeoutSafe.Location = new Point(30, 293);
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
            lblTimeoutSsim.Location = new Point(30, 354);
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
            ClientSize = new Size(1114, 681);
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
        private ModernNumericUpDown numTimeoutEncode = null!;
        private ModernNumericUpDown numTimeoutSearch = null!;
        private ModernNumericUpDown numTimeoutSafe = null!;
        private ModernNumericUpDown numTimeoutSsim = null!;
        private Label lblTimeout = null!;
        private ModernCheckBox chkDryRun = null!;
        private ModernCheckBox chkVerbose = null!;

        private void numTimeoutSsim_ValueChanged(object? sender, EventArgs e)
        {

        }

        // 供 FormCommands 同步读取
        public string GetParamsPreviewText() => _commandsPage?.txtParamsPreview?.Text ?? "";
        public string GetAnimatedCommandText() => _commandsPage?.txtAnimatedCommand?.Text ?? "";

        // 转发到 FormCommands（保持旧 API 兼容）
        public string GetEncoderCustomParams() => _commandsPage?.GetEncoderCustomParams() ?? "";
        public void SetEncoderCustomParams(string v) => _commandsPage?.SetEncoderCustomParams(v);
        public void UpdateEncoderDefaultParams(string? encoder)
        {
            // ★ 同步 _currentEncoder，确保降噪参数走正确的编码器分支
            //    此前仅在构造函数初始化为 libaom-av1，切换编码器后未更新导致降噪功能对非 libaom 失效
            if (!string.IsNullOrEmpty(encoder))
            {
                _currentEncoder = encoder;
            }
            _commandsPage?.UpdateEncoderDefaultParams(encoder ?? "");
        }
        public void SchedulePreviewRefresh() { }  // FormCommands 已自行刷新，此方法仅用于兼容

    }
}
