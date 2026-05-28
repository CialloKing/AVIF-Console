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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            modernTabListControl1 = new LakeUI.ModernTabListControl();
            thisIsYourWindow1 = new LakeUI.ThisIsYourWindow(components);
            SuspendLayout();
            // 
            // modernTabListControl1
            // 
            modernTabListControl1.AllowDrop = true;
            modernTabListControl1.BackColor = Color.Transparent;
            modernTabListControl1.ContentBackColor = Color.Transparent;
            modernTabListControl1.ContentBorderColor = Color.Transparent;
            modernTabListControl1.Dock = DockStyle.Fill;
            modernTabListControl1.ForeColor = Color.Transparent;
            modernTabListControl1.Location = new Point(0, 0);
            modernTabListControl1.Name = "modernTabListControl1";
            modernTabListControl1.ScrollBarTrackColor = Color.Transparent;
            modernTabListControl1.Size = new Size(1264, 681);
            modernTabListControl1.TabIndex = 0;
            modernTabListControl1.TabItemHoverBackColor = Color.FromArgb(128, 64, 64, 64);
            modernTabListControl1.TabItemSelectedBackColor = Color.FromArgb(128, 80, 80, 80);
            modernTabListControl1.TabStripBackColor = Color.Transparent;
            modernTabListControl1.SelectedIndexChanged += modernTabListControl1_SelectedIndexChanged;
            // 
            // thisIsYourWindow1
            // 
            thisIsYourWindow1.BackdropBlurPasses = 2;
            thisIsYourWindow1.BackdropBlurRadius = 5;
            thisIsYourWindow1.BackdropImage = (Image)resources.GetObject("thisIsYourWindow1.BackdropImage");
            thisIsYourWindow1.BackdropMode = LakeUI.ThisIsYourWindow.BackdropModeEnum.Image;
            thisIsYourWindow1.BackdropTintColor = Color.FromArgb(80, 32, 32, 32);
            thisIsYourWindow1.BackdropTintInactiveColor = Color.FromArgb(80, 32, 32, 32);
            thisIsYourWindow1.BorderInactiveColor = Color.Transparent;
            thisIsYourWindow1.CaptionButtonGlyphColor = Color.FromArgb(200, 200, 200);
            thisIsYourWindow1.CaptionHeight = 40;
            thisIsYourWindow1.CaptionInactiveBackColor = Color.Transparent;
            thisIsYourWindow1.CloseButtonGlyphColor = Color.FromArgb(200, 200, 200);
            // 
            // Form1
            // 
            AllowDrop = true;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1264, 681);
            Controls.Add(modernTabListControl1);
            DoubleBuffered = true;
            Font = new Font("Microsoft YaHei UI", 9F);
            ForeColor = Color.Cyan;
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "Form1";
            Text = "AvifEncoder.GuiLakeUl-net10.0";
            Load += Form1_Load;
            ResumeLayout(false);
        }

        #endregion

        private LakeUI.ModernTabListControl modernTabListControl1;
        private LakeUI.ThisIsYourWindow thisIsYourWindow1;
    }
}