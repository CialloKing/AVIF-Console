namespace AvifEncoder.GuiLakeUI.选项窗口
{
    partial class FormCommands
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
            btnResetEncoderParams = new LakeUI.ModernButton();
            btnCopyFfmpegCommand = new LakeUI.ModernButton();
            btnResetExtensions = new LakeUI.ModernButton();
            btnAnimatedCommand = new LakeUI.ModernButton();
            modernPanel1 = new LakeUI.ModernPanel();
            txtExtensions = new LakeUI.ModernTextBox();
            txtEncoderParams = new LakeUI.ModernTextBox();
            txtParamsPreview = new LakeUI.ModernTextBox();
            txtAnimatedCommand = new LakeUI.ModernTextBox();
            modernPanel1.SuspendLayout();
            SuspendLayout();
            // 
            // btnResetEncoderParams
            // 
            btnResetEncoderParams.AnimationFPS = 0;
            btnResetEncoderParams.BackColor1 = Color.Transparent;
            btnResetEncoderParams.BorderColor = Color.Gainsboro;
            btnResetEncoderParams.BorderRadius = 10;
            btnResetEncoderParams.ForeColor = Color.WhiteSmoke;
            btnResetEncoderParams.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            btnResetEncoderParams.Location = new Point(16, 114);
            btnResetEncoderParams.Margin = new Padding(2);
            btnResetEncoderParams.Name = "btnResetEncoderParams";
            btnResetEncoderParams.PressedBackColor1 = Color.White;
            btnResetEncoderParams.Size = new Size(282, 40);
            btnResetEncoderParams.TabIndex = 69;
            btnResetEncoderParams.Text = "自定义ffmpeg命令行高级参数，可直接编辑";
            btnResetEncoderParams.TextAlign = LakeUI.ModernButton.TextAlignEnum.Left;
            // 
            // btnCopyFfmpegCommand
            // 
            btnCopyFfmpegCommand.AnimationFPS = 0;
            btnCopyFfmpegCommand.BackColor1 = Color.Transparent;
            btnCopyFfmpegCommand.BorderColor = Color.Gainsboro;
            btnCopyFfmpegCommand.BorderRadius = 10;
            btnCopyFfmpegCommand.ForeColor = Color.WhiteSmoke;
            btnCopyFfmpegCommand.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            btnCopyFfmpegCommand.Location = new Point(16, 201);
            btnCopyFfmpegCommand.Margin = new Padding(2);
            btnCopyFfmpegCommand.Name = "btnCopyFfmpegCommand";
            btnCopyFfmpegCommand.PressedBackColor1 = Color.White;
            btnCopyFfmpegCommand.Size = new Size(282, 40);
            btnCopyFfmpegCommand.TabIndex = 70;
            btnCopyFfmpegCommand.Text = "实际使用ffmpeg完整命令只读展示";
            btnCopyFfmpegCommand.TextAlign = LakeUI.ModernButton.TextAlignEnum.Left;
            // 
            // btnResetExtensions
            // 
            btnResetExtensions.AnimationFPS = 0;
            btnResetExtensions.BackColor1 = Color.Transparent;
            btnResetExtensions.BorderColor = Color.Gainsboro;
            btnResetExtensions.BorderRadius = 10;
            btnResetExtensions.ForeColor = Color.WhiteSmoke;
            btnResetExtensions.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            btnResetExtensions.Location = new Point(16, 23);
            btnResetExtensions.Margin = new Padding(2);
            btnResetExtensions.Name = "btnResetExtensions";
            btnResetExtensions.PressedBackColor1 = Color.White;
            btnResetExtensions.Size = new Size(611, 40);
            btnResetExtensions.TabIndex = 71;
            btnResetExtensions.Text = "图片后缀名，使用英文逗号分隔，默认为.jpg,.jpeg,.png,.webp,.gif这5种，可按需添加";
            btnResetExtensions.TextAlign = LakeUI.ModernButton.TextAlignEnum.Left;
            // 
            // btnAnimatedCommand
            // 
            btnAnimatedCommand.AnimationFPS = 0;
            btnAnimatedCommand.BackColor1 = Color.Transparent;
            btnAnimatedCommand.BorderColor = Color.Gainsboro;
            btnAnimatedCommand.BorderRadius = 10;
            btnAnimatedCommand.ForeColor = Color.WhiteSmoke;
            btnAnimatedCommand.HoverBackColor1 = Color.FromArgb(128, 255, 255, 255);
            btnAnimatedCommand.Location = new Point(16, 299);
            btnAnimatedCommand.Margin = new Padding(2);
            btnAnimatedCommand.Name = "btnAnimatedCommand";
            btnAnimatedCommand.PressedBackColor1 = Color.White;
            btnAnimatedCommand.Size = new Size(282, 40);
            btnAnimatedCommand.TabIndex = 79;
            btnAnimatedCommand.Text = "动图编码完整命令，可直接编辑";
            btnAnimatedCommand.TextAlign = LakeUI.ModernButton.TextAlignEnum.Left;
            // 
            // modernPanel1
            // 
            modernPanel1.BackColor = Color.Transparent;
            modernPanel1.BackColor1 = Color.Transparent;
            modernPanel1.BorderColor = Color.Transparent;
            modernPanel1.Controls.Add(txtAnimatedCommand);
            modernPanel1.Controls.Add(txtParamsPreview);
            modernPanel1.Controls.Add(txtEncoderParams);
            modernPanel1.Controls.Add(txtExtensions);
            modernPanel1.Controls.Add(btnAnimatedCommand);
            modernPanel1.Controls.Add(btnResetExtensions);
            modernPanel1.Controls.Add(btnCopyFfmpegCommand);
            modernPanel1.Controls.Add(btnResetEncoderParams);
            modernPanel1.Dock = DockStyle.Fill;
            modernPanel1.ForeColor = Color.Transparent;
            modernPanel1.Location = new Point(0, 0);
            modernPanel1.Name = "modernPanel1";
            modernPanel1.Size = new Size(1022, 770);
            modernPanel1.TabIndex = 1;
            // 
            // txtExtensions
            // 
            txtExtensions.AllowDrop = true;
            txtExtensions.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtExtensions.BackColor1 = Color.Transparent;
            txtExtensions.BorderColorFocus = Color.White;
            txtExtensions.ForeColor = Color.WhiteSmoke;
            txtExtensions.Location = new Point(16, 67);
            txtExtensions.Margin = new Padding(2);
            txtExtensions.Name = "txtExtensions";
            txtExtensions.SelectionColor = Color.FromArgb(180, 128, 128, 128);
            txtExtensions.Size = new Size(923, 32);
            txtExtensions.TabIndex = 80;
            // 
            // txtEncoderParams
            // 
            txtEncoderParams.AllowDrop = true;
            txtEncoderParams.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtEncoderParams.BackColor1 = Color.Transparent;
            txtEncoderParams.BorderColorFocus = Color.White;
            txtEncoderParams.ForeColor = Color.WhiteSmoke;
            txtEncoderParams.Location = new Point(16, 158);
            txtEncoderParams.Margin = new Padding(2);
            txtEncoderParams.Name = "txtEncoderParams";
            txtEncoderParams.SelectionColor = Color.FromArgb(180, 128, 128, 128);
            txtEncoderParams.Size = new Size(923, 32);
            txtEncoderParams.TabIndex = 81;
            // 
            // txtParamsPreview
            // 
            txtParamsPreview.AllowDrop = true;
            txtParamsPreview.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtParamsPreview.AnimationFPS = 0;
            txtParamsPreview.BackColor1 = Color.Transparent;
            txtParamsPreview.BorderColor = Color.White;
            txtParamsPreview.BorderColorFocus = Color.White;
            txtParamsPreview.ForeColor = Color.WhiteSmoke;
            txtParamsPreview.Location = new Point(16, 245);
            txtParamsPreview.Margin = new Padding(2);
            txtParamsPreview.Name = "txtParamsPreview";
            txtParamsPreview.ReadOnly = true;
            txtParamsPreview.SelectionColor = Color.FromArgb(180, 128, 128, 128);
            txtParamsPreview.Size = new Size(923, 32);
            txtParamsPreview.TabIndex = 82;
            // 
            // txtAnimatedCommand
            // 
            txtAnimatedCommand.AllowDrop = true;
            txtAnimatedCommand.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtAnimatedCommand.AnimationFPS = 0;
            txtAnimatedCommand.BackColor1 = Color.Transparent;
            txtAnimatedCommand.BorderColorFocus = Color.White;
            txtAnimatedCommand.ForeColor = Color.WhiteSmoke;
            txtAnimatedCommand.Location = new Point(16, 343);
            txtAnimatedCommand.Margin = new Padding(2);
            txtAnimatedCommand.Name = "txtAnimatedCommand";
            txtAnimatedCommand.SelectionColor = Color.FromArgb(180, 128, 128, 128);
            txtAnimatedCommand.Size = new Size(923, 32);
            txtAnimatedCommand.TabIndex = 83;
            // 
            // FormCommands
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.Black;
            ClientSize = new Size(1022, 770);
            Controls.Add(modernPanel1);
            Name = "FormCommands";
            Text = "FormCommands";
            modernPanel1.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion
        internal LakeUI.ModernButton btnResetEncoderParams;
        internal LakeUI.ModernButton btnCopyFfmpegCommand;
        internal LakeUI.ModernButton btnResetExtensions;
        internal LakeUI.ModernButton btnAnimatedCommand;
        public LakeUI.ModernPanel modernPanel1;
        internal LakeUI.ModernTextBox txtExtensions;
        internal LakeUI.ModernTextBox txtEncoderParams;
        internal LakeUI.ModernTextBox txtParamsPreview;
        internal LakeUI.ModernTextBox txtAnimatedCommand;
    }
}