using System;
using System.Windows.Forms;
using LakeUI;

namespace AvifEncoder.GuiLakeUI
{
    public partial class MainWindow : Form
    {
        private Panel pageContainer;

        private UserControl basicPage;
        private UserControl encodePage;
        private UserControl qualityPage;
        private UserControl logPage;

        public MainWindow()
        {
            InitializeComponent();

            // LakeUI 窗口效果（必须在 Designer 里拖 ThisIsYourWindow）
            thisIsYourWindow1.Attach(this);

            BuildLayout();
            BuildPages();
        }

        private void BuildLayout()
        {
            // 左侧导航
            var nav = new ListBox
            {
                Dock = DockStyle.Left,
                Width = 160
            };

            nav.Items.AddRange(new object[]
            {
                "基础",
                "编码器",
                "质量",
                "日志"
            });

            nav.SelectedIndexChanged += (s, e) =>
            {
                switch (nav.SelectedIndex)
                {
                    case 0: SwitchPage(basicPage); break;
                    case 1: SwitchPage(encodePage); break;
                    case 2: SwitchPage(qualityPage); break;
                    case 3: SwitchPage(logPage); break;
                }
            };

            this.Controls.Add(nav);

            // 右侧容器
            pageContainer = new Panel
            {
                Dock = DockStyle.Fill
            };

            this.Controls.Add(pageContainer);
        }

        private void BuildPages()
        {
            basicPage = new BasicPage();
            encodePage = new EncodePage();
            qualityPage = new QualityPage();
            logPage = new LogPage();

            SwitchPage(basicPage);
        }

        private void SwitchPage(Control page)
        {
            pageContainer.Controls.Clear();
            page.Dock = DockStyle.Fill;
            pageContainer.Controls.Add(page);
        }
    }
}