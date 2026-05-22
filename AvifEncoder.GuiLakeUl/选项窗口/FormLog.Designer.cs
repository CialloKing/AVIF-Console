namespace AvifEncoder.GuiLakeUl.选项窗口
{
    partial class FormLog
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
            txtLog = new LakeUI.ModernTextBox();
            modernPanel1.SuspendLayout();
            SuspendLayout();
            // 
            // modernPanel1
            // 
            modernPanel1.Controls.Add(txtLog);
            modernPanel1.Dock = DockStyle.Fill;
            modernPanel1.Location = new Point(0, 0);
            modernPanel1.Name = "modernPanel1";
            modernPanel1.OverlayColor = Color.FromArgb(36, 36, 36);
            modernPanel1.Size = new Size(800, 450);
            modernPanel1.TabIndex = 0;
            modernPanel1.Scroll += modernPanel1_Scroll;
            // 
            // txtLog
            // 
            txtLog.Dock = DockStyle.Bottom;
            txtLog.ForeColor = Color.White;
            txtLog.Location = new Point(1, 173);
            txtLog.Margin = new Padding(2);
            txtLog.MaxUndoCount = 0;
            txtLog.MultiLine = true;
            txtLog.Name = "txtLog";
            txtLog.PreserveScrollPosition = true;
            txtLog.ReadOnly = true;
            txtLog.Size = new Size(797, 275);
            txtLog.TabIndex = 0;
            txtLog.Text = "modernTextBox1";
            // 
            // FormLog
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(modernPanel1);
            Name = "FormLog";
            Text = "FormLog";
            modernPanel1.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion
        public LakeUI.ModernPanel modernPanel1;
        public LakeUI.ModernTextBox txtLog;
    }
}