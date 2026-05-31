namespace AvifEncoder.GuiLakeUI.选项窗口
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
            cmbTemplate = new LakeUI.ModernComboBox();
            cmbPreset = new LakeUI.ModernComboBox();
            cmbEncoder = new LakeUI.ModernComboBox();
            cmbConflict = new LakeUI.ModernComboBox();
            cmbChroma = new LakeUI.ModernComboBox();
            cmbBitDepth = new LakeUI.ModernComboBox();
            cmbQualityMode = new LakeUI.ModernComboBox();
            numSearchCpuUsed = new LakeUI.ModernNumericUpDown();
            numFinalCpuUsed = new LakeUI.ModernNumericUpDown();
            numMaxRes = new LakeUI.ModernNumericUpDown();
            numJobs = new LakeUI.ModernNumericUpDown();
            numQualityValue = new LakeUI.ModernNumericUpDown();
            chkSweep = new LakeUI.ModernCheckBox();
            chkProxy = new LakeUI.ModernCheckBox();
            chkPriorSearch = new LakeUI.ModernCheckBox();
            chkSerialEncode = new LakeUI.ModernCheckBox();
            label11 = new Label();
            chkOutputFullRes = new LakeUI.ModernCheckBox();
            label10 = new Label();
            chkRecursive = new LakeUI.ModernCheckBox();
            chkLossless = new LakeUI.ModernCheckBox();
            label9 = new Label();
            label8 = new Label();
            label7 = new Label();
            label6 = new Label();
            cmbMetric = new LakeUI.ModernComboBox();
            grpCrfMode = new GroupBox();
            numCrfMax = new LakeUI.ModernNumericUpDown();
            numCrfMin = new LakeUI.ModernNumericUpDown();
            numCrfFix = new LakeUI.ModernNumericUpDown();
            rbCrfFix = new LakeUI.ModernCheckBox();
            label5 = new Label();
            rbCrfRange = new LakeUI.ModernCheckBox();
            label4 = new Label();
            txtTemplate = new LakeUI.ModernTextBox();
            label3 = new Label();
            label2 = new Label();
            label1 = new Label();
            btnStop = new LakeUI.ModernButton();
            chkSearch = new LakeUI.ModernCheckBox();
            lblQuality = new Label();
            lblEncoder = new Label();
            lblPreset = new Label();
            progressBar1 = new LakeUI.ExcellentProgressBar();
            btnBrowseOutput = new LakeUI.ModernButton();
            txtOutput = new LakeUI.ModernTextBox();
            btnBrowseInput = new LakeUI.ModernButton();
            btnStart = new LakeUI.ModernButton();
            txtInput = new LakeUI.ModernTextBox();
            btnResume = new LakeUI.ModernButton();
            btnAbandon = new LakeUI.ModernButton();
            modernPanel1.SuspendLayout();
            grpCrfMode.SuspendLayout();
            SuspendLayout();
            // 
            // modernPanel1
            // 
            modernPanel1.AllowDrop = true;
            modernPanel1.BackColor = Color.Transparent;
            modernPanel1.BackColor1 = Color.Black;
            modernPanel1.BorderColor = Color.Transparent;
            modernPanel1.Controls.Add(btnAbandon);
            modernPanel1.Controls.Add(btnResume);
            modernPanel1.Controls.Add(cmbTemplate);
            modernPanel1.Controls.Add(cmbPreset);
            modernPanel1.Controls.Add(cmbEncoder);
            modernPanel1.Controls.Add(cmbConflict);
            modernPanel1.Controls.Add(cmbChroma);
            modernPanel1.Controls.Add(cmbBitDepth);
            modernPanel1.Controls.Add(cmbQualityMode);
            modernPanel1.Controls.Add(numSearchCpuUsed);
            modernPanel1.Controls.Add(numFinalCpuUsed);
            modernPanel1.Controls.Add(numMaxRes);
            modernPanel1.Controls.Add(numJobs);
            modernPanel1.Controls.Add(numQualityValue);
            modernPanel1.Controls.Add(chkSweep);
            modernPanel1.Controls.Add(chkProxy);
            modernPanel1.Controls.Add(chkPriorSearch);
            modernPanel1.Controls.Add(chkSerialEncode);
            modernPanel1.Controls.Add(label11);
            modernPanel1.Controls.Add(chkOutputFullRes);
            modernPanel1.Controls.Add(label10);
            modernPanel1.Controls.Add(chkRecursive);
            modernPanel1.Controls.Add(chkLossless);
            modernPanel1.Controls.Add(label9);
            modernPanel1.Controls.Add(label8);
            modernPanel1.Controls.Add(label7);
            modernPanel1.Controls.Add(label6);
            modernPanel1.Controls.Add(cmbMetric);
            modernPanel1.Controls.Add(grpCrfMode);
            modernPanel1.Controls.Add(txtTemplate);
            modernPanel1.Controls.Add(label3);
            modernPanel1.Controls.Add(label2);
            modernPanel1.Controls.Add(label1);
            modernPanel1.Controls.Add(btnStop);
            modernPanel1.Controls.Add(chkSearch);
            modernPanel1.Controls.Add(lblQuality);
            modernPanel1.Controls.Add(lblEncoder);
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
            modernPanel1.ScrollBarMode = LakeUI.ModernPanel.ScrollMode.None;
            modernPanel1.Size = new Size(1114, 681);
            modernPanel1.TabIndex = 0;
            // 
            // cmbTemplate
            // 
            cmbTemplate.BackColor1 = Color.Transparent;
            cmbTemplate.BorderColor = Color.Gainsboro;
            cmbTemplate.BorderColorFocus = Color.White;
            cmbTemplate.DropDownAnimationFPS = 0;
            cmbTemplate.DropDownBackColor = Color.Transparent;
            cmbTemplate.DropDownBackdropBlurPasses = 2;
            cmbTemplate.DropDownBackdropBlurRadius = 5;
            cmbTemplate.DropDownBackdropMode = LakeUI.PopupBackdropMode.Auto;
            cmbTemplate.DropDownHoverColor = Color.FromArgb(128, 255, 255, 255);
            cmbTemplate.DropDownMode = LakeUI.ModernComboBox.DropDownDisplayMode.Overlay;
            cmbTemplate.DropDownSelectedColor = Color.Transparent;
            cmbTemplate.DropDownSelectedForeColor = Color.White;
            cmbTemplate.ForeColor = Color.WhiteSmoke;
            cmbTemplate.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            cmbTemplate.Location = new Point(37, 113);
            cmbTemplate.Margin = new Padding(2, 2, 2, 2);
            cmbTemplate.Name = "cmbTemplate";
            cmbTemplate.SelectionColor = Color.Transparent;
            cmbTemplate.Size = new Size(160, 32);
            cmbTemplate.TabIndex = 65;
            cmbTemplate.Text = "输出文件名模板";
            cmbTemplate.ToolTipBackColor = Color.DimGray;
            // 
            // cmbPreset
            // 
            cmbPreset.BackColor1 = Color.Transparent;
            cmbPreset.BorderColor = Color.Gainsboro;
            cmbPreset.BorderColorFocus = Color.White;
            cmbPreset.DropDownAnimationFPS = 0;
            cmbPreset.DropDownBackColor = Color.Transparent;
            cmbPreset.DropDownBackdropBlurPasses = 2;
            cmbPreset.DropDownBackdropBlurRadius = 5;
            cmbPreset.DropDownBackdropMode = LakeUI.PopupBackdropMode.Auto;
            cmbPreset.DropDownHoverColor = Color.FromArgb(128, 255, 255, 255);
            cmbPreset.DropDownMode = LakeUI.ModernComboBox.DropDownDisplayMode.Overlay;
            cmbPreset.DropDownSelectedColor = Color.Transparent;
            cmbPreset.DropDownSelectedForeColor = Color.White;
            cmbPreset.ForeColor = Color.WhiteSmoke;
            cmbPreset.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            cmbPreset.Location = new Point(37, 187);
            cmbPreset.Margin = new Padding(2, 2, 2, 2);
            cmbPreset.Name = "cmbPreset";
            cmbPreset.SelectionColor = Color.Transparent;
            cmbPreset.Size = new Size(160, 32);
            cmbPreset.TabIndex = 64;
            cmbPreset.Text = "选择预设";
            cmbPreset.ToolTipBackColor = Color.DimGray;
            // 
            // cmbEncoder
            // 
            cmbEncoder.BackColor1 = Color.Transparent;
            cmbEncoder.BorderColor = Color.Gainsboro;
            cmbEncoder.BorderColorFocus = Color.White;
            cmbEncoder.DropDownAnimationFPS = 0;
            cmbEncoder.DropDownBackColor = Color.Transparent;
            cmbEncoder.DropDownBackdropBlurPasses = 2;
            cmbEncoder.DropDownBackdropBlurRadius = 5;
            cmbEncoder.DropDownBackdropMode = LakeUI.PopupBackdropMode.Auto;
            cmbEncoder.DropDownHoverColor = Color.FromArgb(128, 255, 255, 255);
            cmbEncoder.DropDownMode = LakeUI.ModernComboBox.DropDownDisplayMode.Overlay;
            cmbEncoder.DropDownSelectedColor = Color.Transparent;
            cmbEncoder.DropDownSelectedForeColor = Color.White;
            cmbEncoder.ForeColor = Color.WhiteSmoke;
            cmbEncoder.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            cmbEncoder.Location = new Point(37, 247);
            cmbEncoder.Margin = new Padding(2, 2, 2, 2);
            cmbEncoder.Name = "cmbEncoder";
            cmbEncoder.SelectionColor = Color.Transparent;
            cmbEncoder.Size = new Size(160, 32);
            cmbEncoder.TabIndex = 63;
            cmbEncoder.Text = "选择编码器";
            cmbEncoder.ToolTipBackColor = Color.DimGray;
            // 
            // cmbConflict
            // 
            cmbConflict.BackColor1 = Color.Transparent;
            cmbConflict.BorderColor = Color.Gainsboro;
            cmbConflict.BorderColorFocus = Color.White;
            cmbConflict.DropDownAnimationFPS = 0;
            cmbConflict.DropDownBackColor = Color.Transparent;
            cmbConflict.DropDownBackdropBlurPasses = 2;
            cmbConflict.DropDownBackdropBlurRadius = 5;
            cmbConflict.DropDownBackdropMode = LakeUI.PopupBackdropMode.Auto;
            cmbConflict.DropDownHoverColor = Color.FromArgb(128, 255, 255, 255);
            cmbConflict.DropDownMode = LakeUI.ModernComboBox.DropDownDisplayMode.Overlay;
            cmbConflict.DropDownSelectedColor = Color.Transparent;
            cmbConflict.DropDownSelectedForeColor = Color.White;
            cmbConflict.ForeColor = Color.WhiteSmoke;
            cmbConflict.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            cmbConflict.Location = new Point(37, 309);
            cmbConflict.Margin = new Padding(2, 2, 2, 2);
            cmbConflict.Name = "cmbConflict";
            cmbConflict.SelectionColor = Color.Transparent;
            cmbConflict.Size = new Size(160, 32);
            cmbConflict.TabIndex = 62;
            cmbConflict.Text = "冲突策略";
            cmbConflict.ToolTipBackColor = Color.DimGray;
            // 
            // cmbChroma
            // 
            cmbChroma.BackColor1 = Color.Transparent;
            cmbChroma.BorderColor = Color.Gainsboro;
            cmbChroma.BorderColorFocus = Color.White;
            cmbChroma.DropDownAnimationFPS = 0;
            cmbChroma.DropDownBackColor = Color.Transparent;
            cmbChroma.DropDownBackdropBlurPasses = 2;
            cmbChroma.DropDownBackdropBlurRadius = 5;
            cmbChroma.DropDownBackdropMode = LakeUI.PopupBackdropMode.Auto;
            cmbChroma.DropDownHoverColor = Color.FromArgb(128, 255, 255, 255);
            cmbChroma.DropDownMode = LakeUI.ModernComboBox.DropDownDisplayMode.Overlay;
            cmbChroma.DropDownSelectedColor = Color.Transparent;
            cmbChroma.DropDownSelectedForeColor = Color.White;
            cmbChroma.ForeColor = Color.WhiteSmoke;
            cmbChroma.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            cmbChroma.Location = new Point(37, 380);
            cmbChroma.Margin = new Padding(2, 2, 2, 2);
            cmbChroma.Name = "cmbChroma";
            cmbChroma.SelectionColor = Color.Transparent;
            cmbChroma.Size = new Size(160, 32);
            cmbChroma.TabIndex = 61;
            cmbChroma.Text = "选择色度采样";
            cmbChroma.ToolTipBackColor = Color.DimGray;
            // 
            // cmbBitDepth
            // 
            cmbBitDepth.BackColor1 = Color.Transparent;
            cmbBitDepth.BorderColor = Color.Gainsboro;
            cmbBitDepth.BorderColorFocus = Color.White;
            cmbBitDepth.DropDownAnimationFPS = 0;
            cmbBitDepth.DropDownBackColor = Color.Transparent;
            cmbBitDepth.DropDownBackdropBlurPasses = 2;
            cmbBitDepth.DropDownBackdropBlurRadius = 5;
            cmbBitDepth.DropDownBackdropMode = LakeUI.PopupBackdropMode.Auto;
            cmbBitDepth.DropDownHoverColor = Color.FromArgb(128, 255, 255, 255);
            cmbBitDepth.DropDownMode = LakeUI.ModernComboBox.DropDownDisplayMode.Overlay;
            cmbBitDepth.DropDownSelectedColor = Color.Transparent;
            cmbBitDepth.DropDownSelectedForeColor = Color.White;
            cmbBitDepth.ForeColor = Color.WhiteSmoke;
            cmbBitDepth.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            cmbBitDepth.Location = new Point(37, 438);
            cmbBitDepth.Margin = new Padding(2, 2, 2, 2);
            cmbBitDepth.Name = "cmbBitDepth";
            cmbBitDepth.SelectionColor = Color.Transparent;
            cmbBitDepth.Size = new Size(160, 32);
            cmbBitDepth.TabIndex = 60;
            cmbBitDepth.Text = "选择色深";
            cmbBitDepth.ToolTipBackColor = Color.DimGray;
            // 
            // cmbQualityMode
            // 
            cmbQualityMode.BackColor1 = Color.Transparent;
            cmbQualityMode.BorderColor = Color.Gainsboro;
            cmbQualityMode.BorderColorFocus = Color.White;
            cmbQualityMode.DropDownAnimationFPS = 0;
            cmbQualityMode.DropDownBackColor = Color.Transparent;
            cmbQualityMode.DropDownBackdropBlurPasses = 2;
            cmbQualityMode.DropDownBackdropBlurRadius = 5;
            cmbQualityMode.DropDownBackdropMode = LakeUI.PopupBackdropMode.Auto;
            cmbQualityMode.DropDownHoverColor = Color.FromArgb(128, 255, 255, 255);
            cmbQualityMode.DropDownMode = LakeUI.ModernComboBox.DropDownDisplayMode.Overlay;
            cmbQualityMode.DropDownSelectedColor = Color.Transparent;
            cmbQualityMode.DropDownSelectedForeColor = Color.White;
            cmbQualityMode.ForeColor = Color.WhiteSmoke;
            cmbQualityMode.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            cmbQualityMode.Location = new Point(230, 438);
            cmbQualityMode.Margin = new Padding(2, 2, 2, 2);
            cmbQualityMode.Name = "cmbQualityMode";
            cmbQualityMode.SelectionColor = Color.Transparent;
            cmbQualityMode.Size = new Size(160, 32);
            cmbQualityMode.TabIndex = 59;
            cmbQualityMode.Text = "质量指标";
            cmbQualityMode.ToolTipBackColor = Color.DimGray;
            // 
            // numSearchCpuUsed
            // 
            numSearchCpuUsed.AllowDrop = true;
            numSearchCpuUsed.BackColor1 = Color.Transparent;
            numSearchCpuUsed.BorderColorFocus = Color.White;
            numSearchCpuUsed.CaretColor = Color.FromArgb(220, 220, 220);
            numSearchCpuUsed.DecimalPlaces = 15;
            numSearchCpuUsed.ForeColor = Color.White;
            numSearchCpuUsed.HoverArrowColor = Color.Gray;
            numSearchCpuUsed.HoverButtonBackColor1 = Color.FromArgb(200, 255, 255, 255);
            numSearchCpuUsed.Location = new Point(432, 187);
            numSearchCpuUsed.Name = "numSearchCpuUsed";
            numSearchCpuUsed.Size = new Size(160, 32);
            numSearchCpuUsed.TabIndex = 58;
            // 
            // numFinalCpuUsed
            // 
            numFinalCpuUsed.AllowDrop = true;
            numFinalCpuUsed.BackColor1 = Color.Transparent;
            numFinalCpuUsed.BorderColorFocus = Color.White;
            numFinalCpuUsed.CaretColor = Color.FromArgb(220, 220, 220);
            numFinalCpuUsed.DecimalPlaces = 15;
            numFinalCpuUsed.ForeColor = Color.White;
            numFinalCpuUsed.HoverArrowColor = Color.Gray;
            numFinalCpuUsed.HoverButtonBackColor1 = Color.FromArgb(200, 255, 255, 255);
            numFinalCpuUsed.Location = new Point(432, 247);
            numFinalCpuUsed.Name = "numFinalCpuUsed";
            numFinalCpuUsed.Size = new Size(160, 32);
            numFinalCpuUsed.TabIndex = 57;
            // 
            // numMaxRes
            // 
            numMaxRes.AllowDrop = true;
            numMaxRes.BackColor1 = Color.Transparent;
            numMaxRes.BorderColorFocus = Color.White;
            numMaxRes.CaretColor = Color.FromArgb(220, 220, 220);
            numMaxRes.DecimalPlaces = 15;
            numMaxRes.ForeColor = Color.White;
            numMaxRes.HoverArrowColor = Color.Gray;
            numMaxRes.HoverButtonBackColor1 = Color.FromArgb(200, 255, 255, 255);
            numMaxRes.Increment = 100D;
            numMaxRes.LargeChange = 1000D;
            numMaxRes.Location = new Point(432, 309);
            numMaxRes.Name = "numMaxRes";
            numMaxRes.Size = new Size(160, 32);
            numMaxRes.SmallChange = 100D;
            numMaxRes.TabIndex = 56;
            // 
            // numJobs
            // 
            numJobs.AllowDrop = true;
            numJobs.BackColor1 = Color.Transparent;
            numJobs.BorderColorFocus = Color.White;
            numJobs.CaretColor = Color.FromArgb(220, 220, 220);
            numJobs.DecimalPlaces = 15;
            numJobs.ForeColor = Color.White;
            numJobs.HoverArrowColor = Color.Gray;
            numJobs.HoverButtonBackColor1 = Color.FromArgb(200, 255, 255, 255);
            numJobs.Location = new Point(432, 380);
            numJobs.Name = "numJobs";
            numJobs.Size = new Size(160, 32);
            numJobs.TabIndex = 55;
            // 
            // numQualityValue
            // 
            numQualityValue.AllowDrop = true;
            numQualityValue.BackColor1 = Color.Transparent;
            numQualityValue.BorderColorFocus = Color.White;
            numQualityValue.CaretColor = Color.FromArgb(220, 220, 220);
            numQualityValue.DecimalPlaces = 15;
            numQualityValue.ForeColor = Color.White;
            numQualityValue.HoverArrowColor = Color.Gray;
            numQualityValue.HoverButtonBackColor1 = Color.FromArgb(200, 255, 255, 255);
            numQualityValue.Increment = 0.1D;
            numQualityValue.LargeChange = 1D;
            numQualityValue.Location = new Point(432, 438);
            numQualityValue.Name = "numQualityValue";
            numQualityValue.Size = new Size(160, 32);
            numQualityValue.SmallChange = 0.1D;
            numQualityValue.TabIndex = 54;
            // 
            // chkSweep
            // 
            chkSweep.AnimationFPS = 0;
            chkSweep.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkSweep.BoxUncheckedBackColor = Color.FromArgb(30, 50, 50, 50);
            chkSweep.ForeColor = Color.WhiteSmoke;
            chkSweep.Location = new Point(639, 301);
            chkSweep.Name = "chkSweep";
            chkSweep.Size = new Size(176, 21);
            chkSweep.TabIndex = 52;
            chkSweep.Text = "遍历模式";
            // 
            // chkProxy
            // 
            chkProxy.AnimationFPS = 0;
            chkProxy.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkProxy.BoxUncheckedBackColor = Color.FromArgb(30, 50, 50, 50);
            chkProxy.ForeColor = Color.WhiteSmoke;
            chkProxy.Location = new Point(639, 247);
            chkProxy.Name = "chkProxy";
            chkProxy.Size = new Size(150, 24);
            chkProxy.TabIndex = 51;
            chkProxy.Text = "代理搜索";
            // 
            // chkPriorSearch
            // 
            chkPriorSearch.AnimationFPS = 0;
            chkPriorSearch.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkPriorSearch.BoxUncheckedBackColor = Color.FromArgb(30, 50, 50, 50);
            chkPriorSearch.ForeColor = Color.WhiteSmoke;
            chkPriorSearch.Location = new Point(639, 217);
            chkPriorSearch.Name = "chkPriorSearch";
            chkPriorSearch.Size = new Size(150, 24);
            chkPriorSearch.TabIndex = 50;
            chkPriorSearch.Text = "先验概率分布搜索";
            // 
            // chkSerialEncode
            // 
            chkSerialEncode.AnimationFPS = 0;
            chkSerialEncode.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkSerialEncode.BoxUncheckedBackColor = Color.FromArgb(30, 50, 50, 50);
            chkSerialEncode.ForeColor = Color.WhiteSmoke;
            chkSerialEncode.Location = new Point(639, 386);
            chkSerialEncode.Name = "chkSerialEncode";
            chkSerialEncode.Size = new Size(150, 25);
            chkSerialEncode.TabIndex = 49;
            chkSerialEncode.Text = "单线程极限压缩";
            // 
            // label11
            // 
            label11.AutoSize = true;
            label11.ForeColor = Color.WhiteSmoke;
            label11.Location = new Point(37, 290);
            label11.Name = "label11";
            label11.Size = new Size(92, 17);
            label11.TabIndex = 48;
            label11.Text = "当文件已存在时";
            // 
            // chkOutputFullRes
            // 
            chkOutputFullRes.AnimationFPS = 0;
            chkOutputFullRes.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkOutputFullRes.BoxUncheckedBackColor = Color.FromArgb(30, 50, 50, 50);
            chkOutputFullRes.ForeColor = Color.WhiteSmoke;
            chkOutputFullRes.Location = new Point(639, 359);
            chkOutputFullRes.Name = "chkOutputFullRes";
            chkOutputFullRes.Size = new Size(150, 21);
            chkOutputFullRes.TabIndex = 46;
            chkOutputFullRes.Text = "保持原图分辨率";
            // 
            // label10
            // 
            label10.AutoSize = true;
            label10.ForeColor = Color.WhiteSmoke;
            label10.Location = new Point(432, 290);
            label10.Name = "label10";
            label10.Size = new Size(168, 17);
            label10.TabIndex = 45;
            label10.Text = "预缩放最大分辨率（0=禁用）";
            // 
            // chkRecursive
            // 
            chkRecursive.AnimationFPS = 0;
            chkRecursive.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkRecursive.BoxUncheckedBackColor = Color.FromArgb(30, 50, 50, 50);
            chkRecursive.ForeColor = Color.WhiteSmoke;
            chkRecursive.Location = new Point(639, 328);
            chkRecursive.Name = "chkRecursive";
            chkRecursive.Size = new Size(150, 25);
            chkRecursive.TabIndex = 43;
            chkRecursive.Text = "递归子目录";
            // 
            // chkLossless
            // 
            chkLossless.AnimationFPS = 0;
            chkLossless.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkLossless.BoxUncheckedBackColor = Color.FromArgb(30, 50, 50, 50);
            chkLossless.ForeColor = Color.WhiteSmoke;
            chkLossless.Location = new Point(639, 415);
            chkLossless.Name = "chkLossless";
            chkLossless.Size = new Size(150, 21);
            chkLossless.TabIndex = 42;
            chkLossless.Text = "无损(目前有bug)";
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.ForeColor = Color.WhiteSmoke;
            label9.Location = new Point(37, 419);
            label9.Name = "label9";
            label9.Size = new Size(32, 17);
            label9.TabIndex = 41;
            label9.Text = "色深";
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.ForeColor = Color.WhiteSmoke;
            label8.Location = new Point(37, 359);
            label8.Name = "label8";
            label8.Size = new Size(56, 17);
            label8.TabIndex = 39;
            label8.Text = "色度采样";
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.ForeColor = Color.WhiteSmoke;
            label7.Location = new Point(230, 419);
            label7.Name = "label7";
            label7.Size = new Size(56, 17);
            label7.TabIndex = 36;
            label7.Text = "质量指标";
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.ForeColor = Color.WhiteSmoke;
            label6.Location = new Point(230, 363);
            label6.Name = "label6";
            label6.Size = new Size(56, 17);
            label6.TabIndex = 34;
            label6.Text = "搜索度量";
            // 
            // cmbMetric
            // 
            cmbMetric.BackColor1 = Color.Transparent;
            cmbMetric.BorderColor = Color.Gainsboro;
            cmbMetric.BorderColorFocus = Color.White;
            cmbMetric.DropDownAnimationFPS = 0;
            cmbMetric.DropDownBackColor = Color.Transparent;
            cmbMetric.DropDownBackdropBlurPasses = 2;
            cmbMetric.DropDownBackdropBlurRadius = 5;
            cmbMetric.DropDownBackdropMode = LakeUI.PopupBackdropMode.Auto;
            cmbMetric.DropDownHoverColor = Color.FromArgb(128, 255, 255, 255);
            cmbMetric.DropDownMode = LakeUI.ModernComboBox.DropDownDisplayMode.Overlay;
            cmbMetric.DropDownSelectedColor = Color.Transparent;
            cmbMetric.DropDownSelectedForeColor = Color.White;
            cmbMetric.ForeColor = Color.WhiteSmoke;
            cmbMetric.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            cmbMetric.Location = new Point(230, 380);
            cmbMetric.Margin = new Padding(2, 2, 2, 2);
            cmbMetric.Name = "cmbMetric";
            cmbMetric.SelectionColor = Color.Transparent;
            cmbMetric.Size = new Size(160, 32);
            cmbMetric.TabIndex = 33;
            cmbMetric.Text = "搜索度量";
            cmbMetric.ToolTipBackColor = Color.DimGray;
            // 
            // grpCrfMode
            // 
            grpCrfMode.Controls.Add(numCrfMax);
            grpCrfMode.Controls.Add(numCrfMin);
            grpCrfMode.Controls.Add(numCrfFix);
            grpCrfMode.Controls.Add(rbCrfFix);
            grpCrfMode.Controls.Add(label5);
            grpCrfMode.Controls.Add(rbCrfRange);
            grpCrfMode.Controls.Add(label4);
            grpCrfMode.ForeColor = Color.WhiteSmoke;
            grpCrfMode.Location = new Point(230, 166);
            grpCrfMode.Name = "grpCrfMode";
            grpCrfMode.Size = new Size(162, 187);
            grpCrfMode.TabIndex = 32;
            grpCrfMode.TabStop = false;
            grpCrfMode.Text = "CRF搜索模式";
            // 
            // numCrfMax
            // 
            numCrfMax.BackColor1 = Color.Transparent;
            numCrfMax.BorderColorFocus = Color.White;
            numCrfMax.CaretColor = Color.FromArgb(220, 220, 220);
            numCrfMax.ForeColor = Color.White;
            numCrfMax.HoverArrowColor = Color.Gray;
            numCrfMax.HoverButtonBackColor1 = Color.Silver;
            numCrfMax.Location = new Point(90, 143);
            numCrfMax.Name = "numCrfMax";
            numCrfMax.Size = new Size(66, 32);
            numCrfMax.TabIndex = 54;
            // 
            // numCrfMin
            // 
            numCrfMin.BackColor1 = Color.Transparent;
            numCrfMin.BorderColorFocus = Color.White;
            numCrfMin.CaretColor = Color.FromArgb(220, 220, 220);
            numCrfMin.ForeColor = Color.White;
            numCrfMin.HoverArrowColor = Color.Gray;
            numCrfMin.HoverButtonBackColor1 = Color.Silver;
            numCrfMin.Location = new Point(6, 143);
            numCrfMin.Name = "numCrfMin";
            numCrfMin.Size = new Size(66, 32);
            numCrfMin.TabIndex = 53;
            // 
            // numCrfFix
            // 
            numCrfFix.AllowDrop = true;
            numCrfFix.BackColor1 = Color.Transparent;
            numCrfFix.BorderColorFocus = Color.White;
            numCrfFix.CaretColor = Color.FromArgb(220, 220, 220);
            numCrfFix.ForeColor = Color.White;
            numCrfFix.HoverArrowColor = Color.Gray;
            numCrfFix.HoverButtonBackColor1 = Color.Silver;
            numCrfFix.Location = new Point(6, 52);
            numCrfFix.Name = "numCrfFix";
            numCrfFix.Size = new Size(114, 32);
            numCrfFix.TabIndex = 53;
            // 
            // rbCrfFix
            // 
            rbCrfFix.AnimationFPS = 0;
            rbCrfFix.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            rbCrfFix.CheckMode = LakeUI.ModernCheckBox.CheckModeEnum.RadioButton;
            rbCrfFix.ForeColor = Color.WhiteSmoke;
            rbCrfFix.Location = new Point(6, 21);
            rbCrfFix.Name = "rbCrfFix";
            rbCrfFix.Size = new Size(81, 23);
            rbCrfFix.TabIndex = 25;
            rbCrfFix.Text = "固定 CRF";
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.ForeColor = Color.WhiteSmoke;
            label5.Location = new Point(90, 124);
            label5.Name = "label5";
            label5.Size = new Size(56, 17);
            label5.TabIndex = 31;
            label5.Text = "范围上限";

            // 
            // rbCrfRange
            // 
            rbCrfRange.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            rbCrfRange.CheckMode = LakeUI.ModernCheckBox.CheckModeEnum.RadioButton;
            rbCrfRange.ForeColor = Color.WhiteSmoke;
            rbCrfRange.Location = new Point(6, 90);
            rbCrfRange.Name = "rbCrfRange";
            rbCrfRange.Size = new Size(150, 23);
            rbCrfRange.TabIndex = 26;
            rbCrfRange.Text = "CRF 范围";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.ForeColor = Color.WhiteSmoke;
            label4.Location = new Point(6, 124);
            label4.Name = "label4";
            label4.Size = new Size(56, 17);
            label4.TabIndex = 30;
            label4.Text = "范围下限";

            // 
            // txtTemplate
            // 
            txtTemplate.BackColor1 = Color.Transparent;
            txtTemplate.BorderColorFocus = Color.White;
            txtTemplate.Cursor = Cursors.IBeam;
            txtTemplate.ForeColor = Color.WhiteSmoke;
            txtTemplate.Location = new Point(230, 113);
            txtTemplate.Margin = new Padding(2);
            txtTemplate.Name = "txtTemplate";
            txtTemplate.Size = new Size(607, 32);
            txtTemplate.TabIndex = 22;
            txtTemplate.Text = "covers-{index}.avif";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.ForeColor = Color.Gainsboro;
            label3.Location = new Point(432, 228);
            label3.Name = "label3";
            label3.Size = new Size(80, 17);
            label3.TabIndex = 21;
            label3.Text = "最终编码速度";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.ForeColor = Color.WhiteSmoke;
            label2.Location = new Point(432, 166);
            label2.Name = "label2";
            label2.Size = new Size(56, 17);
            label2.TabIndex = 19;
            label2.Text = "搜索速度";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.ForeColor = Color.WhiteSmoke;
            label1.Location = new Point(432, 363);
            label1.Name = "label1";
            label1.Size = new Size(110, 17);
            label1.TabIndex = 17;
            label1.Text = "并行ffmpeg任务数";
            // 
            // btnStop
            // 
            btnStop.AnimationFPS = 0;
            btnStop.BackColor1 = Color.Transparent;
            btnStop.BorderColor = Color.White;
            btnStop.BorderRadius = 10;
            btnStop.ForeColor = Color.WhiteSmoke;
            btnStop.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            btnStop.Location = new Point(230, 489);
            btnStop.Margin = new Padding(2);
            btnStop.Name = "btnStop";
            btnStop.PressedBackColor1 = Color.White;
            btnStop.Size = new Size(120, 35);
            btnStop.TabIndex = 15;
            btnStop.Text = "停止任务";
            btnStop.Click += btnStop_Click;
            // 
            // chkSearch
            // 
            chkSearch.AnimationFPS = 0;
            chkSearch.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkSearch.BoxUncheckedBackColor = Color.FromArgb(30, 50, 50, 50);
            chkSearch.ForeColor = Color.WhiteSmoke;
            chkSearch.Location = new Point(639, 187);
            chkSearch.Name = "chkSearch";
            chkSearch.Size = new Size(150, 24);
            chkSearch.TabIndex = 14;
            chkSearch.Text = "是否启用 CRF 搜索";
            // 
            // lblQuality
            // 
            lblQuality.AutoSize = true;
            lblQuality.ForeColor = Color.WhiteSmoke;
            lblQuality.Location = new Point(432, 419);
            lblQuality.Name = "lblQuality";
            lblQuality.Size = new Size(80, 17);
            lblQuality.TabIndex = 12;
            lblQuality.Text = "质量指标目标";
            // 
            // lblEncoder
            // 
            lblEncoder.AutoSize = true;
            lblEncoder.ForeColor = Color.WhiteSmoke;
            lblEncoder.Location = new Point(37, 228);
            lblEncoder.Name = "lblEncoder";
            lblEncoder.Size = new Size(44, 17);
            lblEncoder.TabIndex = 10;
            lblEncoder.Text = "编码器";
            // 
            // lblPreset
            // 
            lblPreset.AutoSize = true;
            lblPreset.ForeColor = Color.WhiteSmoke;
            lblPreset.Location = new Point(37, 166);
            lblPreset.Name = "lblPreset";
            lblPreset.Size = new Size(56, 17);
            lblPreset.TabIndex = 8;
            lblPreset.Text = "编码预设";
            // 
            // progressBar1
            // 
            progressBar1.AnimationFPS = 0;
            progressBar1.BorderColor = Color.White;
            progressBar1.BorderSize = 1;
            progressBar1.DisabledOverlayColor = Color.White;
            progressBar1.FillColor = Color.FromArgb(0, 120, 215);
            progressBar1.Location = new Point(37, 528);
            progressBar1.Margin = new Padding(2, 2, 2, 2);
            progressBar1.Name = "progressBar1";
            progressBar1.Size = new Size(800, 20);
            progressBar1.TabIndex = 7;
            progressBar1.TabStop = false;
            progressBar1.TextPadding = new Padding(0);
            progressBar1.TrackColor = Color.Transparent;
            // 
            // btnBrowseOutput
            // 
            btnBrowseOutput.AnimationFPS = 0;
            btnBrowseOutput.BackColor1 = Color.Transparent;
            btnBrowseOutput.BorderColor = Color.Gainsboro;
            btnBrowseOutput.BorderRadius = 10;
            btnBrowseOutput.ForeColor = Color.WhiteSmoke;
            btnBrowseOutput.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            btnBrowseOutput.Location = new Point(37, 67);
            btnBrowseOutput.Margin = new Padding(2);
            btnBrowseOutput.Name = "btnBrowseOutput";
            btnBrowseOutput.PressedBackColor1 = Color.White;
            btnBrowseOutput.Size = new Size(97, 32);
            btnBrowseOutput.TabIndex = 6;
            btnBrowseOutput.Text = "浏览输出路径";
            btnBrowseOutput.Click += btnBrowseOutput_Click;
            // 
            // txtOutput
            // 
            txtOutput.AllowDrop = true;
            txtOutput.BackColor1 = Color.Transparent;
            txtOutput.BorderColorFocus = Color.White;
            txtOutput.ForeColor = Color.WhiteSmoke;
            txtOutput.Location = new Point(156, 67);
            txtOutput.Margin = new Padding(2);
            txtOutput.Name = "txtOutput";
            txtOutput.Size = new Size(681, 32);
            txtOutput.TabIndex = 5;
            txtOutput.WaterText = "输出路径，支持拖入文件夹";
            // 
            // btnBrowseInput
            // 
            btnBrowseInput.AnimationFPS = 0;
            btnBrowseInput.BackColor1 = Color.Transparent;
            btnBrowseInput.BorderColor = Color.Gainsboro;
            btnBrowseInput.BorderRadius = 10;
            btnBrowseInput.ForeColor = Color.WhiteSmoke;
            btnBrowseInput.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            btnBrowseInput.Location = new Point(37, 21);
            btnBrowseInput.Margin = new Padding(2);
            btnBrowseInput.Name = "btnBrowseInput";
            btnBrowseInput.PressedBackColor1 = Color.White;
            btnBrowseInput.Size = new Size(97, 32);
            btnBrowseInput.TabIndex = 4;
            btnBrowseInput.Text = "浏览输入路径";
            btnBrowseInput.Click += btnBrowseInput_Click;
            // 
            // btnStart
            // 
            btnStart.AnimationFPS = 0;
            btnStart.BackColor1 = Color.Transparent;
            btnStart.BorderColor = Color.White;
            btnStart.BorderRadius = 10;
            btnStart.ForeColor = Color.WhiteSmoke;
            btnStart.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            btnStart.Location = new Point(37, 489);
            btnStart.Margin = new Padding(2);
            btnStart.Name = "btnStart";
            btnStart.PressedBackColor1 = Color.White;
            btnStart.Size = new Size(120, 35);
            btnStart.TabIndex = 2;
            btnStart.Text = "开始任务";
            btnStart.Click += btnStart_Click;
            // 
            // txtInput
            // 
            txtInput.AllowDrop = true;
            txtInput.BackColor1 = Color.Transparent;
            txtInput.BorderColorFocus = Color.White;
            txtInput.ForeColor = Color.WhiteSmoke;
            txtInput.Location = new Point(156, 21);
            txtInput.Margin = new Padding(2);
            txtInput.Name = "txtInput";
            txtInput.Size = new Size(681, 32);
            txtInput.TabIndex = 0;
            txtInput.WaterText = "输入路径，支持拖入文件夹";
            // 
            // btnResume
            // 
            btnResume.AnimationFPS = 0;
            btnResume.BackColor1 = Color.Transparent;
            btnResume.BorderColor = Color.White;
            btnResume.BorderRadius = 10;
            btnResume.ForeColor = Color.WhiteSmoke;
            btnResume.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            btnResume.Location = new Point(432, 489);
            btnResume.Margin = new Padding(2);
            btnResume.Name = "btnResume";
            btnResume.PressedBackColor1 = Color.White;
            btnResume.Size = new Size(120, 35);
            btnResume.TabIndex = 66;
            btnResume.Text = "恢复任务";
            // 
            // btnAbandon
            // 
            btnAbandon.AnimationFPS = 0;
            btnAbandon.BackColor1 = Color.Transparent;
            btnAbandon.BorderColor = Color.White;
            btnAbandon.BorderRadius = 10;
            btnAbandon.ForeColor = Color.WhiteSmoke;
            btnAbandon.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            btnAbandon.Location = new Point(639, 489);
            btnAbandon.Margin = new Padding(2);
            btnAbandon.Name = "btnAbandon";
            btnAbandon.PressedBackColor1 = Color.White;
            btnAbandon.Size = new Size(120, 35);
            btnAbandon.TabIndex = 67;
            btnAbandon.Text = "放弃任务";
            // 
            // FormEncode
            // 
            AllowDrop = true;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1114, 681);
            Controls.Add(modernPanel1);
            DoubleBuffered = true;
            Name = "FormEncode";
            Text = "FormEncode";
            modernPanel1.ResumeLayout(false);
            modernPanel1.PerformLayout();
            grpCrfMode.ResumeLayout(false);
            grpCrfMode.PerformLayout();
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
        private Label lblEncoder;
        private Label lblQuality;
        private LakeUI.ModernCheckBox chkSearch;
        private LakeUI.ModernButton btnStop;
        private Label label1;
        private Label label2;
        private Label label3;
        private LakeUI.ModernTextBox txtTemplate;
        private LakeUI.ModernCheckBox rbCrfFix;
        private LakeUI.ModernCheckBox rbCrfRange;
        private Label label4;
        private Label label5;
        private GroupBox grpCrfMode;
        private LakeUI.ModernComboBox cmbMetric;
        private Label label6;
        private Label label7;
        private Label label8;
        private Label label9;
        private LakeUI.ModernCheckBox chkLossless;
        private LakeUI.ModernCheckBox chkRecursive;
        private Label label10;
        private LakeUI.ModernCheckBox chkOutputFullRes;
        private Label label11;
        private LakeUI.ModernCheckBox chkSerialEncode;
        private LakeUI.ModernCheckBox chkPriorSearch;
        private LakeUI.ModernCheckBox chkProxy;
        private LakeUI.ModernCheckBox chkSweep;
        private LakeUI.ModernNumericUpDown numCrfFix;
        private LakeUI.ModernNumericUpDown numCrfMin;
        private LakeUI.ModernNumericUpDown numCrfMax;
        private LakeUI.ModernNumericUpDown numQualityValue;
        private LakeUI.ModernNumericUpDown numJobs;
        private LakeUI.ModernNumericUpDown numMaxRes;
        private LakeUI.ModernNumericUpDown numFinalCpuUsed;
        private LakeUI.ModernNumericUpDown numSearchCpuUsed;
        private LakeUI.ModernComboBox cmbQualityMode;
        private LakeUI.ModernComboBox cmbBitDepth;
        private LakeUI.ModernComboBox cmbChroma;
        private LakeUI.ModernComboBox cmbConflict;
        private LakeUI.ModernComboBox cmbEncoder;
        private LakeUI.ModernComboBox cmbPreset;
        private LakeUI.ModernComboBox cmbTemplate;
        private LakeUI.ModernButton btnResume;
        private LakeUI.ModernButton btnAbandon;
    }
}