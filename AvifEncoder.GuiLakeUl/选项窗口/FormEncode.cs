using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using AvifEncoder; // 核心库命名空间
using static AvifEncoder.PresetConfig; // 访问 CliPreset

namespace AvifEncoder.GuiLakeUl.选项窗口
{
    public partial class FormEncode : Form
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public FormLog? LogPage { get; set; }

        private bool _isEncoding; // 防止重入

        public FormEncode()
        {
            InitializeComponent();
            progressBar1.Maximum = 100;
            progressBar1.Value = 0;

            // 初始化编码选项下拉框
            SetupComboBoxes();
        }

        private void SetupComboBoxes()
        {
            cmbPreset.Items.AddRange(new string[] { "快速", "平衡", "最佳", "极限" });
            cmbPreset.SelectedIndex = 1; // "平衡"

            cmbEncoder.Items.AddRange(new string[] { "libaom-av1", "libsvtav1", "librav1e" });
            cmbEncoder.SelectedIndex = 0; // 默认 libaom-av1

            txtQualityTarget.Text = "95.0";
            chkCRFSearch.Checked = false;
        }

        private string SanitizePath(string? path)
        {
            return (path ?? "").Trim('"').Trim();
        }

        private void btnBrowseInput_Click(object? sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog();
            if (dlg.ShowDialog() == DialogResult.OK)
                txtInput.Text = dlg.SelectedPath;
        }

        private void btnBrowseOutput_Click(object? sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog();
            if (dlg.ShowDialog() == DialogResult.OK)
                txtOutput.Text = dlg.SelectedPath;
        }

        private async void btnStart_Click(object? sender, EventArgs e)
        {
            string inputDir = SanitizePath(txtInput.Text);
            string outputDir = SanitizePath(txtOutput.Text);

            if (string.IsNullOrWhiteSpace(inputDir) || string.IsNullOrWhiteSpace(outputDir))
            {
                MessageBox.Show("请输入输入和输出目录", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_isEncoding)
            {
                MessageBox.Show("编码正在进行中，请稍候...", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 检查目录是否存在
            if (!Directory.Exists(inputDir))
            {
                MessageBox.Show($"输入目录不存在:\n{inputDir}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 预先统计图片数量
            var extensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff", ".gif", ".jp2", ".j2k", ".jpx" };
            var files = Directory.EnumerateFiles(inputDir, "*.*", SearchOption.AllDirectories)
                                 .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                                 .ToList();

            if (files.Count == 0)
            {
                MessageBox.Show($"输入目录中没有支持的图片文件:\n{inputDir}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _isEncoding = true;
            btnStart.Enabled = false;
            progressBar1.Value = 0;

            try
            {
                LogPage?.AppendLog("===== 开始编码 =====");
                LogPage?.AppendLog($"输入目录: {inputDir}");
                LogPage?.AppendLog($"输出目录: {outputDir}");
                LogPage?.AppendLog($"发现图片: {files.Count} 张");

                // ========== 根据用户选择构建配置 ==========
                CliPreset selectedPreset = cmbPreset.SelectedIndex switch
                {
                    0 => CliPreset.Fast,
                    1 => CliPreset.Balanced,
                    2 => CliPreset.Best,
                    3 => CliPreset.Extreme,
                    _ => CliPreset.Balanced
                };

                var config = AvifPipeline.CreateFromPreset(selectedPreset);

                // 覆盖编码器
                config.Encoder = cmbEncoder.SelectedItem?.ToString() ?? "libaom-av1";

                // 解析质量目标（VMAF 范围 0‑100）
                if (double.TryParse(txtQualityTarget.Text, out double vmaf) && vmaf > 0 && vmaf <= 100)
                {
                    config.TargetSSIM = vmaf / 100.0; // 转换为内部 0‑1 目标
                    config.UseCRFSearch = chkCRFSearch.Checked;
                }
                else
                {
                    LogPage?.AppendLog("警告：质量目标无效，将使用预设默认值。");
                }

                LogPage?.AppendLog($"配置：预设={selectedPreset}, 编码器={config.Encoder}, VMAF目标={vmaf:F1}, CRF搜索={config.UseCRFSearch}");

                // 创建日志器
                var guiLogger = new GuiLogger(LogPage);
                var fileLogger = new FileLogger(outputDir);
                var logger = new CompositeLogger(guiLogger, fileLogger);

                // 进度报告
                var progress = new Progress<int>(percent =>
                {
                    if (InvokeRequired)
                        BeginInvoke(new Action(() => UpdateProgress(percent)));
                    else
                        UpdateProgress(percent);
                });

                using var pipeline = new AvifPipeline(
                    inputDir, outputDir, config,
                    logger: logger,
                    progress: progress);

                LogPage?.AppendLog("开始运行编码管道...");
                await Task.Run(() => pipeline.RunAsync());   // 等待完成

                LogPage?.AppendLog("===== 全部完成 =====");
                MessageBox.Show("转换完成！", "信息", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LogPage?.AppendLog($"严重错误: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"处理过程中发生错误:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isEncoding = false;
                btnStart.Enabled = true;
                progressBar1.Value = 100;
            }
        }

        private void UpdateProgress(int percent)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => UpdateProgress(percent)));
                return;
            }
            progressBar1.Value = Math.Max(0, Math.Min(percent, 100));
        }

        // 设计器生成的 Scroll 事件占位
        private void modernPanel1_Scroll(object sender, ScrollEventArgs e)
        {
        }
    }

    // ========== 跨线程安全的日志适配器 ==========
    public class GuiLogger : ILogger
    {
        private readonly FormLog? _logForm;

        public GuiLogger(FormLog? logForm)
        {
            _logForm = logForm;
        }

        public void LogInfo(string msg) => AppendSafe(msg);
        public void LogError(string msg) => AppendSafe("[ERROR] " + msg);
        public void LogMetric(string metric, string msg) => AppendSafe($"[{metric}] {msg}");
        public void LogSearch(string msg) => AppendSafe("[SEARCH] " + msg);

        private void AppendSafe(string message)
        {
            if (_logForm == null) return;
            _logForm.AppendLog(message);
        }
    }

    public class CompositeLogger : ILogger
    {
        private readonly ILogger[] _loggers;

        public CompositeLogger(params ILogger[] loggers)
        {
            _loggers = loggers ?? Array.Empty<ILogger>();
        }

        public void LogInfo(string msg) { foreach (var l in _loggers) l.LogInfo(msg); }
        public void LogError(string msg) { foreach (var l in _loggers) l.LogError(msg); }
        public void LogMetric(string metric, string msg) { foreach (var l in _loggers) l.LogMetric(metric, msg); }
        public void LogSearch(string msg) { foreach (var l in _loggers) l.LogSearch(msg); }
    }
}