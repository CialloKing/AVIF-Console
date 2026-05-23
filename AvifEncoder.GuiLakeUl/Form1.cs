using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using AvifEncoder;
using AvifEncoder.GuiLakeUl.选项窗口;
using LakeUI;
using static LakeUI.D2DHelper;

namespace AvifEncoder.GuiLakeUl
{
    public partial class Form1 : Form
    {
        private FormEncode? _encodePage;
        private FormLog? _logPage;
        private FormHelp? _helpPage;

        public Form1()
        {
            D2DHelper.GlobalTextQuality = TextQualityMode.ClearType;
            InitializeComponent();
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            thisIsYourWindow1.Attach(this);

            // 创建页面实例
            _helpPage = new FormHelp();
            _encodePage = new FormEncode();
            _encodePage.modernPanel1.BackColor = Color.Transparent;
            _encodePage.modernPanel1.BackColor1 = Color.Transparent;
            _encodePage.modernPanel1.BackgroundSource = this;

            _logPage = new FormLog();

            // 确保选项卡集合至少有三个元素
            while (modernTabListControl1.Items.Count < 3)
            {
                modernTabListControl1.Items.Add(new ModernTabListControl.ModernTabPage());
            }

            // 设置选项卡：使用说明 -> 编码 -> 日志
            // 原顺序（0:使用说明, 1:编码, 2:日志）
            modernTabListControl1.Items[0].Text = "编码";
            modernTabListControl1.Items[0].BoundControl = _encodePage;

            modernTabListControl1.Items[1].Text = "日志";
            modernTabListControl1.Items[1].BoundControl = _logPage;

            modernTabListControl1.Items[2].Text = "使用说明";
            modernTabListControl1.Items[2].BoundControl = _helpPage;



            // 同时修改默认选中页（假如仍希望启动时选中编码页，则索引为 0）
            modernTabListControl1.SelectedIndex = 0;

            _encodePage.LogPage = _logPage;

            // 启动环境检测
            await RunStartupCheckAsync();
        }

        private async Task RunStartupCheckAsync()
        {
            try
            {
                _logPage?.AppendLog("===== 启动环境检测 =====");
                var guiLogger = new GuiLogger(_logPage);
                await AvifEnvironmentChecker.CheckEnvironmentAsync(guiLogger);
                _logPage?.AppendLog("===== 启动检测完成 =====");
            }
            catch (Exception ex)
            {
                _logPage?.AppendLog($"环境检测异常: {ex.Message}");
            }
        }

        private void modernTabListControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
        }
    }
}