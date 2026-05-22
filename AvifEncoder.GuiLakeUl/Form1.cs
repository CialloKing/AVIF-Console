using AvifEncoder.GuiLakeUl.选项窗口;
using LakeUI;
using static LakeUI.D2DHelper;

namespace AvifEncoder.GuiLakeUl
{
    public partial class Form1 : Form
    {
        // 注意声明为可空，因为会在 Load 事件中才初始化
        private FormEncode? _encodePage;

        public Form1()
        {
            D2DHelper.GlobalTextQuality = TextQualityMode.ClearType;
            InitializeComponent();
        }

        // 增加一个字段
        private FormLog? _logPage;

        private void Form1_Load(object sender, EventArgs e)
        {
            thisIsYourWindow1.Attach(this);


            // 1. 创建页面实例
            _encodePage = new FormEncode();
            _encodePage.modernPanel1.BackColor = Color.Transparent;
            _encodePage.modernPanel1.BackColor1 = Color.Transparent;
            _encodePage.modernPanel1.BackgroundSource = this;

            _logPage = new FormLog();

            // 2. 确保选项卡集合至少有两个元素（若设计器已加两个，则无需补充）
            while (modernTabListControl1.Items.Count < 2)
            {
                modernTabListControl1.Items.Add(new ModernTabListControl.ModernTabPage());
            }

            // 3. 设置选项卡1（编码）
            modernTabListControl1.Items[0].Text = "编码";
            modernTabListControl1.Items[0].BoundControl = _encodePage;

            // 4. 设置选项卡2（日志）
            modernTabListControl1.Items[1].Text = "日志";
            modernTabListControl1.Items[1].BoundControl = _logPage;

            // 5. 默认选中第一个
            modernTabListControl1.SelectedIndex = 0;

            _encodePage.LogPage = _logPage;
        }



        // 暂时保留，后面可能会用到
        private void modernTabListControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
        }
    }
}