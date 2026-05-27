using System;
using System.Drawing;
using System.Windows.Forms;
using LakeUI;
using AvifEncoder.GuiLakeUl.选项窗口;

namespace AvifEncoder.GuiLakeUl
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

        private void InitializeComponent()
        {
            modernPanel1 = new ModernPanel();
            label1 = new Label();
            btnSelectFont2 = new ModernButton();
            btnSelectFont = new ModernButton();
            btnSaveConfig = new ModernButton();
            btnLoadConfig = new ModernButton();
            btnCheckUpdate = new ModernButton();
            modernPanel1.SuspendLayout();
            SuspendLayout();
            // 
            // modernPanel1
            // 
            modernPanel1.BackColor1 = Color.Black;
            modernPanel1.BorderColor = Color.Transparent;
            modernPanel1.Controls.Add(label1);
            modernPanel1.Controls.Add(btnSelectFont2);
            modernPanel1.Controls.Add(btnSelectFont);
            modernPanel1.Controls.Add(btnSaveConfig);
            modernPanel1.Controls.Add(btnLoadConfig);
            modernPanel1.Controls.Add(btnCheckUpdate);
            modernPanel1.Dock = DockStyle.Fill;
            modernPanel1.Location = new Point(0, 0);
            modernPanel1.Name = "modernPanel1";
            modernPanel1.Size = new Size(1114, 681);
            modernPanel1.TabIndex = 0;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            label1.ForeColor = SystemColors.Highlight;
            label1.Location = new Point(20, 36);
            label1.Name = "label1";
            label1.Size = new Size(376, 21);
            label1.TabIndex = 4;
            label1.Text = "该页选项大部分功能为测试版，如遇bug为正常情况";
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
            // FormOtherOptions
            // 
            BackColor = Color.Black;
            ClientSize = new Size(1114, 681);
            Controls.Add(modernPanel1);
            Name = "FormOtherOptions";
            modernPanel1.ResumeLayout(false);
            modernPanel1.PerformLayout();
            ResumeLayout(false);
        }

        public ModernPanel modernPanel1 = null!;
        private ModernButton btnSelectFont = null!;
        private ModernButton btnSaveConfig = null!;
        private ModernButton btnSelectFont2 = null!;
        private Label label1 = null!;
        private ModernButton btnLoadConfig = null!;
        private ModernButton btnCheckUpdate = null!;
        // 设计器生成的代码区域（此处略，但应包括：modernPanel1、btnSelectFont、lblFontPreview 等控件）
    }
}