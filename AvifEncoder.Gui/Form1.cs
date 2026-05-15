using static AvifEncoder.PresetConfig;

namespace AvifEncoder.Gui
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void lblOutput_Click(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        { }



        private async void btnStart_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtInput.Text) || string.IsNullOrWhiteSpace(txtOutput.Text))
            {
                MessageBox.Show("请输入输入和输出目录");
                return;
            }

            var preset = cmbPreset.SelectedItem?.ToString() switch
            {
                "fast" => CliPreset.Fast,
                "balanced" => CliPreset.Balanced,
                "best" => CliPreset.Best,
                "extreme" => CliPreset.Extreme,
                _ => CliPreset.Balanced
            };

            var config = AvifPipeline.CreateFromPreset(preset);
            var logger = new GuiLogger(rtbLog);
            Logger.SetInstance(logger);

            btnStart.Enabled = false;
            progressBar1.Style = ProgressBarStyle.Marquee;

            try
            {
                var pipeline = new AvifPipeline(
                    txtInput.Text, txtOutput.Text, config,
                    logger: logger,
                    processRunner: new RealProcessRunner(),
                    fileSystem: new RealFileSystem(),
                    cacheManager: new CacheManager());

                await Task.Run(() => pipeline.RunAsync());
                logger.LogInfo("全部完成！");
            }
            catch (Exception ex)
            {
                logger.LogError($"异常: {ex.Message}");
            }
            finally
            {
                btnStart.Enabled = true;
                progressBar1.Style = ProgressBarStyle.Blocks;
                progressBar1.Value = 0;
            }
        }





        private void btnBrowseInput_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                    txtInput.Text = dlg.SelectedPath;
            }
        }

        private void btnBrowseOutput_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                    txtOutput.Text = dlg.SelectedPath;
            }
        }

        private void progressBar1_Click(object sender, EventArgs e)
        {

        }
    }
}
