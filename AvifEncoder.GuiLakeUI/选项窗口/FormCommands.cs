using AvifEncoder;
using LakeUI;
using System;
using System.Linq;
using System.Windows.Forms;

namespace AvifEncoder.GuiLakeUI.选项窗口
{
    public partial class FormCommands : Form
    {
        private FormEncode? _encodePage;
        private FormOptions? _optionsPage;
        private string _currentEncoder = "libaom-av1";

        public FormCommands()
        {
            InitializeComponent();
            txtExtensions.Text = ".jpg,.jpeg,.png,.webp,.gif";
            txtEncoderParams.TextChanged += (s, e) => { RefreshParamsPreview(); _optionsPage?.SchedulePreviewRefresh(); };
            btnResetEncoderParams.Click += (s, e) =>
            {
                txtEncoderParams.Text = FormOptions.GetDefaultPrivateParams(_currentEncoder);
                RefreshParamsPreview();
            };
            btnCopyFfmpegCommand.Click += (s, e) =>
            {
                try { Clipboard.SetText(txtParamsPreview.Text); } catch { }
            };
            btnResetExtensions.Click += (s, e) =>
            {
                txtExtensions.Text = ".jpg,.jpeg,.png,.webp,.gif";
            };
            btnAnimatedCommand.Click += (s, e) => ResetAnimatedCommand();
        }

        public void SetEncodePage(FormEncode page) => _encodePage = page;
        public void SetOptionsPage(FormOptions page) => _optionsPage = page;

        // ═══════════ API ═══════════
        public string GetExtensions() => txtExtensions.Text.Trim();
        public void SetExtensions(string v) => txtExtensions.Text = v ?? "";
        public string GetEncoderCustomParams() => txtEncoderParams.Text.Trim();
        public void SetEncoderCustomParams(string v) => txtEncoderParams.Text = v ?? "";
        public string GetAnimatedCommand() => txtAnimatedCommand?.Text.Trim() ?? "";
        public void SetAnimatedCommand(string v) { if (txtAnimatedCommand != null) txtAnimatedCommand.Text = v ?? ""; }

        public void UpdateEncoderDefaultParams(string encoder)
        {
            _currentEncoder = encoder;
            string defaults = FormOptions.GetDefaultPrivateParams(encoder);
            if (string.IsNullOrEmpty(txtEncoderParams.Text) || IsDefaultParams(txtEncoderParams.Text))
                txtEncoderParams.Text = defaults;
        }

        // ═══════════ 刷新 ═══════════
        public void RefreshParamsPreview()
        {
            if (_encodePage == null) return;
            var ctx = _encodePage.GetPreviewContext();
            string custom = txtEncoderParams.Text.Trim();
            string preview = FormOptions.BuildFullFfmpegPreview(ctx, custom);
            if (txtParamsPreview.Text != preview)
                txtParamsPreview.Text = preview;
        }

        public void UpdateAnimatedCommand()
        {
            if (_encodePage == null) return;
            var ctx = _encodePage.GetPreviewContext();
            string cmd = FormOptions.BuildDefaultAnimatedCommand(ctx);
            if (txtAnimatedCommand.Text != cmd)
                txtAnimatedCommand.Text = cmd;
        }

        public void ResetAnimatedCommand()
        {
            UpdateAnimatedCommand();
            LakeUI.ExFloatingTipModule.ExFloatingTip(btnAnimatedCommand, "已恢复为默认动图命令");
        }

        private bool IsDefaultParams(string text)
        {
            foreach (var enc in new[] { "libaom-av1", "libsvtav1", "librav1e", "av1_nvenc", "av1_qsv", "av1_amf", "av1_vaapi" })
            {
                if (text.Trim() == FormOptions.GetDefaultPrivateParams(enc).Trim()) return true;
            }
            return false;
        }
    }
}
