namespace AvifEncoder.GuiLakeUl.选项窗口
{
    partial class FormHelp
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
            txtHelp = new LakeUI.ModernTextBox();
            modernPanel1.SuspendLayout();
            SuspendLayout();
            // 
            // modernPanel1
            // 
            modernPanel1.BackColor = Color.Transparent;
            modernPanel1.BackColor1 = Color.Transparent;
            modernPanel1.BorderColor = Color.Transparent;
            modernPanel1.Controls.Add(txtHelp);
            modernPanel1.Dock = DockStyle.Fill;
            modernPanel1.Location = new Point(0, 0);
            modernPanel1.Name = "modernPanel1";
            modernPanel1.Size = new Size(1114, 681);
            modernPanel1.TabIndex = 0;
            // 
            // txtHelp
            // 
            txtHelp.AllowDrop = true;
            txtHelp.BackColor1 = Color.Transparent;
            txtHelp.BackgroundSource = modernPanel1;
            txtHelp.BorderColor = Color.Transparent;
            txtHelp.BorderColorFocus = Color.Transparent;
            txtHelp.Dock = DockStyle.Fill;
            txtHelp.Font = new Font("Cascadia Code", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            txtHelp.ForeColor = Color.WhiteSmoke;
            txtHelp.Location = new Point(1, 1);
            txtHelp.Margin = new Padding(2);
            txtHelp.MultiLine = true;
            txtHelp.Name = "txtHelp";
            txtHelp.ReadOnly = true;
            txtHelp.SelectionColor = Color.Transparent;
            txtHelp.Size = new Size(1111, 678);
            txtHelp.TabIndex = 0;
            txtHelp.Text = "modernTextBox1";
            // 
            // FormHelp
            // 
            AllowDrop = true;
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1114, 681);
            Controls.Add(modernPanel1);
            DoubleBuffered = true;
            Name = "FormHelp";
            Text = "FormHelp";
            Load += FormHelp_Load;
            modernPanel1.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion
        private LakeUI.ModernTextBox txtHelp;
        public LakeUI.ModernPanel modernPanel1;
    }
}