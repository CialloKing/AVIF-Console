namespace AvifEncoder.GuiLakeUl.选项窗口
{
    partial class FormUpdate
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.ProgressBar pbDownload;
        private System.Windows.Forms.Button btnDownload;
        private System.Windows.Forms.Button btnSkip;

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

        private void InitializeComponent()
        {
            lblStatus = new System.Windows.Forms.Label();
            pbDownload = new System.Windows.Forms.ProgressBar();
            btnDownload = new System.Windows.Forms.Button();
            btnSkip = new System.Windows.Forms.Button();
            SuspendLayout();
            // 
            // lblStatus
            // 
            lblStatus.Font = new System.Drawing.Font(
                "Microsoft YaHei UI", 10F,
                System.Drawing.FontStyle.Regular,
                System.Drawing.GraphicsUnit.Point,
                134);
            lblStatus.Location = new System.Drawing.Point(20, 20);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new System.Drawing.Size(420, 80);
            lblStatus.TabIndex = 0;
            lblStatus.Text = "正在检查更新...";
            lblStatus.TextAlign =
                System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // pbDownload
            // 
            pbDownload.Location = new System.Drawing.Point(20, 115);
            pbDownload.Name = "pbDownload";
            pbDownload.Size = new System.Drawing.Size(420, 28);
            pbDownload.Style =
                System.Windows.Forms.ProgressBarStyle.Continuous;
            pbDownload.TabIndex = 1;
            // 
            // btnDownload
            // 
            btnDownload.Font = new System.Drawing.Font(
                "Microsoft YaHei UI", 9F);
            btnDownload.Location = new System.Drawing.Point(230, 165);
            btnDownload.Name = "btnDownload";
            btnDownload.Size = new System.Drawing.Size(100, 35);
            btnDownload.TabIndex = 2;
            btnDownload.Text = "下载更新";
            btnDownload.UseVisualStyleBackColor = true;
            btnDownload.Click += btnDownload_Click;
            // 
            // btnSkip
            // 
            btnSkip.Font = new System.Drawing.Font(
                "Microsoft YaHei UI", 9F);
            btnSkip.Location = new System.Drawing.Point(340, 165);
            btnSkip.Name = "btnSkip";
            btnSkip.Size = new System.Drawing.Size(100, 35);
            btnSkip.TabIndex = 3;
            btnSkip.Text = "跳过";
            btnSkip.UseVisualStyleBackColor = true;
            btnSkip.Click += btnSkip_Click;
            // 
            // FormUpdate
            // 
            AutoScaleDimensions =
                new System.Drawing.SizeF(96F, 96F);
            AutoScaleMode =
                System.Windows.Forms.AutoScaleMode.Dpi;
            ClientSize = new System.Drawing.Size(460, 220);
            Controls.Add(btnSkip);
            Controls.Add(btnDownload);
            Controls.Add(pbDownload);
            Controls.Add(lblStatus);
            DoubleBuffered = true;
            Font = new System.Drawing.Font(
                "Microsoft YaHei UI", 9F,
                System.Drawing.FontStyle.Regular,
                System.Drawing.GraphicsUnit.Point,
                134);
            FormBorderStyle =
                System.Windows.Forms.FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "FormUpdate";
            StartPosition =
                System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "软件更新";
            Load += FormUpdate_Load;
            ResumeLayout(false);
        }

        #endregion
    }
}