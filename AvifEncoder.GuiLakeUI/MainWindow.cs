using LakeUI;
using LakeUI.Controls;
using System;
using System.Drawing;
using System.Windows.Forms;
using static LakeUI.D2DHelper;

namespace AvifEncoder.Gui
{
    public partial class MainWindow : Form
    {
        // 声明各功能页实例
        private readonly FormBasic _basicPage = new FormBasic();
        private readonly FormQuality _qualityPage = new FormQuality();
        private readonly FormEncoder _encoderPage = new FormEncoder();
        private readonly FormAdvanced _advancedPage = new FormAdvanced();
        private readonly FormLog _logPage = new FormLog();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void MainWindow_Load(object sender, EventArgs e)
        {
            // 设置全局文本质量
            D2DHelper.GlobalTextQuality = TextQualityMode.ClearType;

            // 附加窗口美化组件（自定义标题栏、阴影等）
            thisIsYourWindow1.Attach(this);

            // 绑定子窗体到对应的导航页签
            BindTabPage(5, _basicPage);
            BindTabPage(6, _qualityPage);
            BindTabPage(7, _encoderPage);
            BindTabPage(8, _advancedPage);
            BindTabPage(9, _logPage);

            // 默认选中基础参数页
            modernTabListControl1.SelectedIndex = 5;
        }

        /// <summary>
        /// 统一绑定页签并将子窗体的根面板背景设为透明
        /// </summary>
        private void BindTabPage(int index, Form subForm)
        {
            var tab = modernTabListControl1.Items[index];
            tab.BoundControl = subForm;

            if (subForm.Controls.Count > 0
                && subForm.Controls[0] is ModernPanel rootPanel)
            {
                rootPanel.BackColor = Color.Transparent;
                rootPanel.BackColor1 = Color.Transparent;
                rootPanel.BackgroundSource = this; // 继承主窗体背景
            }
        }
    }
}