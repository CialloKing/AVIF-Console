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
            txtExtensions.Text = ".jpg,.jpeg,.png,.webp";
            numTimeoutEncode!.Maximum = double.MaxValue;
            numTimeoutSearch!.Maximum = double.MaxValue;
            numTimeoutSafe!.Maximum = double.MaxValue;
            numTimeoutSsim!.Maximum = double.MaxValue;
            numTimeoutEncode!.Value = 0;
            numTimeoutSearch!.Value = 60;
            numTimeoutSafe!.Value = 180;
            numTimeoutSsim!.Value = 5;
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

        private void InitializeComponent()
        {
            modernPanel1 = new ModernPanel();
            chkDryRun = new ModernCheckBox();
            chkVerbose = new ModernCheckBox();
            numTimeoutSsim = new ModernNumericUpDown();
            numTimeoutSafe = new ModernNumericUpDown();
            numTimeoutSearch = new ModernNumericUpDown();
            numTimeoutEncode = new ModernNumericUpDown();
            modernButton1 = new ModernButton();
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
            modernPanel1.Controls.Add(chkDryRun);
            modernPanel1.Controls.Add(chkVerbose);
            modernPanel1.Controls.Add(numTimeoutSsim);
            modernPanel1.Controls.Add(numTimeoutSafe);
            modernPanel1.Controls.Add(numTimeoutSearch);
            modernPanel1.Controls.Add(numTimeoutEncode);
            modernPanel1.Controls.Add(modernButton1);
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
            modernPanel1.Size = new Size(1256, 681);
            modernPanel1.TabIndex = 0;
            // 
            // chkDryRun
            // 
            chkDryRun.AnimationFPS = 0;
            chkDryRun.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkDryRun.BoxUncheckedBackColor = Color.FromArgb(30, 50, 50, 50);
            chkDryRun.ForeColor = Color.WhiteSmoke;
            chkDryRun.Location = new Point(731, 287);
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
            chkVerbose.Location = new Point(731, 251);
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
            numTimeoutSsim.Location = new Point(12, 445);
            numTimeoutSsim.Name = "numTimeoutSsim";
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
            numTimeoutSafe.Location = new Point(12, 378);
            numTimeoutSafe.Name = "numTimeoutSafe";
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
            numTimeoutSearch.Location = new Point(12, 310);
            numTimeoutSearch.Name = "numTimeoutSearch";
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
            numTimeoutEncode.Location = new Point(12, 243);
            numTimeoutEncode.Name = "numTimeoutEncode";
            numTimeoutEncode.Size = new Size(160, 32);
            numTimeoutEncode.TabIndex = 59;
            // 
            // modernButton1
            // 
            modernButton1.BackColor1 = Color.Transparent;
            modernButton1.BorderColor = Color.White;
            modernButton1.BorderRadius = 10;
            modernButton1.ForeColor = Color.White;
            modernButton1.Location = new Point(11, 54);
            modernButton1.Margin = new Padding(2);
            modernButton1.Name = "modernButton1";
            modernButton1.Size = new Size(591, 40);
            modernButton1.TabIndex = 15;
            modernButton1.Text = "图片后缀名，使用英文逗号分隔，默认为.jpg,.jpeg,.png,.webp这4种，可按需添加";
            modernButton1.TextAlign = ModernButton.TextAlignEnum.Left;
            // 
            // txtExtensions
            // 
            txtExtensions.AllowDrop = true;
            txtExtensions.BackColor1 = Color.Transparent;
            txtExtensions.BorderColorFocus = Color.White;
            txtExtensions.ForeColor = Color.WhiteSmoke;
            txtExtensions.Location = new Point(11, 111);
            txtExtensions.Margin = new Padding(2);
            txtExtensions.Name = "txtExtensions";
            txtExtensions.Size = new Size(870, 32);
            txtExtensions.TabIndex = 14;
            // 
            // lblTimeout
            // 
            lblTimeout.AutoSize = true;
            lblTimeout.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold, GraphicsUnit.Point, 134);
            lblTimeout.ForeColor = Color.WhiteSmoke;
            lblTimeout.Location = new Point(12, 178);
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
            lblTimeoutEncode.Location = new Point(12, 220);
            lblTimeoutEncode.Name = "lblTimeoutEncode";
            lblTimeoutEncode.Size = new Size(140, 20);
            lblTimeoutEncode.TabIndex = 4;
            lblTimeoutEncode.Text = "单次编码超时 (0=自动):";
            // 
            // lblTimeoutSearch
            // 
            lblTimeoutSearch.AutoSize = true;
            lblTimeoutSearch.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 134);
            lblTimeoutSearch.ForeColor = Color.WhiteSmoke;
            lblTimeoutSearch.Location = new Point(12, 287);
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
            lblTimeoutSafe.Location = new Point(12, 355);
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
            lblTimeoutSsim.Location = new Point(12, 422);
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
            ClientSize = new Size(1256, 681);
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
        private ModernButton modernButton1 = null!;
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
    }
}
