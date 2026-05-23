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
            components = new System.ComponentModel.Container();
            modernTabListControl1 = new LakeUI.ModernTabListControl();
            thisIsYourWindow1 = new LakeUI.ThisIsYourWindow(components);
            SuspendLayout();
            // 
            // modernTabListControl1
            // 
            modernTabListControl1.Dock = DockStyle.Fill;
            modernTabListControl1.Location = new Point(0, 0);
            modernTabListControl1.Name = "modernTabListControl1";
            modernTabListControl1.Size = new Size(1109, 684);
            modernTabListControl1.TabIndex = 0;
            modernTabListControl1.SelectedIndexChanged += modernTabListControl1_SelectedIndexChanged;
            // 
            // thisIsYourWindow1
            // 
            thisIsYourWindow1.BackdropMaxParallelism = 16;
            thisIsYourWindow1.CaptionButtonGlyphColor = Color.FromArgb(200, 200, 200);
            thisIsYourWindow1.CloseButtonGlyphColor = Color.FromArgb(200, 200, 200);
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1109, 684);
            Controls.Add(modernTabListControl1);
            DoubleBuffered = true;
            ForeColor = SystemColors.ControlText;
            Name = "Form1";
            Text = "Form1";
            Load += Form1_Load;
            ResumeLayout(false);
        }

        #endregion

        private LakeUI.ModernTabListControl modernTabListControl1;
        private LakeUI.ThisIsYourWindow thisIsYourWindow1;
    }
}
