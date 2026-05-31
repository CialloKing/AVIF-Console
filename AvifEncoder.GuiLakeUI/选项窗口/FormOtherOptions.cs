using System;
using System.Drawing;
using System.Windows.Forms;
using LakeUI;
using AvifEncoder.GuiLakeUI.选项窗口;

namespace AvifEncoder.GuiLakeUI
{
    public partial class FormOtherOptions : Form
    {
        // 假设已通过设计器拖放了 modernPanel1 和按钮等，此处仅给出逻辑
        // 若没有设计器，纯代码构建也可，下面示例使用设计器假设，核心是按钮事件
        private Font? _currentFont;

        public FormOtherOptions()
        {
            InitializeComponent();
            this.btnSelectFont.Click += btnSelectFont_Click;
            this.btnSaveConfig.Click += btnSaveConfig_Click;
            this.btnLoadConfig.Click += btnLoadConfig_Click;
            this.btnCheckUpdate.Click += btnCheckUpdate_Click;
            // 默认与主窗口字体一致
            if (Application.OpenForms["Form1"] is Form1 mainForm && mainForm.Font != null)
                _currentFont = mainForm.Font;
        }




        private void btnSelectFont_Click(object? sender, EventArgs e)
        {
            using var fontDlg = new ModernFontDialog();
            // 必须附着到主窗口的窗口管理器，否则可能导致 UI 卡死或异常退出
            if (Application.OpenForms["Form1"] is Form1 mainForm)
            {
                mainForm.AttachDialog(fontDlg);
            }
            if (fontDlg.ShowDialog(this) == DialogResult.OK)
            {
                Font selectedFont = fontDlg.SelectedFont;
                _currentFont = selectedFont;
                btnSelectFont.Font = selectedFont;
                btnSelectFont.Text = $"{selectedFont.Name}, {selectedFont.Size}pt";
                ApplyFontToApp(selectedFont);
            }
        }

        private void btnSelectFont_Click2(object? sender, EventArgs e)
        {
            using var fontDlg = new FontDialog();
            // 可选：将当前已选字体预置到对话框中
            if (_currentFont != null)
                fontDlg.Font = _currentFont;
            if (fontDlg.ShowDialog(this) == DialogResult.OK)
            {
                Font selectedFont = fontDlg.Font;
                _currentFont = selectedFont;
                btnSelectFont.Font = selectedFont;
                btnSelectFont.Text = $"{selectedFont.Name}, {selectedFont.Size}pt";
                ApplyFontToApp(selectedFont);
            }
        }

