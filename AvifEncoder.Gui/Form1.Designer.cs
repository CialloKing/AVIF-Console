namespace AvifEncoder.Gui
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
            txtInput = new TextBox();
            btnBrowseInput = new Button();
            lblOutput = new Label();
            txtOutput = new TextBox();
            btnBrowseOutput = new Button();
            cmbPreset = new ComboBox();
            btnStart = new Button();
            progressBar1 = new ProgressBar();
            rtbLog = new RichTextBox();
            cmbEncoder = new ComboBox();
            numJobs = new NumericUpDown();
            chkSearch = new CheckBox();
            cmbMetric = new ComboBox();
            txtTemplate = new TextBox();
            label1 = new Label();
            label2 = new Label();
            label3 = new Label();
            label4 = new Label();
            chkRecursive = new CheckBox();
            cmbQualityMode = new ComboBox();
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
            cmbChroma = new ComboBox();
            label10 = new Label();
            cmbBitDepth = new ComboBox();
            label11 = new Label();
            chkLossless = new CheckBox();
            numMaxRes = new NumericUpDown();
            label12 = new Label();
            chkOutputFullRes = new CheckBox();
            chkSerialEncode = new CheckBox();
            chkPriorSearch = new CheckBox();
            chkProxy = new CheckBox();
            chkDryRun = new CheckBox();
            chkVerbose = new CheckBox();
            cmbConflict = new ComboBox();
            label13 = new Label();
            label14 = new Label();
            numSearchCpuUsed = new NumericUpDown();
            numFinalCpuUsed = new NumericUpDown();
            label15 = new Label();
            chkSweep = new CheckBox();
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
            lblInput.Location = new Point(8, 18);
            lblInput.Margin = new Padding(2, 0, 2, 0);
            lblInput.Name = "lblInput";
            lblInput.Size = new Size(59, 17);
            lblInput.TabIndex = 0;
            lblInput.Text = "输入目录:";
            lblInput.Click += label1_Click;
            // 
            // txtInput
            // 
            txtInput.Location = new Point(75, 16);
            txtInput.Margin = new Padding(2);
            txtInput.Name = "txtInput";
            txtInput.Size = new Size(826, 23);
            txtInput.TabIndex = 1;
            // 
            // btnBrowseInput
            // 
            btnBrowseInput.Location = new Point(916, 14);
            btnBrowseInput.Margin = new Padding(2);
            btnBrowseInput.Name = "btnBrowseInput";
            btnBrowseInput.Size = new Size(71, 24);
            btnBrowseInput.TabIndex = 2;
            btnBrowseInput.Text = "浏览...";
            btnBrowseInput.UseVisualStyleBackColor = true;
            btnBrowseInput.Click += btnBrowseInput_Click;
            // 
            // lblOutput
            // 
            lblOutput.AutoSize = true;
            lblOutput.Location = new Point(8, 50);
            lblOutput.Margin = new Padding(2, 0, 2, 0);
            lblOutput.Name = "lblOutput";
            lblOutput.Size = new Size(59, 17);
            lblOutput.TabIndex = 3;
            lblOutput.Text = "输出目录:";
            lblOutput.Click += lblOutput_Click;
            // 
            // txtOutput
            // 
            txtOutput.Location = new Point(75, 50);
            txtOutput.Margin = new Padding(2);
            txtOutput.Name = "txtOutput";
            txtOutput.Size = new Size(826, 23);
            txtOutput.TabIndex = 4;
            // 
            // btnBrowseOutput
            // 
            btnBrowseOutput.Location = new Point(916, 50);
            btnBrowseOutput.Margin = new Padding(2);
            btnBrowseOutput.Name = "btnBrowseOutput";
            btnBrowseOutput.Size = new Size(71, 24);
            btnBrowseOutput.TabIndex = 5;
            btnBrowseOutput.Text = "浏览...";
            btnBrowseOutput.UseVisualStyleBackColor = true;
            btnBrowseOutput.Click += btnBrowseOutput_Click;
            // 
            // cmbPreset
            // 
            cmbPreset.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbPreset.FormattingEnabled = true;
            cmbPreset.Location = new Point(75, 149);
            cmbPreset.Margin = new Padding(2);
            cmbPreset.Name = "cmbPreset";
            cmbPreset.Size = new Size(117, 25);
            cmbPreset.TabIndex = 6;
            cmbPreset.SelectedIndexChanged += cmbPreset_SelectedIndexChanged;
            // 
            // btnStart
            // 
            btnStart.Location = new Point(8, 502);
            btnStart.Margin = new Padding(2);
            btnStart.Name = "btnStart";
            btnStart.Size = new Size(62, 26);
            btnStart.TabIndex = 7;
            btnStart.Text = "开始转换";
            btnStart.UseVisualStyleBackColor = true;
            btnStart.Click += btnStart_Click;
            // 
            // progressBar1
            // 
            progressBar1.Location = new Point(76, 502);
            progressBar1.Margin = new Padding(2);
            progressBar1.Name = "progressBar1";
            progressBar1.Size = new Size(825, 26);
            progressBar1.Style = ProgressBarStyle.Marquee;
            progressBar1.TabIndex = 8;
            progressBar1.Click += progressBar1_Click;
            // 
            // rtbLog
            // 
            rtbLog.Location = new Point(75, 538);
            rtbLog.Margin = new Padding(2);
            rtbLog.Name = "rtbLog";
            rtbLog.Size = new Size(826, 170);
            rtbLog.TabIndex = 9;
            rtbLog.Text = "";
            // 
            // cmbEncoder
            // 
            cmbEncoder.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbEncoder.FormattingEnabled = true;
            cmbEncoder.Location = new Point(75, 200);
            cmbEncoder.Margin = new Padding(2);
            cmbEncoder.Name = "cmbEncoder";
            cmbEncoder.Size = new Size(117, 25);
            cmbEncoder.TabIndex = 10;
            // 
            // numJobs
            // 
            numJobs.Location = new Point(76, 251);
            numJobs.Margin = new Padding(2);
            numJobs.Name = "numJobs";
            numJobs.Size = new Size(115, 23);
            numJobs.TabIndex = 11;
            // 
            // chkSearch
            // 
            chkSearch.AutoSize = true;
            chkSearch.Checked = true;
            chkSearch.CheckState = CheckState.Checked;
            chkSearch.Location = new Point(689, 94);
            chkSearch.Margin = new Padding(2);
            chkSearch.Name = "chkSearch";
            chkSearch.Size = new Size(171, 21);
            chkSearch.TabIndex = 12;
            chkSearch.Text = "勾选启用搜索（默认勾选）";
            chkSearch.UseVisualStyleBackColor = true;
            // 
            // cmbMetric
            // 
            cmbMetric.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbMetric.FormattingEnabled = true;
            cmbMetric.Location = new Point(76, 307);
            cmbMetric.Margin = new Padding(2);
            cmbMetric.Name = "cmbMetric";
            cmbMetric.Size = new Size(276, 25);
            cmbMetric.TabIndex = 13;
            // 
            // txtTemplate
            // 
            txtTemplate.Location = new Point(75, 100);
            txtTemplate.Margin = new Padding(2);
            txtTemplate.Name = "txtTemplate";
            txtTemplate.Size = new Size(183, 23);
            txtTemplate.TabIndex = 14;
            txtTemplate.Text = "covers-{index}.avif";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(75, 81);
            label1.Margin = new Padding(2, 0, 2, 0);
            label1.Name = "label1";
            label1.Size = new Size(275, 17);
            label1.TabIndex = 15;
            label1.Text = "输出文件名模板，支持 {name}、{index} 等占位符";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(75, 130);
            label2.Margin = new Padding(2, 0, 2, 0);
            label2.Name = "label2";
            label2.Size = new Size(310, 17);
            label2.TabIndex = 16;
            label2.Text = "预设快速配置：影响 CRF、搜索开关、像素格式等默认值";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(75, 181);
            label3.Margin = new Padding(2, 0, 2, 0);
            label3.Name = "label3";
            label3.Size = new Size(278, 17);
            label3.TabIndex = 17;
            label3.Text = "AV1 编码器选择，例如 libaom-av1、libsvtav1 等";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(75, 232);
            label4.Margin = new Padding(2, 0, 2, 0);
            label4.Name = "label4";
            label4.Size = new Size(219, 17);
            label4.TabIndex = 18;
            label4.Text = "并行任务数，0 表示自动根据 CPU 计算";
            // 
            // chkRecursive
            // 
            chkRecursive.AutoSize = true;
            chkRecursive.Location = new Point(689, 142);
            chkRecursive.Margin = new Padding(2);
            chkRecursive.Name = "chkRecursive";
            chkRecursive.Size = new Size(111, 21);
            chkRecursive.TabIndex = 19;
            chkRecursive.Text = "递归处理子目录";
            chkRecursive.UseVisualStyleBackColor = true;
            // 
            // cmbQualityMode
            // 
            cmbQualityMode.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbQualityMode.FormattingEnabled = true;
            cmbQualityMode.Location = new Point(76, 361);
            cmbQualityMode.Margin = new Padding(2);
            cmbQualityMode.Name = "cmbQualityMode";
            cmbQualityMode.Size = new Size(117, 25);
            cmbQualityMode.TabIndex = 20;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(75, 288);
            label5.Margin = new Padding(2, 0, 2, 0);
            label5.Name = "label5";
            label5.Size = new Size(366, 17);
            label5.TabIndex = 21;
            label5.Text = "质量评价度量：vmaf、xpsnr、ssim、ms-ssim、mix混合评分等等";
            label5.Click += label5_Click;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(76, 341);
            label6.Margin = new Padding(2, 0, 2, 0);
            label6.Name = "label6";
            label6.Size = new Size(366, 17);
            label6.TabIndex = 22;
            label6.Text = "质量目标类型：VMAF、XPSNR、SSIM、PSNR-Y、MS-SSIM等等";
            // 
            // numQualityValue
            // 
            numQualityValue.DecimalPlaces = 1;
            numQualityValue.Location = new Point(285, 361);
            numQualityValue.Margin = new Padding(2);
            numQualityValue.Name = "numQualityValue";
            numQualityValue.Size = new Size(115, 23);
            numQualityValue.TabIndex = 23;
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(206, 362);
            label7.Margin = new Padding(2, 0, 2, 0);
            label7.Name = "label7";
            label7.Size = new Size(80, 17);
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
            grpCrfMode.Location = new Point(457, 81);
            grpCrfMode.Margin = new Padding(2);
            grpCrfMode.Name = "grpCrfMode";
            grpCrfMode.Padding = new Padding(2);
            grpCrfMode.Size = new Size(200, 169);
            grpCrfMode.TabIndex = 25;
            grpCrfMode.TabStop = false;
            grpCrfMode.Text = "CRF 模式";
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.Location = new Point(10, 121);
            label9.Margin = new Padding(2, 0, 2, 0);
            label9.Name = "label9";
            label9.Size = new Size(56, 17);
            label9.TabIndex = 6;
            label9.Text = "范围上限";
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.Location = new Point(10, 74);
            label8.Margin = new Padding(2, 0, 2, 0);
            label8.Name = "label8";
            label8.Size = new Size(56, 17);
            label8.TabIndex = 5;
            label8.Text = "范围下限";
            label8.Click += label8_Click;
            // 
            // numCrfMax
            // 
            numCrfMax.Location = new Point(10, 140);
            numCrfMax.Margin = new Padding(2);
            numCrfMax.Maximum = new decimal(new int[] { 63, 0, 0, 0 });
            numCrfMax.Name = "numCrfMax";
            numCrfMax.Size = new Size(115, 23);
            numCrfMax.TabIndex = 4;
            numCrfMax.Value = new decimal(new int[] { 63, 0, 0, 0 });
            // 
            // numCrfMin
            // 
            numCrfMin.Location = new Point(10, 96);
            numCrfMin.Margin = new Padding(2);
            numCrfMin.Maximum = new decimal(new int[] { 63, 0, 0, 0 });
            numCrfMin.Name = "numCrfMin";
            numCrfMin.Size = new Size(115, 23);
            numCrfMin.TabIndex = 3;
            // 
            // rbCrfRange
            // 
            rbCrfRange.AutoSize = true;
            rbCrfRange.Checked = true;
            rbCrfRange.Location = new Point(10, 50);
            rbCrfRange.Margin = new Padding(2);
            rbCrfRange.Name = "rbCrfRange";
            rbCrfRange.Size = new Size(74, 21);
            rbCrfRange.TabIndex = 2;
            rbCrfRange.TabStop = true;
            rbCrfRange.Text = "范围搜索";
            rbCrfRange.UseVisualStyleBackColor = true;
            // 
            // numCrfFix
            // 
            numCrfFix.Location = new Point(78, 21);
            numCrfFix.Margin = new Padding(2);
            numCrfFix.Maximum = new decimal(new int[] { 63, 0, 0, 0 });
            numCrfFix.Name = "numCrfFix";
            numCrfFix.Size = new Size(115, 23);
            numCrfFix.TabIndex = 1;
            numCrfFix.Value = new decimal(new int[] { 30, 0, 0, 0 });
            // 
            // rbCrfFix
            // 
            rbCrfFix.AutoSize = true;
            rbCrfFix.Location = new Point(10, 21);
            rbCrfFix.Margin = new Padding(2);
            rbCrfFix.Name = "rbCrfFix";
            rbCrfFix.Size = new Size(76, 21);
            rbCrfFix.TabIndex = 0;
            rbCrfFix.Text = "固定 CRF";
            rbCrfFix.UseVisualStyleBackColor = true;
            // 
            // cmbChroma
            // 
            cmbChroma.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbChroma.FormattingEnabled = true;
            cmbChroma.Location = new Point(457, 275);
            cmbChroma.Margin = new Padding(2);
            cmbChroma.Name = "cmbChroma";
            cmbChroma.Size = new Size(117, 25);
            cmbChroma.TabIndex = 26;
            // 
            // label10
            // 
            label10.AutoSize = true;
            label10.Location = new Point(457, 256);
            label10.Margin = new Padding(2, 0, 2, 0);
            label10.Name = "label10";
            label10.Size = new Size(56, 17);
            label10.TabIndex = 27;
            label10.Text = "色度采样";
            // 
            // cmbBitDepth
            // 
            cmbBitDepth.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbBitDepth.FormattingEnabled = true;
            cmbBitDepth.Location = new Point(457, 319);
            cmbBitDepth.Margin = new Padding(2);
            cmbBitDepth.Name = "cmbBitDepth";
            cmbBitDepth.Size = new Size(117, 25);
            cmbBitDepth.TabIndex = 28;
            // 
            // label11
            // 
            label11.AutoSize = true;
            label11.Location = new Point(457, 300);
            label11.Margin = new Padding(2, 0, 2, 0);
            label11.Name = "label11";
            label11.Size = new Size(56, 17);
            label11.TabIndex = 29;
            label11.Text = "输出位深";
            label11.Click += label11_Click;
            // 
            // chkLossless
            // 
            chkLossless.AutoSize = true;
            chkLossless.Location = new Point(689, 118);
            chkLossless.Margin = new Padding(2);
            chkLossless.Name = "chkLossless";
            chkLossless.Size = new Size(178, 21);
            chkLossless.TabIndex = 30;
            chkLossless.Text = "无损模式(有bug不建议使用)";
            chkLossless.UseVisualStyleBackColor = true;
            // 
            // numMaxRes
            // 
            numMaxRes.Location = new Point(75, 412);
            numMaxRes.Margin = new Padding(2);
            numMaxRes.Maximum = new decimal(new int[] { 8192, 0, 0, 0 });
            numMaxRes.Name = "numMaxRes";
            numMaxRes.Size = new Size(115, 23);
            numMaxRes.TabIndex = 31;
            // 
            // label12
            // 
            label12.AutoSize = true;
            label12.Location = new Point(75, 392);
            label12.Margin = new Padding(2, 0, 2, 0);
            label12.Name = "label12";
            label12.Size = new Size(223, 17);
            label12.TabIndex = 32;
            label12.Text = "最大分辨率限制（长边），0 表示不缩放";
            // 
            // chkOutputFullRes
            // 
            chkOutputFullRes.AutoSize = true;
            chkOutputFullRes.Location = new Point(689, 167);
            chkOutputFullRes.Margin = new Padding(2);
            chkOutputFullRes.Name = "chkOutputFullRes";
            chkOutputFullRes.Size = new Size(159, 21);
            chkOutputFullRes.TabIndex = 33;
            chkOutputFullRes.Text = "最终输出保留原始分辨率";
            chkOutputFullRes.UseVisualStyleBackColor = true;
            // 
            // chkSerialEncode
            // 
            chkSerialEncode.AutoSize = true;
            chkSerialEncode.Location = new Point(689, 191);
            chkSerialEncode.Margin = new Padding(2);
            chkSerialEncode.Name = "chkSerialEncode";
            chkSerialEncode.Size = new Size(135, 21);
            chkSerialEncode.TabIndex = 34;
            chkSerialEncode.Text = "极限压缩（单线程）";
            chkSerialEncode.UseVisualStyleBackColor = true;
            // 
            // chkPriorSearch
            // 
            chkPriorSearch.AutoSize = true;
            chkPriorSearch.Location = new Point(689, 215);
            chkPriorSearch.Margin = new Padding(2);
            chkPriorSearch.Name = "chkPriorSearch";
            chkPriorSearch.Size = new Size(123, 21);
            chkPriorSearch.TabIndex = 35;
            chkPriorSearch.Text = "先验搜索（更快）";
            chkPriorSearch.UseVisualStyleBackColor = true;
            // 
            // chkProxy
            // 
            chkProxy.AutoSize = true;
            chkProxy.Location = new Point(689, 239);
            chkProxy.Margin = new Padding(2);
            chkProxy.Name = "chkProxy";
            chkProxy.Size = new Size(159, 21);
            chkProxy.TabIndex = 36;
            chkProxy.Text = "代理搜索（需先验搜索）";
            chkProxy.UseVisualStyleBackColor = true;
            // 
            // chkDryRun
            // 
            chkDryRun.AutoSize = true;
            chkDryRun.Location = new Point(689, 264);
            chkDryRun.Margin = new Padding(2);
            chkDryRun.Name = "chkDryRun";
            chkDryRun.Size = new Size(147, 21);
            chkDryRun.TabIndex = 37;
            chkDryRun.Text = "仅模拟运行（不编码）";
            chkDryRun.UseVisualStyleBackColor = true;
            // 
            // chkVerbose
            // 
            chkVerbose.AutoSize = true;
            chkVerbose.Location = new Point(689, 285);
            chkVerbose.Margin = new Padding(2);
            chkVerbose.Name = "chkVerbose";
            chkVerbose.Size = new Size(75, 21);
            chkVerbose.TabIndex = 38;
            chkVerbose.Text = "详细日志";
            chkVerbose.UseVisualStyleBackColor = true;
            // 
            // cmbConflict
            // 
            cmbConflict.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbConflict.FormattingEnabled = true;
            cmbConflict.Location = new Point(457, 363);
            cmbConflict.Margin = new Padding(2);
            cmbConflict.Name = "cmbConflict";
            cmbConflict.Size = new Size(117, 25);
            cmbConflict.TabIndex = 39;
            // 
            // label13
            // 
            label13.AutoSize = true;
            label13.Location = new Point(457, 344);
            label13.Margin = new Padding(2, 0, 2, 0);
            label13.Name = "label13";
            label13.Size = new Size(92, 17);
            label13.TabIndex = 40;
            label13.Text = "当文件已存在时";
            label13.Click += label13_Click;
            // 
            // label14
            // 
            label14.AutoSize = true;
            label14.Location = new Point(457, 392);
            label14.Margin = new Padding(2, 0, 2, 0);
            label14.Name = "label14";
            label14.Size = new Size(80, 17);
            label14.TabIndex = 41;
            label14.Text = "设置搜索速度";
            // 
            // numSearchCpuUsed
            // 
            numSearchCpuUsed.Location = new Point(457, 412);
            numSearchCpuUsed.Margin = new Padding(2);
            numSearchCpuUsed.Name = "numSearchCpuUsed";
            numSearchCpuUsed.Size = new Size(115, 23);
            numSearchCpuUsed.TabIndex = 42;
            numSearchCpuUsed.ValueChanged += numSearchCpuUsed_ValueChanged;
            // 
            // numFinalCpuUsed
            // 
            numFinalCpuUsed.Location = new Point(457, 458);
            numFinalCpuUsed.Margin = new Padding(2);
            numFinalCpuUsed.Name = "numFinalCpuUsed";
            numFinalCpuUsed.Size = new Size(115, 23);
            numFinalCpuUsed.TabIndex = 43;
            // 
            // label15
            // 
            label15.AutoSize = true;
            label15.Location = new Point(457, 439);
            label15.Margin = new Padding(2, 0, 2, 0);
            label15.Name = "label15";
            label15.Size = new Size(104, 17);
            label15.TabIndex = 44;
            label15.Text = "设置最终编码速度";
            // 
            // chkSweep
            // 
            chkSweep.AutoSize = true;
            chkSweep.Location = new Point(689, 307);
            chkSweep.Name = "chkSweep";
            chkSweep.Size = new Size(134, 21);
            chkSweep.TabIndex = 45;
            chkSweep.Text = "遍历模式 (--sweep)";
            chkSweep.UseVisualStyleBackColor = true;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1005, 718);
            Controls.Add(chkSweep);
            Controls.Add(label15);
            Controls.Add(numFinalCpuUsed);
            Controls.Add(numSearchCpuUsed);
            Controls.Add(label14);
            Controls.Add(label13);
            Controls.Add(cmbConflict);
            Controls.Add(chkVerbose);
            Controls.Add(chkDryRun);
            Controls.Add(chkProxy);
            Controls.Add(chkPriorSearch);
            Controls.Add(chkSerialEncode);
            Controls.Add(chkOutputFullRes);
            Controls.Add(label12);
            Controls.Add(numMaxRes);
            Controls.Add(chkLossless);
            Controls.Add(label11);
            Controls.Add(cmbBitDepth);
            Controls.Add(cmbChroma);
            Controls.Add(label10);
            Controls.Add(grpCrfMode);
            Controls.Add(label7);
            Controls.Add(numQualityValue);
            Controls.Add(label6);
            Controls.Add(label5);
            Controls.Add(cmbQualityMode);
            Controls.Add(chkRecursive);
            Controls.Add(label4);
            Controls.Add(label3);
            Controls.Add(label2);
            Controls.Add(label1);
            Controls.Add(txtTemplate);
            Controls.Add(cmbMetric);
            Controls.Add(chkSearch);
            Controls.Add(numJobs);
            Controls.Add(cmbEncoder);
            Controls.Add(rtbLog);
            Controls.Add(progressBar1);
            Controls.Add(btnStart);
            Controls.Add(cmbPreset);
            Controls.Add(btnBrowseOutput);
            Controls.Add(txtOutput);
            Controls.Add(lblOutput);
            Controls.Add(btnBrowseInput);
            Controls.Add(txtInput);
            Controls.Add(lblInput);
            Margin = new Padding(2);
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
        private TextBox txtInput;
        private Button btnBrowseInput;
        private Label lblOutput;
        private TextBox txtOutput;
        private Button btnBrowseOutput;
        private ComboBox cmbPreset;
        private Button btnStart;
        private ProgressBar progressBar1;
        private RichTextBox rtbLog;
        private ComboBox cmbEncoder;
        private NumericUpDown numJobs;
        private CheckBox chkSearch;
        private ComboBox cmbMetric;
        private TextBox txtTemplate;
        private Label label1;
        private Label label2;
        private Label label3;
        private Label label4;
        private CheckBox chkRecursive;
        private ComboBox cmbQualityMode;
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
        private ComboBox cmbChroma;
        private Label label10;
        private ComboBox cmbBitDepth;
        private Label label11;
        private CheckBox chkLossless;
        private NumericUpDown numMaxRes;
        private Label label12;
        private CheckBox chkOutputFullRes;
        private CheckBox chkSerialEncode;
        private CheckBox chkPriorSearch;
        private CheckBox chkProxy;
        private CheckBox chkDryRun;
        private CheckBox chkVerbose;
        private ComboBox cmbConflict;
        private Label label13;
        private Label label14;
        private NumericUpDown numSearchCpuUsed;
        private NumericUpDown numFinalCpuUsed;
        private Label label15;
        private CheckBox chkSweep;
    }
}
