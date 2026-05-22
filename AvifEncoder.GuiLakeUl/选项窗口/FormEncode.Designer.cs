namespace AvifEncoder.GuiLakeUl.选项窗口
{
    partial class FormEncode
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            modernPanel1 = new LakeUI.ModernPanel();
            chkCRFSearch = new LakeUI.ModernCheckBox();
            txtQualityTarget = new LakeUI.ModernTextBox();
            lblQuality = new Label();
            cmbEncoder = new LakeUI.ModernComboBox();
            lblEncoder = new Label();
            cmbPreset = new LakeUI.ModernComboBox();
            lblPreset = new Label();
            progressBar1 = new LakeUI.ExcellentProgressBar();
            btnBrowseOutput = new LakeUI.ModernButton();
            txtOutput = new LakeUI.ModernTextBox();
            btnBrowseInput = new LakeUI.ModernButton();
            btnStart = new LakeUI.ModernButton();
            txtInput = new LakeUI.ModernTextBox();
            modernPanel1.SuspendLayout();
            SuspendLayout();
            // 
            // modernPanel1
            // 
            modernPanel1.Controls.Add(chkCRFSearch);
            modernPanel1.Controls.Add(txtQualityTarget);
            modernPanel1.Controls.Add(lblQuality);
            modernPanel1.Controls.Add(cmbEncoder);
            modernPanel1.Controls.Add(lblEncoder);
            modernPanel1.Controls.Add(cmbPreset);
            modernPanel1.Controls.Add(lblPreset);
            modernPanel1.Controls.Add(progressBar1);
            modernPanel1.Controls.Add(btnBrowseOutput);
            modernPanel1.Controls.Add(txtOutput);
            modernPanel1.Controls.Add(btnBrowseInput);
            modernPanel1.Controls.Add(btnStart);
            modernPanel1.Controls.Add(txtInput);
            modernPanel1.Dock = DockStyle.Fill;
            modernPanel1.Location = new Point(0, 0);
            modernPanel1.Name = "modernPanel1";
            modernPanel1.OverlayColor = Color.FromArgb(36, 36, 36);
            modernPanel1.ScrollBarMode = LakeUI.ModernPanel.ScrollMode.None;
            modernPanel1.Size = new Size(800, 450);
            modernPanel1.TabIndex = 0;
            modernPanel1.Scroll += modernPanel1_Scroll;
            // 
            // chkCRFSearch
            // 
            chkCRFSearch.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkCRFSearch.Location = new Point(638, 165);
            chkCRFSearch.Name = "chkCRFSearch";
            chkCRFSearch.Size = new Size(150, 40);
            chkCRFSearch.TabIndex = 14;
            chkCRFSearch.Text = "是否启用 CRF 搜索";
            // 
            // txtQualityTarget
            // 
            txtQualityTarget.Location = new Point(468, 173);
            txtQualityTarget.Margin = new Padding(2);
            txtQualityTarget.Name = "txtQualityTarget";
            txtQualityTarget.Size = new Size(120, 32);
            txtQualityTarget.TabIndex = 13;
            txtQualityTarget.Text = "输入目标质量值";
            // 
            // lblQuality
            // 
            lblQuality.AutoSize = true;
            lblQuality.ForeColor = Color.Gray;
            lblQuality.Location = new Point(468, 154);
            lblQuality.Name = "lblQuality";
            lblQuality.Size = new Size(80, 17);
            lblQuality.TabIndex = 12;
            lblQuality.Text = "质量指标目标";
            // 
            // cmbEncoder
            // 
            cmbEncoder.CaretColor = Color.FromArgb(220, 220, 220);
            cmbEncoder.DropDownAnimationFPS = 0;
            cmbEncoder.DropDownScrollBarHoverColor = Color.FromArgb(200, 200, 200);
            cmbEncoder.Location = new Point(251, 173);
            cmbEncoder.Margin = new Padding(2, 2, 2, 2);
            cmbEncoder.Name = "cmbEncoder";
            cmbEncoder.Size = new Size(160, 32);
            cmbEncoder.TabIndex = 11;
            cmbEncoder.Text = "选择编码器";
            // 
            // lblEncoder
            // 
            lblEncoder.AutoSize = true;
            lblEncoder.ForeColor = Color.Gray;
            lblEncoder.Location = new Point(251, 154);
            lblEncoder.Name = "lblEncoder";
            lblEncoder.Size = new Size(44, 17);
            lblEncoder.TabIndex = 10;
            lblEncoder.Text = "编码器";
            // 
            // cmbPreset
            // 
            cmbPreset.CaretColor = Color.FromArgb(220, 220, 220);
            cmbPreset.DropDownAnimationFPS = 0;
            cmbPreset.DropDownScrollBarHoverColor = Color.FromArgb(200, 200, 200);
            cmbPreset.Location = new Point(37, 173);
            cmbPreset.Margin = new Padding(2, 2, 2, 2);
            cmbPreset.Name = "cmbPreset";
            cmbPreset.Size = new Size(160, 32);
            cmbPreset.TabIndex = 9;
            cmbPreset.Text = "选择预设";
            // 
            // lblPreset
            // 
            lblPreset.AutoSize = true;
            lblPreset.ForeColor = Color.Gray;
            lblPreset.Location = new Point(37, 154);
            lblPreset.Name = "lblPreset";
            lblPreset.Size = new Size(56, 17);
            lblPreset.TabIndex = 8;
            lblPreset.Text = "编码预设";
            // 
            // progressBar1
            // 
            progressBar1.BorderColor = Color.Gainsboro;
            progressBar1.DisabledOverlayColor = Color.White;
            progressBar1.FillColor = Color.FromArgb(0, 120, 215);
            progressBar1.Location = new Point(37, 312);
            progressBar1.Margin = new Padding(2, 2, 2, 2);
            progressBar1.Name = "progressBar1";
            progressBar1.Size = new Size(752, 20);
            progressBar1.TabIndex = 7;
            progressBar1.TextPadding = new Padding(0);
            progressBar1.TrackColor = Color.White;
            // 
            // btnBrowseOutput
            // 
            btnBrowseOutput.BorderRadius = 10;
            btnBrowseOutput.Location = new Point(37, 95);
            btnBrowseOutput.Margin = new Padding(2);
            btnBrowseOutput.Name = "btnBrowseOutput";
            btnBrowseOutput.Size = new Size(120, 35);
            btnBrowseOutput.TabIndex = 6;
            btnBrowseOutput.Text = "浏览输出路径";
            btnBrowseOutput.Click += btnBrowseOutput_Click;
            // 
            // txtOutput
            // 
            txtOutput.Location = new Point(192, 98);
            txtOutput.Margin = new Padding(2);
            txtOutput.Name = "txtOutput";
            txtOutput.Size = new Size(597, 32);
            txtOutput.TabIndex = 5;
            txtOutput.Text = "输出路径";
            // 
            // btnBrowseInput
            // 
            btnBrowseInput.AnimationDuration = 0;
            btnBrowseInput.AnimationFPS = 0;
            btnBrowseInput.BorderRadius = 10;
            btnBrowseInput.Location = new Point(37, 30);
            btnBrowseInput.Margin = new Padding(2);
            btnBrowseInput.Name = "btnBrowseInput";
            btnBrowseInput.Size = new Size(120, 35);
            btnBrowseInput.TabIndex = 4;
            btnBrowseInput.Text = "浏览输入路径";
            btnBrowseInput.Click += btnBrowseInput_Click;
            // 
            // btnStart
            // 
            btnStart.AnimationDuration = 0;
            btnStart.AnimationFPS = 0;
            btnStart.BorderRadius = 10;
            btnStart.Location = new Point(37, 264);
            btnStart.Margin = new Padding(2);
            btnStart.Name = "btnStart";
            btnStart.Size = new Size(120, 35);
            btnStart.TabIndex = 2;
            btnStart.Text = "开始任务";
            btnStart.Click += btnStart_Click;
            // 
            // txtInput
            // 
            txtInput.Location = new Point(192, 30);
            txtInput.Margin = new Padding(2);
            txtInput.Name = "txtInput";
            txtInput.Size = new Size(597, 32);
            txtInput.TabIndex = 0;
            txtInput.Text = "输入路径";
            // 
            // FormEncode
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(modernPanel1);
            Name = "FormEncode";
            Text = "FormEncode";
            modernPanel1.ResumeLayout(false);
            modernPanel1.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        public LakeUI.ModernPanel modernPanel1;
        private LakeUI.ModernTextBox txtInput;
        private LakeUI.ModernButton btnStart;
        private LakeUI.ModernButton btnBrowseInput;
        private LakeUI.ModernTextBox txtOutput;
        private LakeUI.ModernButton btnBrowseOutput;
        private LakeUI.ExcellentProgressBar progressBar1;
        private Label lblPreset;
        private LakeUI.ModernComboBox cmbPreset;
        private Label lblEncoder;
        private LakeUI.ModernComboBox cmbEncoder;
        private Label lblQuality;
        private LakeUI.ModernTextBox txtQualityTarget;
        private LakeUI.ModernCheckBox chkCRFSearch;
    }
}