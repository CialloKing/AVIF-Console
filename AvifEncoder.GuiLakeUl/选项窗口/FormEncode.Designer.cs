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
            chkProxy = new LakeUI.ModernCheckBox();
            chkPriorSearch = new LakeUI.ModernCheckBox();
            chkSerialEncode = new LakeUI.ModernCheckBox();
            label11 = new Label();
            cmbConflict = new LakeUI.ModernComboBox();
            chkOutputFullRes = new LakeUI.ModernCheckBox();
            label10 = new Label();
            numMaxRes = new NumericUpDown();
            chkRecursive = new LakeUI.ModernCheckBox();
            chkLossless = new LakeUI.ModernCheckBox();
            label9 = new Label();
            cmbBitDepth = new LakeUI.ModernComboBox();
            label8 = new Label();
            cmbChroma = new LakeUI.ModernComboBox();
            numQualityValue = new NumericUpDown();
            label7 = new Label();
            cmbQualityMode = new LakeUI.ModernComboBox();
            label6 = new Label();
            cmbMetric = new LakeUI.ModernComboBox();
            grpCrfMode = new GroupBox();
            rbCrfFix = new LakeUI.ModernCheckBox();
            numCrfMax = new NumericUpDown();
            label5 = new Label();
            numCrfMin = new NumericUpDown();
            numCrfFix = new NumericUpDown();
            label4 = new Label();
            rbCrfRange = new LakeUI.ModernCheckBox();
            modernButton1 = new LakeUI.ModernButton();
            txtTemplate = new LakeUI.ModernTextBox();
            label3 = new Label();
            numFinalCpuUsed = new NumericUpDown();
            label2 = new Label();
            numSearchCpuUsed = new NumericUpDown();
            label1 = new Label();
            numJobs = new NumericUpDown();
            btnStop = new LakeUI.ModernButton();
            chkSearch = new LakeUI.ModernCheckBox();
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
            ((System.ComponentModel.ISupportInitialize)numMaxRes).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numQualityValue).BeginInit();
            grpCrfMode.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numCrfMax).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numCrfMin).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numCrfFix).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numFinalCpuUsed).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numSearchCpuUsed).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numJobs).BeginInit();
            SuspendLayout();
            // 
            // modernPanel1
            // 
            modernPanel1.BackColor = Color.Transparent;
            modernPanel1.BackColor1 = Color.Black;
            modernPanel1.BorderColor = Color.Transparent;
            modernPanel1.Controls.Add(chkProxy);
            modernPanel1.Controls.Add(chkPriorSearch);
            modernPanel1.Controls.Add(chkSerialEncode);
            modernPanel1.Controls.Add(label11);
            modernPanel1.Controls.Add(cmbConflict);
            modernPanel1.Controls.Add(chkOutputFullRes);
            modernPanel1.Controls.Add(label10);
            modernPanel1.Controls.Add(numMaxRes);
            modernPanel1.Controls.Add(chkRecursive);
            modernPanel1.Controls.Add(chkLossless);
            modernPanel1.Controls.Add(label9);
            modernPanel1.Controls.Add(cmbBitDepth);
            modernPanel1.Controls.Add(label8);
            modernPanel1.Controls.Add(cmbChroma);
            modernPanel1.Controls.Add(numQualityValue);
            modernPanel1.Controls.Add(label7);
            modernPanel1.Controls.Add(cmbQualityMode);
            modernPanel1.Controls.Add(label6);
            modernPanel1.Controls.Add(cmbMetric);
            modernPanel1.Controls.Add(grpCrfMode);
            modernPanel1.Controls.Add(modernButton1);
            modernPanel1.Controls.Add(txtTemplate);
            modernPanel1.Controls.Add(label3);
            modernPanel1.Controls.Add(numFinalCpuUsed);
            modernPanel1.Controls.Add(label2);
            modernPanel1.Controls.Add(numSearchCpuUsed);
            modernPanel1.Controls.Add(label1);
            modernPanel1.Controls.Add(numJobs);
            modernPanel1.Controls.Add(btnStop);
            modernPanel1.Controls.Add(chkSearch);
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
            modernPanel1.ScrollBarMode = LakeUI.ModernPanel.ScrollMode.None;
            modernPanel1.Size = new Size(1114, 681);
            modernPanel1.TabIndex = 0;
            // 
            // chkProxy
            // 
            chkProxy.AnimationFPS = 0;
            chkProxy.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkProxy.ForeColor = Color.WhiteSmoke;
            chkProxy.Location = new Point(625, 249);
            chkProxy.Name = "chkProxy";
            chkProxy.Size = new Size(150, 24);
            chkProxy.TabIndex = 51;
            chkProxy.Text = "代理搜索";
            // 
            // chkPriorSearch
            // 
            chkPriorSearch.AnimationFPS = 0;
            chkPriorSearch.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkPriorSearch.ForeColor = Color.WhiteSmoke;
            chkPriorSearch.Location = new Point(625, 218);
            chkPriorSearch.Name = "chkPriorSearch";
            chkPriorSearch.Size = new Size(150, 24);
            chkPriorSearch.TabIndex = 50;
            chkPriorSearch.Text = "先验概率分布搜索";
            // 
            // chkSerialEncode
            // 
            chkSerialEncode.AnimationFPS = 0;
            chkSerialEncode.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkSerialEncode.ForeColor = Color.WhiteSmoke;
            chkSerialEncode.Location = new Point(625, 279);
            chkSerialEncode.Name = "chkSerialEncode";
            chkSerialEncode.Size = new Size(150, 25);
            chkSerialEncode.TabIndex = 49;
            chkSerialEncode.Text = "单线程极限压缩";
            // 
            // label11
            // 
            label11.AutoSize = true;
            label11.ForeColor = Color.WhiteSmoke;
            label11.Location = new Point(37, 309);
            label11.Name = "label11";
            label11.Size = new Size(92, 17);
            label11.TabIndex = 48;
            label11.Text = "当文件已存在时";
            // 
            // cmbConflict
            // 
            cmbConflict.BackColor1 = Color.Transparent;
            cmbConflict.CaretColor = Color.FromArgb(220, 220, 220);
            cmbConflict.DropDownAnimationFPS = 0;
            cmbConflict.DropDownScrollBarHoverColor = Color.FromArgb(200, 200, 200);
            cmbConflict.ForeColor = Color.Gainsboro;
            cmbConflict.Location = new Point(37, 328);
            cmbConflict.Margin = new Padding(2, 2, 2, 2);
            cmbConflict.Name = "cmbConflict";
            cmbConflict.Size = new Size(160, 32);
            cmbConflict.TabIndex = 47;
            cmbConflict.Text = "冲突策略";
            // 
            // chkOutputFullRes
            // 
            chkOutputFullRes.AnimationFPS = 0;
            chkOutputFullRes.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkOutputFullRes.ForeColor = Color.WhiteSmoke;
            chkOutputFullRes.Location = new Point(625, 341);
            chkOutputFullRes.Name = "chkOutputFullRes";
            chkOutputFullRes.Size = new Size(150, 21);
            chkOutputFullRes.TabIndex = 46;
            chkOutputFullRes.Text = "保持原图分辨率";
            // 
            // label10
            // 
            label10.AutoSize = true;
            label10.ForeColor = Color.WhiteSmoke;
            label10.Location = new Point(432, 299);
            label10.Name = "label10";
            label10.Size = new Size(168, 17);
            label10.TabIndex = 45;
            label10.Text = "预缩放最大分辨率（0=禁用）";
            // 
            // numMaxRes
            // 
            numMaxRes.Location = new Point(432, 319);
            numMaxRes.Name = "numMaxRes";
            numMaxRes.Size = new Size(120, 23);
            numMaxRes.TabIndex = 44;
            // 
            // chkRecursive
            // 
            chkRecursive.AnimationFPS = 0;
            chkRecursive.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkRecursive.ForeColor = Color.WhiteSmoke;
            chkRecursive.Location = new Point(625, 310);
            chkRecursive.Name = "chkRecursive";
            chkRecursive.Size = new Size(150, 25);
            chkRecursive.TabIndex = 43;
            chkRecursive.Text = "递归子目录";
            // 
            // chkLossless
            // 
            chkLossless.AnimationFPS = 0;
            chkLossless.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            chkLossless.ForeColor = Color.WhiteSmoke;
            chkLossless.Location = new Point(625, 368);
            chkLossless.Name = "chkLossless";
            chkLossless.Size = new Size(150, 21);
            chkLossless.TabIndex = 42;
            chkLossless.Text = "无损";
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.ForeColor = Color.WhiteSmoke;
            label9.Location = new Point(37, 445);
            label9.Name = "label9";
            label9.Size = new Size(32, 17);
            label9.TabIndex = 41;
            label9.Text = "色深";
            // 
            // cmbBitDepth
            // 
            cmbBitDepth.BackColor1 = Color.Transparent;
            cmbBitDepth.CaretColor = Color.FromArgb(220, 220, 220);
            cmbBitDepth.DropDownAnimationFPS = 0;
            cmbBitDepth.DropDownScrollBarHoverColor = Color.FromArgb(200, 200, 200);
            cmbBitDepth.ForeColor = Color.Gainsboro;
            cmbBitDepth.Location = new Point(37, 464);
            cmbBitDepth.Margin = new Padding(2, 2, 2, 2);
            cmbBitDepth.Name = "cmbBitDepth";
            cmbBitDepth.Size = new Size(160, 32);
            cmbBitDepth.TabIndex = 40;
            cmbBitDepth.Text = "选择色深";
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.ForeColor = Color.WhiteSmoke;
            label8.Location = new Point(37, 380);
            label8.Name = "label8";
            label8.Size = new Size(56, 17);
            label8.TabIndex = 39;
            label8.Text = "色度采样";
            // 
            // cmbChroma
            // 
            cmbChroma.BackColor1 = Color.Transparent;
            cmbChroma.CaretColor = Color.FromArgb(220, 220, 220);
            cmbChroma.DropDownAnimationFPS = 0;
            cmbChroma.DropDownScrollBarHoverColor = Color.FromArgb(200, 200, 200);
            cmbChroma.ForeColor = Color.Gainsboro;
            cmbChroma.Location = new Point(37, 399);
            cmbChroma.Margin = new Padding(2, 2, 2, 2);
            cmbChroma.Name = "cmbChroma";
            cmbChroma.Size = new Size(160, 32);
            cmbChroma.TabIndex = 38;
            cmbChroma.Text = "选择色度采样";
            // 
            // numQualityValue
            // 
            numQualityValue.DecimalPlaces = 8;
            numQualityValue.Location = new Point(432, 464);
            numQualityValue.Maximum = new decimal(new int[] { 276447232, 23283, 0, 0 });
            numQualityValue.Minimum = new decimal(new int[] { 276447232, 23283, 0, int.MinValue });
            numQualityValue.Name = "numQualityValue";
            numQualityValue.Size = new Size(120, 23);
            numQualityValue.TabIndex = 37;
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.ForeColor = Color.WhiteSmoke;
            label7.Location = new Point(230, 445);
            label7.Name = "label7";
            label7.Size = new Size(56, 17);
            label7.TabIndex = 36;
            label7.Text = "质量指标";
            // 
            // cmbQualityMode
            // 
            cmbQualityMode.BackColor1 = Color.Transparent;
            cmbQualityMode.CaretColor = Color.FromArgb(220, 220, 220);
            cmbQualityMode.DropDownAnimationFPS = 0;
            cmbQualityMode.DropDownMode = LakeUI.ModernComboBox.DropDownDisplayMode.Overlay;
            cmbQualityMode.DropDownScrollBarHoverColor = Color.FromArgb(200, 200, 200);
            cmbQualityMode.ForeColor = Color.Gainsboro;
            cmbQualityMode.Location = new Point(230, 464);
            cmbQualityMode.Margin = new Padding(2, 2, 2, 2);
            cmbQualityMode.Name = "cmbQualityMode";
            cmbQualityMode.Size = new Size(160, 32);
            cmbQualityMode.TabIndex = 35;
            cmbQualityMode.Text = "质量指标";
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.ForeColor = Color.WhiteSmoke;
            label6.Location = new Point(230, 380);
            label6.Name = "label6";
            label6.Size = new Size(56, 17);
            label6.TabIndex = 34;
            label6.Text = "搜索度量";
            // 
            // cmbMetric
            // 
            cmbMetric.BackColor1 = Color.Transparent;
            cmbMetric.CaretColor = Color.FromArgb(220, 220, 220);
            cmbMetric.DropDownAnimationFPS = 0;
            cmbMetric.DropDownMode = LakeUI.ModernComboBox.DropDownDisplayMode.Overlay;
            cmbMetric.DropDownScrollBarHoverColor = Color.FromArgb(200, 200, 200);
            cmbMetric.ForeColor = Color.Gainsboro;
            cmbMetric.Location = new Point(230, 399);
            cmbMetric.Margin = new Padding(2, 2, 2, 2);
            cmbMetric.Name = "cmbMetric";
            cmbMetric.Size = new Size(160, 32);
            cmbMetric.TabIndex = 33;
            cmbMetric.Text = "搜索度量";
            // 
            // grpCrfMode
            // 
            grpCrfMode.Controls.Add(rbCrfFix);
            grpCrfMode.Controls.Add(numCrfMax);
            grpCrfMode.Controls.Add(label5);
            grpCrfMode.Controls.Add(numCrfMin);
            grpCrfMode.Controls.Add(numCrfFix);
            grpCrfMode.Controls.Add(label4);
            grpCrfMode.Controls.Add(rbCrfRange);
            grpCrfMode.ForeColor = Color.WhiteSmoke;
            grpCrfMode.Location = new Point(230, 187);
            grpCrfMode.Name = "grpCrfMode";
            grpCrfMode.Size = new Size(162, 175);
            grpCrfMode.TabIndex = 32;
            grpCrfMode.TabStop = false;
            grpCrfMode.Text = "CRF搜索模式";
            // 
            // rbCrfFix
            // 
            rbCrfFix.AnimationFPS = 0;
            rbCrfFix.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            rbCrfFix.CheckMode = LakeUI.ModernCheckBox.CheckModeEnum.RadioButton;
            rbCrfFix.ForeColor = Color.WhiteSmoke;
            rbCrfFix.Location = new Point(6, 31);
            rbCrfFix.Name = "rbCrfFix";
            rbCrfFix.Size = new Size(81, 23);
            rbCrfFix.TabIndex = 25;
            rbCrfFix.Text = "固定 CRF";
            // 
            // numCrfMax
            // 
            numCrfMax.Location = new Point(90, 142);
            numCrfMax.Maximum = new decimal(new int[] { 63, 0, 0, 0 });
            numCrfMax.Name = "numCrfMax";
            numCrfMax.Size = new Size(60, 23);
            numCrfMax.TabIndex = 29;
            numCrfMax.Value = new decimal(new int[] { 63, 0, 0, 0 });
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.ForeColor = Color.WhiteSmoke;
            label5.Location = new Point(90, 122);
            label5.Name = "label5";
            label5.Size = new Size(56, 17);
            label5.TabIndex = 31;
            label5.Text = "范围上限";
            // 
            // numCrfMin
            // 
            numCrfMin.Location = new Point(6, 142);
            numCrfMin.Maximum = new decimal(new int[] { 63, 0, 0, 0 });
            numCrfMin.Name = "numCrfMin";
            numCrfMin.Size = new Size(60, 23);
            numCrfMin.TabIndex = 28;
            // 
            // numCrfFix
            // 
            numCrfFix.Location = new Point(6, 60);
            numCrfFix.Maximum = new decimal(new int[] { 63, 0, 0, 0 });
            numCrfFix.Name = "numCrfFix";
            numCrfFix.Size = new Size(85, 23);
            numCrfFix.TabIndex = 27;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.ForeColor = Color.WhiteSmoke;
            label4.Location = new Point(6, 122);
            label4.Name = "label4";
            label4.Size = new Size(56, 17);
            label4.TabIndex = 30;
            label4.Text = "范围下限";
            // 
            // rbCrfRange
            // 
            rbCrfRange.BoxCheckedBackColor = Color.FromArgb(0, 120, 215);
            rbCrfRange.CheckMode = LakeUI.ModernCheckBox.CheckModeEnum.RadioButton;
            rbCrfRange.ForeColor = Color.WhiteSmoke;
            rbCrfRange.Location = new Point(6, 96);
            rbCrfRange.Name = "rbCrfRange";
            rbCrfRange.Size = new Size(150, 23);
            rbCrfRange.TabIndex = 26;
            rbCrfRange.Text = "CRF 范围";
            // 
            // modernButton1
            // 
            modernButton1.BackColor1 = Color.Transparent;
            modernButton1.BorderRadius = 10;
            modernButton1.ForeColor = Color.WhiteSmoke;
            modernButton1.Location = new Point(37, 125);
            modernButton1.Margin = new Padding(2);
            modernButton1.Name = "modernButton1";
            modernButton1.Size = new Size(97, 32);
            modernButton1.TabIndex = 24;
            modernButton1.Text = "输出文件名模板";
            // 
            // txtTemplate
            // 
            txtTemplate.BackColor1 = Color.Transparent;
            txtTemplate.ForeColor = Color.WhiteSmoke;
            txtTemplate.Location = new Point(156, 125);
            txtTemplate.Margin = new Padding(2);
            txtTemplate.Name = "txtTemplate";
            txtTemplate.Size = new Size(942, 32);
            txtTemplate.TabIndex = 22;
            txtTemplate.Text = "covers-{index}.avif";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.ForeColor = Color.Gainsboro;
            label3.Location = new Point(432, 245);
            label3.Name = "label3";
            label3.Size = new Size(80, 17);
            label3.TabIndex = 21;
            label3.Text = "最终编码速度";
            // 
            // numFinalCpuUsed
            // 
            numFinalCpuUsed.Location = new Point(432, 265);
            numFinalCpuUsed.Name = "numFinalCpuUsed";
            numFinalCpuUsed.Size = new Size(120, 23);
            numFinalCpuUsed.TabIndex = 20;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.ForeColor = Color.WhiteSmoke;
            label2.Location = new Point(432, 187);
            label2.Name = "label2";
            label2.Size = new Size(56, 17);
            label2.TabIndex = 19;
            label2.Text = "搜索速度";
            // 
            // numSearchCpuUsed
            // 
            numSearchCpuUsed.Location = new Point(432, 207);
            numSearchCpuUsed.Name = "numSearchCpuUsed";
            numSearchCpuUsed.Size = new Size(120, 23);
            numSearchCpuUsed.TabIndex = 18;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.ForeColor = Color.WhiteSmoke;
            label1.Location = new Point(432, 380);
            label1.Name = "label1";
            label1.Size = new Size(110, 17);
            label1.TabIndex = 17;
            label1.Text = "并行ffmpeg任务数";
            // 
            // numJobs
            // 
            numJobs.Location = new Point(432, 399);
            numJobs.Name = "numJobs";
            numJobs.Size = new Size(120, 23);
            numJobs.TabIndex = 16;
            // 
            // btnStop
            // 
            btnStop.AnimationDuration = 0;
            btnStop.AnimationFPS = 0;
            btnStop.BackColor1 = Color.Transparent;
            btnStop.BorderRadius = 10;
            btnStop.ForeColor = Color.WhiteSmoke;
            btnStop.HoverBackColor1 = Color.DarkGray;
            btnStop.Location = new Point(230, 548);
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
            chkSearch.ForeColor = Color.WhiteSmoke;
            chkSearch.Location = new Point(625, 187);
            chkSearch.Name = "chkSearch";
            chkSearch.Size = new Size(150, 24);
            chkSearch.TabIndex = 14;
            chkSearch.Text = "是否启用 CRF 搜索";
            // 
            // lblQuality
            // 
            lblQuality.AutoSize = true;
            lblQuality.ForeColor = Color.WhiteSmoke;
            lblQuality.Location = new Point(432, 445);
            lblQuality.Name = "lblQuality";
            lblQuality.Size = new Size(80, 17);
            lblQuality.TabIndex = 12;
            lblQuality.Text = "质量指标目标";
            // 
            // cmbEncoder
            // 
            cmbEncoder.BackColor1 = Color.Transparent;
            cmbEncoder.CaretColor = Color.FromArgb(220, 220, 220);
            cmbEncoder.DropDownAnimationFPS = 0;
            cmbEncoder.DropDownMode = LakeUI.ModernComboBox.DropDownDisplayMode.Overlay;
            cmbEncoder.DropDownScrollBarHoverColor = Color.FromArgb(200, 200, 200);
            cmbEncoder.ForeColor = Color.WhiteSmoke;
            cmbEncoder.Location = new Point(37, 268);
            cmbEncoder.Margin = new Padding(2, 2, 2, 2);
            cmbEncoder.Name = "cmbEncoder";
            cmbEncoder.Size = new Size(160, 32);
            cmbEncoder.TabIndex = 11;
            cmbEncoder.Text = "选择编码器";
            // 
            // lblEncoder
            // 
            lblEncoder.AutoSize = true;
            lblEncoder.ForeColor = Color.WhiteSmoke;
            lblEncoder.Location = new Point(37, 249);
            lblEncoder.Name = "lblEncoder";
            lblEncoder.Size = new Size(44, 17);
            lblEncoder.TabIndex = 10;
            lblEncoder.Text = "编码器";
            // 
            // cmbPreset
            // 
            cmbPreset.BackColor1 = Color.Transparent;
            cmbPreset.CaretColor = Color.FromArgb(220, 220, 220);
            cmbPreset.DropDownAnimationFPS = 0;
            cmbPreset.DropDownScrollBarHoverColor = Color.FromArgb(200, 200, 200);
            cmbPreset.ForeColor = Color.Gainsboro;
            cmbPreset.Location = new Point(37, 204);
            cmbPreset.Margin = new Padding(2, 2, 2, 2);
            cmbPreset.Name = "cmbPreset";
            cmbPreset.Size = new Size(160, 32);
            cmbPreset.TabIndex = 9;
            cmbPreset.Text = "选择预设";
            // 
            // lblPreset
            // 
            lblPreset.AutoSize = true;
            lblPreset.ForeColor = Color.WhiteSmoke;
            lblPreset.Location = new Point(37, 185);
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
            progressBar1.Location = new Point(37, 607);
            progressBar1.Margin = new Padding(2, 2, 2, 2);
            progressBar1.Name = "progressBar1";
            progressBar1.Size = new Size(1061, 20);
            progressBar1.TabIndex = 7;
            progressBar1.TextPadding = new Padding(0);
            progressBar1.TrackColor = Color.White;
            // 
            // btnBrowseOutput
            // 
            btnBrowseOutput.BackColor1 = Color.Transparent;
            btnBrowseOutput.BorderRadius = 10;
            btnBrowseOutput.ForeColor = Color.WhiteSmoke;
            btnBrowseOutput.HoverBackColor1 = Color.Gainsboro;
            btnBrowseOutput.Location = new Point(37, 78);
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
            txtOutput.ForeColor = Color.WhiteSmoke;
            txtOutput.Location = new Point(156, 78);
            txtOutput.Margin = new Padding(2);
            txtOutput.Name = "txtOutput";
            txtOutput.Size = new Size(942, 32);
            txtOutput.TabIndex = 5;
            txtOutput.WaterText = "输出路径";
            // 
            // btnBrowseInput
            // 
            btnBrowseInput.AnimationDuration = 0;
            btnBrowseInput.AnimationFPS = 0;
            btnBrowseInput.BackColor1 = Color.Transparent;
            btnBrowseInput.BorderRadius = 10;
            btnBrowseInput.ForeColor = Color.WhiteSmoke;
            btnBrowseInput.HoverBackColor1 = Color.Gainsboro;
            btnBrowseInput.Location = new Point(37, 30);
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
            btnStart.AnimationDuration = 0;
            btnStart.AnimationFPS = 0;
            btnStart.BackColor1 = Color.Transparent;
            btnStart.BorderRadius = 10;
            btnStart.ForeColor = Color.WhiteSmoke;
            btnStart.HoverBackColor1 = Color.DarkGray;
            btnStart.Location = new Point(37, 548);
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
            txtInput.ForeColor = Color.WhiteSmoke;
            txtInput.Location = new Point(156, 30);
            txtInput.Margin = new Padding(2);
            txtInput.Name = "txtInput";
            txtInput.Size = new Size(942, 32);
            txtInput.TabIndex = 0;
            txtInput.WaterText = "输入路径";
            // 
            // FormEncode
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1114, 681);
            Controls.Add(modernPanel1);
            DoubleBuffered = true;
            Name = "FormEncode";
            Text = "FormEncode";
            modernPanel1.ResumeLayout(false);
            modernPanel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numMaxRes).EndInit();
            ((System.ComponentModel.ISupportInitialize)numQualityValue).EndInit();
            grpCrfMode.ResumeLayout(false);
            grpCrfMode.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numCrfMax).EndInit();
            ((System.ComponentModel.ISupportInitialize)numCrfMin).EndInit();
            ((System.ComponentModel.ISupportInitialize)numCrfFix).EndInit();
            ((System.ComponentModel.ISupportInitialize)numFinalCpuUsed).EndInit();
            ((System.ComponentModel.ISupportInitialize)numSearchCpuUsed).EndInit();
            ((System.ComponentModel.ISupportInitialize)numJobs).EndInit();
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
        private LakeUI.ModernCheckBox chkSearch;
        private LakeUI.ModernButton btnStop;
        private NumericUpDown numJobs;
        private Label label1;
        private NumericUpDown numSearchCpuUsed;
        private Label label2;
        private NumericUpDown numFinalCpuUsed;
        private Label label3;
        private LakeUI.ModernTextBox txtTemplate;
        private LakeUI.ModernButton modernButton1;
        private LakeUI.ModernCheckBox rbCrfFix;
        private LakeUI.ModernCheckBox rbCrfRange;
        private NumericUpDown numCrfFix;
        private NumericUpDown numCrfMin;
        private NumericUpDown numCrfMax;
        private Label label4;
        private Label label5;
        private GroupBox grpCrfMode;
        private LakeUI.ModernComboBox cmbMetric;
        private Label label6;
        private LakeUI.ModernComboBox cmbQualityMode;
        private Label label7;
        private NumericUpDown numQualityValue;
        private LakeUI.ModernComboBox cmbChroma;
        private Label label8;
        private LakeUI.ModernComboBox cmbBitDepth;
        private Label label9;
        private LakeUI.ModernCheckBox chkLossless;
        private LakeUI.ModernCheckBox chkRecursive;
        private NumericUpDown numMaxRes;
        private Label label10;
        private LakeUI.ModernCheckBox chkOutputFullRes;
        private LakeUI.ModernComboBox cmbConflict;
        private Label label11;
        private LakeUI.ModernCheckBox chkSerialEncode;
        private LakeUI.ModernCheckBox chkPriorSearch;
        private LakeUI.ModernCheckBox chkProxy;
    }
}