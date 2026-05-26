using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AvifEncoder;

namespace AvifEncoder.GuiLakeUl.选项窗口
{
    public partial class FormUpdate : Form
    {
        private readonly UpdateManager _manager = new();
        private ReleaseInfo? _release;
        private CancellationTokenSource? _cts;

        public FormUpdate()
        {
            InitializeComponent();
        }

        /// <summary>加载时自动检查更新</summary>
        private async void FormUpdate_Load(object sender,
    EventArgs e)
        {
            try
            {
                lblStatus.Text = "正在检查更新...";
                btnDownload.Enabled = false;
                btnSkip.Enabled = false;

                _release =
                    await _manager.CheckForUpdateAsync();

                if (_release == null || !_release.Success)
                {
                    lblStatus.Text =
                        _release?.Error ?? "当前已是最新版本";
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
            catch (Exception ex)
            {
                lblStatus.Text =
                    $"检查更新失败：{ex.Message}";
                btnSkip.Text = "关闭";
                btnSkip.Enabled = true;
            }
        }

        private async void btnDownload_Click(object sender,
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

        private void btnSkip_Click(object sender, EventArgs e)
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