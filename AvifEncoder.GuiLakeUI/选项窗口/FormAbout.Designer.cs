namespace AvifEncoder.GuiLakeUI.选项窗口
{
    partial class FormAbout
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
            txtAbout = new LakeUI.ModernTextBox();
            modernPanel1.SuspendLayout();
            SuspendLayout();
            // 
            // modernPanel1
            // 
            modernPanel1.BackColor = Color.Transparent;
            modernPanel1.BackColor1 = Color.Transparent;
            modernPanel1.BorderColor = Color.Transparent;
            modernPanel1.Controls.Add(txtAbout);
            modernPanel1.Dock = DockStyle.Fill;
            modernPanel1.Location = new Point(0, 0);
            modernPanel1.Name = "modernPanel1";
            modernPanel1.Size = new Size(800, 450);
            modernPanel1.TabIndex = 0;
            // 
            // txtAbout
            // 
            txtAbout.AllowDrop = true;
            txtAbout.AnimationFPS = 0;
            txtAbout.BackColor1 = Color.Transparent;
            txtAbout.BackgroundSource = modernPanel1;
            txtAbout.BorderColor = Color.Transparent;
            txtAbout.BorderColorFocus = Color.Transparent;
            txtAbout.Dock = DockStyle.Fill;
            txtAbout.Font = new Font("Cascadia Code", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            txtAbout.ForeColor = Color.White;
            txtAbout.LinkColor = Color.FromArgb(83, 177, 255);
            txtAbout.LinkDetection = true;
            txtAbout.Location = new Point(1, 1);
            txtAbout.Margin = new Padding(2);
            txtAbout.MaxUndoCount = 0;
            txtAbout.MultiLine = true;
            txtAbout.Name = "txtAbout";
            txtAbout.ReadOnly = true;
            txtAbout.SelectionColor = Color.FromArgb(180, 128, 128, 128);
            txtAbout.Size = new Size(797, 447);
            txtAbout.TabIndex = 1;
            txtAbout.Text = "关于页";
            // 
            // FormAbout
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.Black;
            ClientSize = new Size(800, 450);
            Controls.Add(modernPanel1);
            Name = "FormAbout";
            Text = "FormAbout";
            modernPanel1.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        public LakeUI.ModernPanel modernPanel1;
        private LakeUI.ModernTextBox txtAbout;
    }
}