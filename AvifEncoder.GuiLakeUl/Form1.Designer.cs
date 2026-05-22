using LakeUI;
namespace AvifEncoder.GuiLakeUl
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            lblInput = new Label();
            txtInput = new ModernTextBox();
            btnBrowseInput = new ModernButton();
            lblOutput = new Label();
            txtOutput = new ModernTextBox();
            btnBrowseOutput = new ModernButton();
            cmbPreset = new ModernComboBox();
            btnStart = new ModernButton();
            progressBar1 = new ExcellentProgressBar();
            rtbLog = new ModernTextBox();
            cmbEncoder = new ModernComboBox();
            numJobs = new NumericUpDown();
            chkSearch = new ModernCheckBox();
            cmbMetric = new ModernComboBox();
            txtTemplate = new ModernTextBox();
            label1 = new Label();
            label2 = new Label();
            label3 = new Label();
            label4 = new Label();
            chkRecursive = new ModernCheckBox();
            cmbQualityMode = new ModernComboBox();
            label5 = new Label();
            label6 = new Label();
            numQualityValue = new NumericUpDown();
            label7 = new Label();
            grpCrfMode = new GroupBox();
            label9 = new Label();
            label8 = new Label();
            numCrfMax = new NumericUpDown();
            numCrfMin = new NumericUpDown();
            rbCrfRange = new RadioButton();
            numCrfFix = new NumericUpDown();
            rbCrfFix = new RadioButton();
            cmbChroma = new ModernComboBox();
            label10 = new Label();
            cmbBitDepth = new ModernComboBox();
            label11 = new Label();
            chkLossless = new ModernCheckBox();
            numMaxRes = new NumericUpDown();
            label12 = new Label();
            chkOutputFullRes = new ModernCheckBox();
            chkSerialEncode = new ModernCheckBox();
            chkPriorSearch = new ModernCheckBox();
            chkProxy = new ModernCheckBox();
            chkDryRun = new ModernCheckBox();
            chkVerbose = new ModernCheckBox();
            cmbConflict = new ModernComboBox();
            label13 = new Label();
            label14 = new Label();
            numSearchCpuUsed = new NumericUpDown();
            numFinalCpuUsed = new NumericUpDown();
            label15 = new Label();
            ((System.ComponentModel.ISupportInitialize)numJobs).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numQualityValue).BeginInit();
            grpCrfMode.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numCrfMax).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numCrfMin).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numCrfFix).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numMaxRes).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numSearchCpuUsed).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numFinalCpuUsed).BeginInit();
            SuspendLayout();
            // 
            // lblInput
            // 
            lblInput.AutoSize = true;
            lblInput.Location = new Point(12, 25);
            lblInput.Name = "lblInput";
            lblInput.Size = new Size(86, 24);
            lblInput.TabIndex = 0;
            lblInput.Text = "输入目录:";
            lblInput.Click += label1_Click;
            // 
            // txtInput
            // 
            txtInput.Location = new Point(118, 22);
            txtInput.Name = "txtInput";
            txtInput.Size = new Size(1296, 30);
            txtInput.TabIndex = 1;
            // 
            // btnBrowseInput
            // 
            btnBrowseInput.Location = new Point(1439, 20);
            btnBrowseInput.Name = "btnBrowseInput";
            btnBrowseInput.Size = new Size(112, 34);
            btnBrowseInput.TabIndex = 2;
            btnBrowseInput.Text = "浏览...";
            // btnBrowseInput.UseVisualStyleBackColor not applicable for ModernButton
            btnBrowseInput.Click += btnBrowseInput_Click;
            // 
            // lblOutput
            // 
            lblOutput.AutoSize = true;
            lblOutput.Location = new Point(12, 70);
            lblOutput.Name = "lblOutput";
            lblOutput.Size = new Size(86, 24);
            lblOutput.TabIndex = 3;
            lblOutput.Text = "输出目录:";
            lblOutput.Click += lblOutput_Click;
            // 
            // txtOutput
            // 
            txtOutput.Location = new Point(118, 70);
            txtOutput.Name = "txtOutput";
            txtOutput.Size = new Size(1296, 30);
            txtOutput.TabIndex = 4;
            // 
            // btnBrowseOutput
            // 
            btnBrowseOutput.Location = new Point(1439, 70);
            btnBrowseOutput.Name = "btnBrowseOutput";
            btnBrowseOutput.Size = new Size(112, 34);
            btnBrowseOutput.TabIndex = 5;
            btnBrowseOutput.Text = "浏览...";
            // btnBrowseOutput.UseVisualStyleBackColor not applicable for ModernButton
            btnBrowseOutput.Click += btnBrowseOutput_Click;
            // 
            // cmbPreset
            // 
            // cmbPreset.DropDownStyle/FormattingEnabled not applicable for ModernComboBox
            cmbPreset.Location = new Point(118, 211);
            cmbPreset.Name = "cmbPreset";
            cmbPreset.Size = new Size(182, 32);
            cmbPreset.TabIndex = 6;
            cmbPreset.SelectedIndexChanged += cmbPreset_SelectedIndexChanged;
            // 
            // btnStart
            // 
            btnStart.Location = new Point(12, 708);
            btnStart.Name = "btnStart";
            btnStart.Size = new Size(98, 36);
            btnStart.TabIndex = 7;
            btnStart.Text = "开始转换";
            // btnStart.UseVisualStyleBackColor not applicable for ModernButton
            btnStart.Click += btnStart_Click;
            // 
            // progressBar1
            // 
            progressBar1.Location = new Point(120, 708);
            progressBar1.Name = "progressBar1";
            progressBar1.Size = new Size(1296, 36);
            // progressBar1.Style = ProgressBarStyle.Marquee; // ExcellentProgressBar uses different API
            progressBar1.TabIndex = 8;
            progressBar1.Click += progressBar1_Click;
            // 
            // rtbLog
            // 
            rtbLog.Location = new Point(118, 760);
            rtbLog.Name = "rtbLog";
            rtbLog.Size = new Size(1296, 238);
            rtbLog.TabIndex = 9;
            rtbLog.Text = "";
            // 
            // cmbEncoder
            // 
            // cmbEncoder.DropDownStyle/FormattingEnabled not applicable for ModernComboBox
            cmbEncoder.Location = new Point(118, 282);
            cmbEncoder.Name = "cmbEncoder";
            cmbEncoder.Size = new Size(182, 32);
            cmbEncoder.TabIndex = 10;
            // 
            // numJobs
            // 
            numJobs.Location = new Point(120, 355);
            numJobs.Name = "numJobs";
            numJobs.Size = new Size(180, 30);
            numJobs.TabIndex = 11;
            // 
            // chkSearch
            // 
            chkSearch.AutoSize = true;
            chkSearch.Checked = true;
            chkSearch.Location = new Point(1082, 133);
            chkSearch.Name = "chkSearch";
            chkSearch.Size = new Size(252, 28);
            chkSearch.TabIndex = 12;
            chkSearch.Text = "勾选启用搜索（默认勾选）";
            chkSearch.Checked = true;
            // 
            // cmbMetric
            // 
            // cmbMetric.DropDownStyle/FormattingEnabled not applicable for ModernComboBox
            cmbMetric.Location = new Point(120, 433);
            cmbMetric.Name = "cmbMetric";
            cmbMetric.Size = new Size(432, 32);
            cmbMetric.TabIndex = 13;
            // 
            // txtTemplate
            // 
            txtTemplate.Location = new Point(118, 141);
            txtTemplate.Name = "txtTemplate";
            txtTemplate.Size = new Size(286, 30);
            txtTemplate.TabIndex = 14;
            txtTemplate.Text = "covers-{index}.avif";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(118, 114);
            label1.Name = "label1";
            label1.Size = new Size(409, 24);
            label1.TabIndex = 15;
            label1.Text = "输出文件名模板，支持 {name}、{index} 等占位符";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(118, 184);
            label2.Name = "label2";
            label2.Size = new Size(463, 24);
            label2.TabIndex = 16;
            label2.Text = "预设快速配置：影响 CRF、搜索开关、像素格式等默认值";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(118, 255);
            label3.Name = "label3";
            label3.Size = new Size(415, 24);
            label3.TabIndex = 17;
            label3.Text = "AV1 编码器选择，例如 libaom-av1、libsvtav1 等";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(118, 328);
            label4.Name = "label4";
            label4.Size = new Size(324, 24);
            label4.TabIndex = 18;
            label4.Text = "并行任务数，0 表示自动根据 CPU 计算";
            // 
            // chkRecursive
            // 
            chkRecursive.AutoSize = true;
            chkRecursive.Location = new Point(1082, 201);
            chkRecursive.Name = "chkRecursive";
            chkRecursive.Size = new Size(162, 28);
            chkRecursive.TabIndex = 19;
            chkRecursive.Text = "递归处理子目录";
            // chkRecursive.UseVisualStyleBackColor not applicable for ModernCheckBox
            // 
            // cmbQualityMode
            // 
            // cmbQualityMode.DropDownStyle/FormattingEnabled not applicable for ModernComboBox
            cmbQualityMode.Location = new Point(120, 509);
            cmbQualityMode.Name = "cmbQualityMode";
            cmbQualityMode.Size = new Size(182, 32);
            cmbQualityMode.TabIndex = 20;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(118, 406);
            label5.Name = "label5";
            label5.Size = new Size(545, 24);
            label5.TabIndex = 21;
            label5.Text = "质量评价度量：vmaf、xpsnr、ssim、ms-ssim、mix混合评分等等";
            label5.Click += label5_Click;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(120, 482);
            label6.Name = "label6";
            label6.Size = new Size(546, 24);
            label6.TabIndex = 22;
            label6.Text = "质量目标类型：VMAF、XPSNR、SSIM、PSNR-Y、MS-SSIM等等";
            // 
            // numQualityValue
            // 
            numQualityValue.DecimalPlaces = 1;
            numQualityValue.Location = new Point(448, 509);
            numQualityValue.Name = "numQualityValue";
            numQualityValue.Size = new Size(180, 30);
            numQualityValue.TabIndex = 23;
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(324, 511);
            label7.Name = "label7";
            label7.Size = new Size(118, 24);
            label7.TabIndex = 24;
            label7.Text = "目标质量数值";
            // 
            // grpCrfMode
            // 
            grpCrfMode.Controls.Add(label9);
            grpCrfMode.Controls.Add(label8);
            grpCrfMode.Controls.Add(numCrfMax);
            grpCrfMode.Controls.Add(numCrfMin);
            grpCrfMode.Controls.Add(rbCrfRange);
            grpCrfMode.Controls.Add(numCrfFix);
            grpCrfMode.Controls.Add(rbCrfFix);
            grpCrfMode.Location = new Point(718, 114);
            grpCrfMode.Name = "grpCrfMode";
            grpCrfMode.Size = new Size(315, 238);
            grpCrfMode.TabIndex = 25;
            grpCrfMode.TabStop = false;
            grpCrfMode.Text = "CRF 模式";
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.Location = new Point(16, 171);
            label9.Name = "label9";
            label9.Size = new Size(82, 24);
            label9.TabIndex = 6;
            label9.Text = "范围上限";
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.Location = new Point(16, 105);
            label8.Name = "label8";
            label8.Size = new Size(82, 24);
            label8.TabIndex = 5;
            label8.Text = "范围下限";
            label8.Click += label8_Click;
            // 
            // numCrfMax
            // 
            numCrfMax.Location = new Point(16, 198);
            numCrfMax.Maximum = new decimal(new int[] { 63, 0, 0, 0 });
            numCrfMax.Name = "numCrfMax";
            numCrfMax.Size = new Size(180, 30);
            numCrfMax.TabIndex = 4;
            numCrfMax.Value = new decimal(new int[] { 63, 0, 0, 0 });
            // 
            // numCrfMin
            // 
            numCrfMin.Location = new Point(16, 135);
            numCrfMin.Maximum = new decimal(new int[] { 63, 0, 0, 0 });
            numCrfMin.Name = "numCrfMin";
            numCrfMin.Size = new Size(180, 30);
            numCrfMin.TabIndex = 3;
            // 
            // rbCrfRange
            // 
            rbCrfRange.AutoSize = true;
            rbCrfRange.Checked = true;
            rbCrfRange.Location = new Point(16, 70);
            rbCrfRange.Name = "rbCrfRange";
            rbCrfRange.Size = new Size(107, 28);
            rbCrfRange.TabIndex = 2;
            rbCrfRange.TabStop = true;
            rbCrfRange.Text = "范围搜索";
            rbCrfRange.UseVisualStyleBackColor = true;
            // 
            // numCrfFix
            // 
            numCrfFix.Location = new Point(123, 29);
            numCrfFix.Maximum = new decimal(new int[] { 63, 0, 0, 0 });
            numCrfFix.Name = "numCrfFix";
            numCrfFix.Size = new Size(180, 30);
            numCrfFix.TabIndex = 1;
            numCrfFix.Value = new decimal(new int[] { 30, 0, 0, 0 });
            // 
            // rbCrfFix
            // 
            rbCrfFix.AutoSize = true;
            rbCrfFix.Location = new Point(16, 29);
            rbCrfFix.Name = "rbCrfFix";
            rbCrfFix.Size = new Size(110, 28);
            rbCrfFix.TabIndex = 0;
            rbCrfFix.TabStop = true;
            rbCrfFix.Text = "固定 CRF";
            rbCrfFix.UseVisualStyleBackColor = true;
            // 
            // cmbChroma
            // 
            // cmbChroma.DropDownStyle/FormattingEnabled not applicable for ModernComboBox
            cmbChroma.Location = new Point(718, 388);
            cmbChroma.Name = "cmbChroma";
            cmbChroma.Size = new Size(182, 32);
            cmbChroma.TabIndex = 26;
            // 
            // label10
            // 
            label10.AutoSize = true;
            label10.Location = new Point(718, 361);
            label10.Name = "label10";
            label10.Size = new Size(82, 24);
            label10.TabIndex = 27;
            label10.Text = "色度采样";
            // 
            // cmbBitDepth
            // 
            // cmbBitDepth.DropDownStyle/FormattingEnabled not applicable for ModernComboBox
            cmbBitDepth.Location = new Point(718, 450);
            cmbBitDepth.Name = "cmbBitDepth";
            cmbBitDepth.Size = new Size(182, 32);
            cmbBitDepth.TabIndex = 28;
            // 
            // label11
            // 
            label11.AutoSize = true;
            label11.Location = new Point(718, 423);
            label11.Name = "label11";
            label11.Size = new Size(82, 24);
            label11.TabIndex = 29;
            label11.Text = "输出位深";
            label11.Click += label11_Click;
            // 
            // chkLossless
            // 
            chkLossless.AutoSize = true;
            chkLossless.Location = new Point(1082, 167);
            chkLossless.Name = "chkLossless";
            chkLossless.Size = new Size(263, 28);
            chkLossless.TabIndex = 30;
            chkLossless.Text = "无损模式(有bug不建议使用)";
            // chkLossless.UseVisualStyleBackColor not applicable for ModernCheckBox
            // 
            // numMaxRes
            // 
            numMaxRes.Location = new Point(118, 581);
            numMaxRes.Maximum = new decimal(new int[] { 8192, 0, 0, 0 });
            numMaxRes.Name = "numMaxRes";
            numMaxRes.Size = new Size(180, 30);
            numMaxRes.TabIndex = 31;
            // 
            // label12
            // 
            label12.AutoSize = true;
            label12.Location = new Point(118, 554);
            label12.Name = "label12";
            label12.Size = new Size(332, 24);
            label12.TabIndex = 32;
            label12.Text = "最大分辨率限制（长边），0 表示不缩放";
            // 
            // chkOutputFullRes
            // 
            chkOutputFullRes.AutoSize = true;
            chkOutputFullRes.Location = new Point(1082, 236);
            chkOutputFullRes.Name = "chkOutputFullRes";
            chkOutputFullRes.Size = new Size(234, 28);
            chkOutputFullRes.TabIndex = 33;
            chkOutputFullRes.Text = "最终输出保留原始分辨率";
            // chkOutputFullRes.UseVisualStyleBackColor not applicable for ModernCheckBox
            // 
            // chkSerialEncode
            // 
            chkSerialEncode.AutoSize = true;
            chkSerialEncode.Location = new Point(1082, 270);
            chkSerialEncode.Name = "chkSerialEncode";
            chkSerialEncode.Size = new Size(198, 28);
            chkSerialEncode.TabIndex = 34;
            chkSerialEncode.Text = "极限压缩（单线程）";
            // chkSerialEncode.UseVisualStyleBackColor not applicable for ModernCheckBox
            // 
            // chkPriorSearch
            // 
            chkPriorSearch.AutoSize = true;
            chkPriorSearch.Location = new Point(1082, 304);
            chkPriorSearch.Name = "chkPriorSearch";
            chkPriorSearch.Size = new Size(180, 28);
            chkPriorSearch.TabIndex = 35;
            chkPriorSearch.Text = "先验搜索（更快）";
            // chkPriorSearch.UseVisualStyleBackColor not applicable for ModernCheckBox
            // 
            // chkProxy
            // 
            chkProxy.AutoSize = true;
            chkProxy.Location = new Point(1082, 338);
            chkProxy.Name = "chkProxy";
            chkProxy.Size = new Size(234, 28);
            chkProxy.TabIndex = 36;
            chkProxy.Text = "代理搜索（需先验搜索）";
            // chkProxy.UseVisualStyleBackColor not applicable for ModernCheckBox
            // 
            // chkDryRun
            // 
            chkDryRun.AutoSize = true;
            chkDryRun.Location = new Point(1082, 372);
            chkDryRun.Name = "chkDryRun";
            chkDryRun.Size = new Size(216, 28);
            chkDryRun.TabIndex = 37;
            chkDryRun.Text = "仅模拟运行（不编码）";
            // chkDryRun.UseVisualStyleBackColor not applicable for ModernCheckBox
            // 
            // chkVerbose
            // 
            chkVerbose.AutoSize = true;
            chkVerbose.Location = new Point(1082, 402);
            chkVerbose.Name = "chkVerbose";
            chkVerbose.Size = new Size(108, 28);
            chkVerbose.TabIndex = 38;
            chkVerbose.Text = "详细日志";
            // chkVerbose.UseVisualStyleBackColor not applicable for ModernCheckBox
            // 
            // cmbConflict
            // 
            // cmbConflict.DropDownStyle/FormattingEnabled not applicable for ModernComboBox
            cmbConflict.Location = new Point(718, 512);
            cmbConflict.Name = "cmbConflict";
            cmbConflict.Size = new Size(182, 32);
            cmbConflict.TabIndex = 39;
            // 
            // label13
            // 
            label13.AutoSize = true;
            label13.Location = new Point(718, 485);
            label13.Name = "label13";
            label13.Size = new Size(136, 24);
            label13.TabIndex = 40;
            label13.Text = "当文件已存在时";
            label13.Click += label13_Click;
            // 
            // label14
            // 
            label14.AutoSize = true;
            label14.Location = new Point(718, 554);
            label14.Name = "label14";
            label14.Size = new Size(118, 24);
            label14.TabIndex = 41;
            label14.Text = "设置搜索速度";
            // 
            // numSearchCpuUsed
            // 
            numSearchCpuUsed.Location = new Point(718, 581);
            numSearchCpuUsed.Name = "numSearchCpuUsed";
            numSearchCpuUsed.Size = new Size(180, 30);
            numSearchCpuUsed.TabIndex = 42;
            numSearchCpuUsed.ValueChanged += numSearchCpuUsed_ValueChanged;
            // 
            // numFinalCpuUsed
            // 
            numFinalCpuUsed.Location = new Point(718, 647);
            numFinalCpuUsed.Name = "numFinalCpuUsed";
            numFinalCpuUsed.Size = new Size(180, 30);
            numFinalCpuUsed.TabIndex = 43;
            // 
            // label15
            // 
            label15.AutoSize = true;
            label15.Location = new Point(718, 620);
            label15.Name = "label15";
            label15.Size = new Size(154, 24);
            label15.TabIndex = 44;
            label15.Text = "设置最终编码速度";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(11F, 24F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1580, 1010);

            // Create tab panels to split form into multiple pages
            pnlBasic = new ModernPanel();
            pnlEncode = new ModernPanel();
            pnlQuality = new ModernPanel();
            pnlLog = new ModernPanel();

            // Configure panels
            pnlBasic.Location = new Point(0, 0);
            pnlBasic.Name = "pnlBasic";
            pnlBasic.Size = new Size(1580, 900);
            pnlBasic.BackColor = Color.Transparent;

            pnlEncode.Location = new Point(0, 0);
            pnlEncode.Name = "pnlEncode";
            pnlEncode.Size = new Size(1580, 900);
            pnlEncode.BackColor = Color.Transparent;
            pnlEncode.Visible = false;

            pnlQuality.Location = new Point(0, 0);
            pnlQuality.Name = "pnlQuality";
            pnlQuality.Size = new Size(1580, 900);
            pnlQuality.BackColor = Color.Transparent;
            pnlQuality.Visible = false;

            pnlLog.Location = new Point(0, 0);
            pnlLog.Name = "pnlLog";
            pnlLog.Size = new Size(1580, 900);
            pnlLog.BackColor = Color.Transparent;
            pnlLog.Visible = false;

            // Add controls to panels (grouped logically)
            // Basic
            pnlBasic.Controls.Add(lblInput);
            pnlBasic.Controls.Add(txtInput);
            pnlBasic.Controls.Add(btnBrowseInput);
            pnlBasic.Controls.Add(lblOutput);
            pnlBasic.Controls.Add(txtOutput);
            pnlBasic.Controls.Add(btnBrowseOutput);
            pnlBasic.Controls.Add(txtTemplate);
            pnlBasic.Controls.Add(label1);
            pnlBasic.Controls.Add(cmbPreset);
            pnlBasic.Controls.Add(label2);
            pnlBasic.Controls.Add(btnStart);
            pnlBasic.Controls.Add(progressBar1);

            // Encode
            pnlEncode.Controls.Add(cmbEncoder);
            pnlEncode.Controls.Add(numJobs);
            pnlEncode.Controls.Add(chkSearch);
            pnlEncode.Controls.Add(chkRecursive);
            pnlEncode.Controls.Add(grpCrfMode);
            pnlEncode.Controls.Add(cmbChroma);
            pnlEncode.Controls.Add(cmbBitDepth);
            pnlEncode.Controls.Add(chkLossless);
            pnlEncode.Controls.Add(numMaxRes);
            pnlEncode.Controls.Add(label12);
            pnlEncode.Controls.Add(chkOutputFullRes);
            pnlEncode.Controls.Add(chkSerialEncode);
            pnlEncode.Controls.Add(chkPriorSearch);
            pnlEncode.Controls.Add(chkProxy);
            pnlEncode.Controls.Add(chkDryRun);
            pnlEncode.Controls.Add(chkVerbose);
            pnlEncode.Controls.Add(cmbConflict);
            pnlEncode.Controls.Add(label13);
            pnlEncode.Controls.Add(label14);
            pnlEncode.Controls.Add(numSearchCpuUsed);
            pnlEncode.Controls.Add(numFinalCpuUsed);
            pnlEncode.Controls.Add(label15);

            // Quality
            pnlQuality.Controls.Add(cmbMetric);
            pnlQuality.Controls.Add(cmbQualityMode);
            pnlQuality.Controls.Add(numQualityValue);
            pnlQuality.Controls.Add(label5);
            pnlQuality.Controls.Add(label6);
            pnlQuality.Controls.Add(label7);

            // Log
            pnlLog.Controls.Add(rtbLog);

            // Add panels to form
            Controls.Add(pnlBasic);
            Controls.Add(pnlEncode);
            Controls.Add(pnlQuality);
            Controls.Add(pnlLog);

            Name = "Form1";
            Text = "输出目录";
            ((System.ComponentModel.ISupportInitialize)numJobs).EndInit();
            ((System.ComponentModel.ISupportInitialize)numQualityValue).EndInit();
            grpCrfMode.ResumeLayout(false);
            grpCrfMode.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numCrfMax).EndInit();
            ((System.ComponentModel.ISupportInitialize)numCrfMin).EndInit();
            ((System.ComponentModel.ISupportInitialize)numCrfFix).EndInit();
            ((System.ComponentModel.ISupportInitialize)numMaxRes).EndInit();
            ((System.ComponentModel.ISupportInitialize)numSearchCpuUsed).EndInit();
            ((System.ComponentModel.ISupportInitialize)numFinalCpuUsed).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label lblInput;
        private ModernTextBox txtInput;
        private ModernButton btnBrowseInput;
        private Label lblOutput;
        private ModernTextBox txtOutput;
        private ModernButton btnBrowseOutput;
        private ModernComboBox cmbPreset;
        private ModernButton btnStart;
        private ExcellentProgressBar progressBar1;
        private ModernTextBox rtbLog;
        private ModernComboBox cmbEncoder;
        private NumericUpDown numJobs;
        private ModernCheckBox chkSearch;
        private ModernComboBox cmbMetric;
        private ModernTextBox txtTemplate;
        private Label label1;
        private Label label2;
        private Label label3;
        private Label label4;
        private ModernCheckBox chkRecursive;
        private ModernComboBox cmbQualityMode;
        private Label label5;
        private Label label6;
        private NumericUpDown numQualityValue;
        private Label label7;
        private GroupBox grpCrfMode;
        private RadioButton rbCrfFix;
        private NumericUpDown numCrfFix;
        private RadioButton rbCrfRange;
        private NumericUpDown numCrfMin;
        private NumericUpDown numCrfMax;
        private Label label8;
        private Label label9;
        private ModernComboBox cmbChroma;
        private Label label10;
        private ModernComboBox cmbBitDepth;
        private Label label11;
        private ModernCheckBox chkLossless;
        private NumericUpDown numMaxRes;
        private Label label12;
        private ModernCheckBox chkOutputFullRes;
        private ModernCheckBox chkSerialEncode;
        private ModernCheckBox chkPriorSearch;
        private ModernCheckBox chkProxy;
        private ModernCheckBox chkDryRun;
        private ModernCheckBox chkVerbose;
        private ModernComboBox cmbConflict;
        private Label label13;
        private Label label14;
        private NumericUpDown numSearchCpuUsed;
        private NumericUpDown numFinalCpuUsed;
        private Label label15;

        // Tab panels
        private ModernPanel pnlBasic;
        private ModernPanel pnlEncode;
        private ModernPanel pnlQuality;
        private ModernPanel pnlLog;
    }
}
