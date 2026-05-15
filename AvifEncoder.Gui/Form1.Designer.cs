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
            txtInput.Size = new Size(612, 30);
            txtInput.TabIndex = 1;
            // 
            // btnBrowseInput
            // 
            btnBrowseInput.Location = new Point(759, 20);
            btnBrowseInput.Name = "btnBrowseInput";
            btnBrowseInput.Size = new Size(112, 34);
            btnBrowseInput.TabIndex = 2;
            btnBrowseInput.Text = "浏览...";
            btnBrowseInput.UseVisualStyleBackColor = true;
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
            txtOutput.Size = new Size(612, 30);
            txtOutput.TabIndex = 4;
            // 
            // btnBrowseOutput
            // 
            btnBrowseOutput.Location = new Point(759, 70);
            btnBrowseOutput.Name = "btnBrowseOutput";
            btnBrowseOutput.Size = new Size(112, 34);
            btnBrowseOutput.TabIndex = 5;
            btnBrowseOutput.Text = "浏览...";
            btnBrowseOutput.UseVisualStyleBackColor = true;
            btnBrowseOutput.Click += btnBrowseOutput_Click;
            // 
            // cmbPreset
            // 
            cmbPreset.FormattingEnabled = true;
            cmbPreset.Location = new Point(118, 127);
            cmbPreset.Name = "cmbPreset";
            cmbPreset.Size = new Size(182, 32);
            cmbPreset.TabIndex = 6;
            // 
            // btnStart
            // 
            btnStart.Location = new Point(118, 209);
            btnStart.Name = "btnStart";
            btnStart.Size = new Size(112, 34);
            btnStart.TabIndex = 7;
            btnStart.Text = "开始转换";
            btnStart.UseVisualStyleBackColor = true;
            btnStart.Click += button1_Click;
            // 
            // progressBar1
            // 
            progressBar1.Location = new Point(118, 285);
            progressBar1.Name = "progressBar1";
            progressBar1.Size = new Size(612, 79);
            progressBar1.TabIndex = 8;
            progressBar1.Click += progressBar1_Click;
            // 
            // rtbLog
            // 
            rtbLog.Location = new Point(118, 404);
            rtbLog.Name = "rtbLog";
            rtbLog.Size = new Size(612, 136);
            rtbLog.TabIndex = 9;
            rtbLog.Text = "";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(11F, 24F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(922, 571);
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
            Name = "Form1";
            Text = "输出目录";
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
    }
}