        private void btnSaveConfig_Click(object? sender, EventArgs e)
        {
            using var sfd = new SaveFileDialog
            {
                Filter = "JSON文件|*.json|所有文件|*.*",
                DefaultExt = "json",
                FileName = "app_settings.json",
                Title = "保存配置文件"
            };
            if (sfd.ShowDialog(this) == DialogResult.OK)
            {
                AppConfig config = BuildConfigFromCurrentState();
                AppConfigHelper.SaveToFile(config, sfd.FileName);
                MessageBox.Show("配置已保存", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void btnLoadConfig_Click(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "JSON文件|*.json|所有文件|*.*",
                Title = "加载配置文件"
            };
            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                AppConfig? config = AppConfigHelper.LoadFromFile(ofd.FileName);
                if (config != null)
                {
                    ApplyConfig(config);
                    MessageBox.Show("配置已加载并应用", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("配置文件无效或损坏", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // 辅助方法：构建当前配置对象（含字体与窗口状态）
        private AppConfig BuildConfigFromCurrentState()
        {
            var cfg = new AppConfig();
            if (_currentFont != null)
            {
                cfg.FontFamily = _currentFont.FontFamily.Name;
                cfg.FontSize = _currentFont.Size;
            }
            if (Application.OpenForms["Form1"] is Form1 mainForm)
            {
                cfg.WindowWidth = mainForm.Width;
                cfg.WindowHeight = mainForm.Height;
                cfg.WindowLeft = mainForm.Left;
                cfg.WindowTop = mainForm.Top;
                cfg.Maximized = mainForm.WindowState == FormWindowState.Maximized;

                // ★ 同步收集编码设置
                mainForm.BuildEncodeConfig(cfg);
            }
            return cfg;
        }

        // 应用配置（字体全局 + 窗口状态）
        private void ApplyConfig(AppConfig config)
        {
            // 字体
            try
            {
                var font = new Font(config.FontFamily, config.FontSize);
                _currentFont = font;
                ApplyFontToApp(font);
                btnSelectFont.Font = font;
                btnSelectFont.Text = $"{font.Name}, {font.Size}pt";
            }
            catch { /* 忽略无效字体 */ }
            // 窗口状态 + 编码设置
            if (Application.OpenForms["Form1"] is Form1 mainForm)
            {
                mainForm.SuspendLayout();
                mainForm.WindowState = config.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
                if (!config.Maximized)
                {
                    mainForm.Left = Math.Max(0, config.WindowLeft);
                    mainForm.Top = Math.Max(0, config.WindowTop);
                    mainForm.Width = Math.Max(100, config.WindowWidth);
                    mainForm.Height = Math.Max(100, config.WindowHeight);
                }
                mainForm.ResumeLayout();

                // ★ 同步恢复编码设置
                mainForm.ApplyEncodeConfig(config);
            }
        }

        private void ApplyFontToApp(Font font)
        {
            if (Application.OpenForms["Form1"] is Form1 mainForm)
                mainForm.ApplyGlobalFont(font);
        }

        /// <summary>
        /// 供主窗口在加载默认配置后调用，同步当前字体状态到按钮显示。
        /// </summary>
        public void SyncCurrentFont(Font font)
        {
            if (font == null)
            {
                return;
            }
            _currentFont = font;
            btnSelectFont.Font = font;
            btnSelectFont.Text = $"{font.Name}, {font.Size}pt";
        }

        private async void btnCheckUpdate_Click(object? sender,
            EventArgs e)
        {
            btnCheckUpdate.Enabled = false;
            btnCheckUpdate.Text = "正在检查...";

            try
            {
                var manager = new UpdateManager();
                var release =
                    await manager.CheckForUpdateAsync();

                if (release == null || !release.Success)
                {
                    MessageBox.Show(
                        release?.Error ?? "当前已是最新版本",
                        "检查更新",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    using var frm = new FormUpdate(release);
                    if (Application.OpenForms["Form1"]
                        is Form1 mainForm)
                    {
                        mainForm.AttachDialog(frm);
                    }
                    frm.StartPosition =
                        FormStartPosition.CenterParent;
                    frm.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"检查更新失败：{ex.Message}",
                    "检查更新",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                btnCheckUpdate.Enabled = true;
                btnCheckUpdate.Text = "检查更新";
            }
        }

        private void BtnOpenOutput_Click(object? sender, EventArgs e)
        {
            string? outputDir = null;
            if (Application.OpenForms["Form1"] is Form1 mainForm)
            {
                var encodePage = mainForm.GetEncodePage();
                if (encodePage != null)
                    outputDir = encodePage.GetOutputDir();
            }
            if (string.IsNullOrEmpty(outputDir))
            {
                MessageBox.Show("请先在编码页面设置输出目录。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { System.Diagnostics.Process.Start("explorer.exe", outputDir); }
            catch (Exception ex) { MessageBox.Show($"无法打开: {ex.Message}"); }
        }

        private async void BtnSysInfo_Click(object? sender, EventArgs e)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($".NET 版本: {Environment.Version}");
            sb.AppendLine($"OS: {Environment.OSVersion}");
            sb.AppendLine($"处理器核心数: {Environment.ProcessorCount}");
            sb.AppendLine($"工作集内存: {Environment.WorkingSet / 1024 / 1024} MB");
            sb.AppendLine();

            string? ffmpeg = EncoderUtils.FindExecutable("ffmpeg");
            sb.AppendLine($"ffmpeg: {(ffmpeg ?? "未找到")}");
            if (ffmpeg != null)
            {
                var v = await AvifEnvironmentChecker.GetEncoderVersionInfoAsync(ffmpeg);
                sb.AppendLine($"  ffmpeg 版本: {v.FfmpegVersion}");
                foreach (var kv in v.EncoderVersions)
                    sb.AppendLine($"  {kv.Key}: {kv.Value}");
            }

            sb.AppendLine();
            sb.AppendLine($"ssimulacra2: {(EncoderUtils.FindExecutable("ssimulacra2") ?? "未找到")}");
            sb.AppendLine($"butteraugli_main: {(EncoderUtils.FindExecutable("butteraugli_main") ?? "未找到")}");

            MessageBox.Show(sb.ToString(), "系统信息",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void BtnResetDefault_Click(object? sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "确定要重置所有设置到出厂默认值吗？\n此操作不可撤销。",
                "确认重置", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return;

            if (Application.OpenForms["Form1"] is Form1 mainForm)
            {
                mainForm.ResetToDefaults();
            }
            MessageBox.Show("已重置为默认设置。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void InitializeComponent()
        {
            modernPanel1 = new ModernPanel();
            modernButton1 = new ModernButton();
            btnSelectFont2 = new ModernButton();
            btnSelectFont = new ModernButton();
            btnSaveConfig = new ModernButton();
            btnLoadConfig = new ModernButton();
            btnCheckUpdate = new ModernButton();
            btnOpenOutput = new ModernButton();
            btnSysInfo = new ModernButton();
            btnResetDefault = new ModernButton();
            modernPanel1.SuspendLayout();
            SuspendLayout();
            // 
            // modernPanel1
            // 
            modernPanel1.BackColor = Color.Transparent;
            modernPanel1.BackColor1 = Color.Black;
            modernPanel1.BorderColor = Color.Transparent;
            modernPanel1.Controls.Add(modernButton1);
            modernPanel1.Controls.Add(btnSelectFont2);
            modernPanel1.Controls.Add(btnSelectFont);
            modernPanel1.Controls.Add(btnSaveConfig);
            modernPanel1.Controls.Add(btnLoadConfig);
            modernPanel1.Controls.Add(btnCheckUpdate);
            modernPanel1.Controls.Add(btnOpenOutput);
            modernPanel1.Controls.Add(btnSysInfo);
            modernPanel1.Controls.Add(btnResetDefault);
            modernPanel1.Dock = DockStyle.Fill;
            modernPanel1.ForeColor = Color.Transparent;
            modernPanel1.Location = new Point(0, 0);
            modernPanel1.Name = "modernPanel1";
            modernPanel1.Size = new Size(1114, 681);
            modernPanel1.TabIndex = 0;
            // 
            // modernButton1
            // 
            modernButton1.AnimationFPS = 0;
            modernButton1.BackColor1 = Color.Transparent;
            modernButton1.BorderColor = Color.Transparent;
            modernButton1.Dock = DockStyle.Top;
            modernButton1.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Regular, GraphicsUnit.Point, 134);
            modernButton1.ForeColor = Color.DodgerBlue;
            modernButton1.Location = new Point(1, 1);
            modernButton1.Margin = new Padding(2);
            modernButton1.Name = "modernButton1";
            modernButton1.Size = new Size(1111, 52);
            modernButton1.TabIndex = 9;
            modernButton1.Text = "该页选项为测试版，如遇bug属正常情况";
            // 
            // btnSelectFont2
            // 
            btnSelectFont2.BackColor1 = Color.Transparent;
            btnSelectFont2.ForeColor = Color.WhiteSmoke;
            btnSelectFont2.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            btnSelectFont2.Location = new Point(20, 131);
            btnSelectFont2.Margin = new Padding(2);
            btnSelectFont2.Name = "btnSelectFont2";
            btnSelectFont2.Size = new Size(416, 40);
            btnSelectFont2.TabIndex = 3;
            btnSelectFont2.Text = "切换字体2";
            btnSelectFont2.Click += btnSelectFont_Click2;
            // 
            // btnSelectFont
            // 
            btnSelectFont.BackColor1 = Color.Transparent;
            btnSelectFont.ForeColor = Color.WhiteSmoke;
            btnSelectFont.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            btnSelectFont.Location = new Point(20, 71);
            btnSelectFont.Margin = new Padding(2);
            btnSelectFont.Name = "btnSelectFont";
            btnSelectFont.Size = new Size(416, 40);
            btnSelectFont.TabIndex = 0;
            btnSelectFont.Text = "切换字体";
            // 
            // btnSaveConfig
            // 
            btnSaveConfig.BackColor1 = Color.Transparent;
            btnSaveConfig.ForeColor = Color.WhiteSmoke;
            btnSaveConfig.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            btnSaveConfig.Location = new Point(20, 187);
            btnSaveConfig.Margin = new Padding(2);
            btnSaveConfig.Name = "btnSaveConfig";
            btnSaveConfig.Size = new Size(416, 40);
            btnSaveConfig.TabIndex = 1;
            btnSaveConfig.Text = "保存配置到文件";
            // 
            // btnLoadConfig
            // 
            btnLoadConfig.BackColor1 = Color.Transparent;
            btnLoadConfig.ForeColor = Color.WhiteSmoke;
            btnLoadConfig.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            btnLoadConfig.Location = new Point(20, 249);
            btnLoadConfig.Margin = new Padding(2);
            btnLoadConfig.Name = "btnLoadConfig";
            btnLoadConfig.Size = new Size(416, 40);
            btnLoadConfig.TabIndex = 2;
            btnLoadConfig.Text = "从文件加载配置";
            // 
            // btnCheckUpdate
            // 
            btnCheckUpdate.BackColor1 = Color.Transparent;
            btnCheckUpdate.ForeColor = Color.WhiteSmoke;
            btnCheckUpdate.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            btnCheckUpdate.Location = new Point(20, 313);
            btnCheckUpdate.Margin = new Padding(2);
            btnCheckUpdate.Name = "btnCheckUpdate";
            btnCheckUpdate.Size = new Size(416, 40);
            btnCheckUpdate.TabIndex = 5;
            btnCheckUpdate.Text = "检查更新";
            // 
            // btnOpenOutput
            // 
            btnOpenOutput.BackColor1 = Color.Transparent;
            btnOpenOutput.ForeColor = Color.WhiteSmoke;
            btnOpenOutput.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            btnOpenOutput.Location = new Point(20, 367);
            btnOpenOutput.Margin = new Padding(2);
            btnOpenOutput.Name = "btnOpenOutput";
            btnOpenOutput.Size = new Size(416, 40);
            btnOpenOutput.TabIndex = 6;
            btnOpenOutput.Text = "打开输出/日志目录";
            btnOpenOutput.Click += BtnOpenOutput_Click;
            // 
            // btnSysInfo
            // 
            btnSysInfo.BackColor1 = Color.Transparent;
            btnSysInfo.ForeColor = Color.WhiteSmoke;
            btnSysInfo.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            btnSysInfo.Location = new Point(20, 419);
            btnSysInfo.Margin = new Padding(2);
            btnSysInfo.Name = "btnSysInfo";
            btnSysInfo.Size = new Size(416, 40);
            btnSysInfo.TabIndex = 7;
            btnSysInfo.Text = "系统信息";
            btnSysInfo.Click += BtnSysInfo_Click;
            // 
            // btnResetDefault
            // 
            btnResetDefault.BackColor1 = Color.Transparent;
            btnResetDefault.ForeColor = Color.WhiteSmoke;
            btnResetDefault.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            btnResetDefault.Location = new Point(20, 471);
            btnResetDefault.Margin = new Padding(2);
            btnResetDefault.Name = "btnResetDefault";
            btnResetDefault.Size = new Size(416, 40);
            btnResetDefault.TabIndex = 8;
            btnResetDefault.Text = "重置为默认设置";
            btnResetDefault.Click += BtnResetDefault_Click;
            // 
            // FormOtherOptions
            // 
            BackColor = Color.Black;
            ClientSize = new Size(1114, 681);
            Controls.Add(modernPanel1);
            Name = "FormOtherOptions";
            modernPanel1.ResumeLayout(false);
            ResumeLayout(false);
        }

        public ModernPanel modernPanel1 = null!;
        private ModernButton btnSelectFont = null!;
        private ModernButton btnSaveConfig = null!;
        private ModernButton btnSelectFont2 = null!;
        private ModernButton btnLoadConfig = null!;
        private ModernButton btnCheckUpdate = null!;
        private ModernButton btnOpenOutput = null!;
        private ModernButton btnSysInfo = null!;
        private ModernButton modernButton1 = null!;
        private ModernButton btnResetDefault = null!;


    }
}