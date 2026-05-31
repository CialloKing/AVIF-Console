using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AvifEncoder;
using LakeUI;

namespace AvifEncoder.GuiLakeUI.选项窗口
{
    public partial class FormUpdate : Form
    {
        private readonly UpdateManager _manager = new();
        private ReleaseInfo? _release;
        private CancellationTokenSource? _cts;

        public FormUpdate(ReleaseInfo release)
        {
            InitializeComponent();
            _release = release;
        }

        [System.Diagnostics.CodeAnalysis.MemberNotNull(
            nameof(modernPanel1), nameof(lblStatus),
            nameof(pbDownload), nameof(btnDownload),
            nameof(btnSkip))]
        private void InitializeComponent()
        {
            modernPanel1 = new ModernPanel();
            lblStatus = new ModernTextBox();
            pbDownload = new ExcellentProgressBar();
            btnDownload = new ModernButton();
            btnSkip = new ModernButton();
            modernPanel1.SuspendLayout();
            SuspendLayout();

            // ---- modernPanel1 (容器) ----
            modernPanel1.BackColor = Color.Transparent;
            modernPanel1.BorderColor = Color.Transparent;
            modernPanel1.Dock = DockStyle.Fill;
            modernPanel1.Location = new Point(0, 0);
            modernPanel1.Name = "modernPanel1";
            modernPanel1.Size = new Size(460, 240);
            modernPanel1.TabIndex = 0;
            modernPanel1.Controls.Add(lblStatus);
            modernPanel1.Controls.Add(pbDownload);
            modernPanel1.Controls.Add(btnDownload);
            modernPanel1.Controls.Add(btnSkip);

            // ---- lblStatus (状态文字) ----
            lblStatus.AnimationFPS = 0;
            lblStatus.BackColor1 = Color.Transparent;
            lblStatus.BorderColor = Color.Transparent;
            lblStatus.BorderColorFocus = Color.Transparent;
            lblStatus.Font = new Font("Microsoft YaHei UI", 10F);
            lblStatus.ForeColor = Color.White;
            lblStatus.Location = new Point(20, 20);
            lblStatus.MultiLine = true;
            lblStatus.Name = "lblStatus";
            lblStatus.ReadOnly = true;
            lblStatus.SelectionColor = Color.Gray;
            lblStatus.Size = new Size(420, 110);
            lblStatus.TabIndex = 0;
            lblStatus.Text = "正在检查更新...";
            lblStatus.WordWrap = true;

            // ---- pbDownload (进度条) ----
            pbDownload.BackColor = Color.FromArgb(50, 50, 55);
            pbDownload.Location = new Point(20, 145);
            pbDownload.Maximum = 100;
            pbDownload.Name = "pbDownload";
            pbDownload.Size = new Size(420, 24);
            pbDownload.TabIndex = 1;
            pbDownload.Value = 0;

            // ---- btnDownload (下载按钮) ----
            btnDownload.BackColor = Color.FromArgb(45, 95, 180);
            btnDownload.Font = new Font("Microsoft YaHei UI", 9F);
            btnDownload.ForeColor = Color.White;
            btnDownload.Location = new Point(230, 185);
            btnDownload.Name = "btnDownload";
            btnDownload.Size = new Size(100, 35);
            btnDownload.TabIndex = 2;
            btnDownload.Text = "下载更新";
            btnDownload.Enabled = false;
            btnDownload.Click += btnDownload_Click;

            // ---- btnSkip (跳过按钮) ----
            btnSkip.BackColor = Color.FromArgb(60, 60, 65);
            btnSkip.Font = new Font("Microsoft YaHei UI", 9F);
            btnSkip.ForeColor = Color.Silver;
            btnSkip.Location = new Point(340, 185);
            btnSkip.Name = "btnSkip";
            btnSkip.Size = new Size(100, 35);
            btnSkip.TabIndex = 3;
            btnSkip.Text = "跳过";
            btnSkip.Enabled = false;
            btnSkip.Click += btnSkip_Click;

            // ---- FormUpdate ----
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(28, 28, 32);
            ClientSize = new Size(460, 240);
            Controls.Add(modernPanel1);
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "FormUpdate";
            StartPosition = FormStartPosition.CenterParent;
            Text = "软件更新";
            Load += FormUpdate_Load;

            modernPanel1.ResumeLayout(false);
            ResumeLayout(false);
        }

        private void FormUpdate_Load(object? sender,
            EventArgs e)
        {
            if (_release == null || !_release.Success)
            {
                lblStatus.Text =
                    _release?.Error ?? "未获取到更新信息";
                btnSkip.Text = "关闭";
                btnSkip.Enabled = true;
                return;
            }

            lblStatus.Text =
                $"发现新版本：v{_release.TagName}\n" +
                $"当前版本：v{UpdateManager.CurrentVersion}\n" +
                $"文件大小：{_release.FileSize
                    / 1024.0 / 1024.0:F1} MB";
            btnDownload.Enabled = true;
            btnSkip.Enabled = true;
        }

        private async void btnDownload_Click(object? sender,
            EventArgs e)
        {
            if (_release == null || !_release.Success)
            {
                return;
            }

            btnDownload.Enabled = false;
            btnSkip.Enabled = false;
            _cts = new CancellationTokenSource();

            var progress =
                new Progress<UpdateProgressEventArgs>(e =>
                {
                    pbDownload.Value = e.Percent;
                    lblStatus.Text =
                        $"正在下载 v{_release.TagName}...\n" +
                        $"{e.BytesDownloaded / 1024.0 / 1024.0:F1} MB" +
                        $" / {e.TotalBytes / 1024.0 / 1024.0:F1} MB\n" +
                        $"速度：{e.SpeedBytesPerSec / 1024.0 / 1024.0:F1} MB/s";
                });

            try
            {
                string newPath = await _manager.DownloadAsync(
                    _release, progress, _cts.Token);

                lblStatus.Text =
                    "下载完成！正在准备更新...\n" +
                    "应用将在 2 秒后自动重启。";

                await Task.Delay(1500);

                _manager.InstallAndRestart(newPath);
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "下载已取消";
                btnSkip.Enabled = true;
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"下载失败：{ex.Message}";
                btnSkip.Enabled = true;
            }
        }

        private void btnSkip_Click(object? sender, EventArgs e)
        {
            _cts?.Cancel();
            Close();
        }

        protected override void OnFormClosing(
            FormClosingEventArgs e)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
