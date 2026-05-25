using System;
using System.Drawing;
using System.Runtime.InteropServices;
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
        private const float AspectRatio = 16f / 9f;    // ★ 设定 16:9 比例
        private FormEncode? _encodePage;
        private FormLog? _logPage;
        private FormHelp? _helpPage;

        public Form1()
        {
            D2DHelper.GlobalTextQuality = TextQualityMode.ClearType;
            InitializeComponent();
            // 可设置一个合理的初始大小
            this.Size = new Size(1600, 900);             // ← 改为 1600×900
            this.MinimumSize = new Size(640, 360);     // 最小尺寸（保持比例）
        }

        // ===== 添加 WM_SIZING 处理 =====
        private const int WM_SIZING = 0x0214;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SIZING)
            {
                RECT rc = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT))!;
                Rectangle r = rc.ToRectangle();
                int edge = m.WParam.ToInt32();

                // 根据边缘修正尺寸
                Rectangle newRect = ApplyAspectRatio(r, edge);
                Marshal.StructureToPtr(RECT.FromRectangle(newRect), m.LParam, true);
                m.Result = IntPtr.Zero;
                return; // 消息已处理
            }

            base.WndProc(ref m);
        }

        private Rectangle ApplyAspectRatio(Rectangle r, int edge)
        {
            // 边缘方向常量（与 Win32 定义一致）
            const int WMSZ_LEFT = 1, WMSZ_RIGHT = 2, WMSZ_TOP = 3,
                      WMSZ_TOPLEFT = 4, WMSZ_TOPRIGHT = 5, WMSZ_BOTTOM = 6,
                      WMSZ_BOTTOMLEFT = 7, WMSZ_BOTTOMRIGHT = 8;

            int newWidth = r.Width, newHeight = r.Height;
            int x = r.X, y = r.Y;

            // 根据不同的拖动边缘采用不同的修正策略
            switch (edge)
            {
                case WMSZ_LEFT:
                case WMSZ_RIGHT:
                    // 仅水平拖动 → 高度由宽度决定，垂直居中
                    newHeight = (int)(r.Width / AspectRatio);
                    y += (r.Height - newHeight) / 2;
                    break;
                case WMSZ_TOP:
                case WMSZ_BOTTOM:
                    // 仅垂直拖动 → 宽度由高度决定，水平居中
                    newWidth = (int)(r.Height * AspectRatio);
                    x += (r.Width - newWidth) / 2;
                    break;
                case WMSZ_TOPLEFT:
                    // 拖动左上角 → 以右下角为锚点
                    newHeight = (int)(r.Width / AspectRatio);
                    y = r.Bottom - newHeight;
                    break;
                case WMSZ_TOPRIGHT:
                    // 拖动右上角 → 以左下角为锚点
                    newHeight = (int)(r.Width / AspectRatio);
                    y = r.Bottom - newHeight;
                    break;
                case WMSZ_BOTTOMLEFT:
                    // 拖动左下角 → 以右上角为锚点
                    newHeight = (int)(r.Width / AspectRatio);
                    // y 不变（下边界固定）
                    break;
                case WMSZ_BOTTOMRIGHT:
                    // 拖动右下角 → 以左上角为锚点
                    newHeight = (int)(r.Width / AspectRatio);
                    break;
                default:
                    break;
            }

            // 确保不超出屏幕工作区（可选）
            Rectangle screen = Screen.FromRectangle(r).WorkingArea;
            newWidth = Math.Min(newWidth, screen.Width);
            newHeight = Math.Min(newHeight, screen.Height);

            // 防止小于最小尺寸
            int minWidth = MinimumSize.Width;
            int minHeight = MinimumSize.Height;
            if (newWidth < minWidth)
            {
                newWidth = minWidth;
                newHeight = (int)(minWidth / AspectRatio);
            }
            if (newHeight < minHeight)
            {
                newHeight = minHeight;
                newWidth = (int)(minHeight * AspectRatio);
            }

            return new Rectangle(x, y, newWidth, newHeight);
        }

        // ===== RECT 结构（用于 Marshal） =====
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
            public Rectangle ToRectangle() => new Rectangle(Left, Top, Right - Left, Bottom - Top);
            public static RECT FromRectangle(Rectangle r) => new RECT
            {
                Left = r.Left,
                Top = r.Top,
                Right = r.Right,
                Bottom = r.Bottom
            };
        }

        // ===== 原有代码保持不变 =====
        private async void Form1_Load(object sender, EventArgs e)
        {
            thisIsYourWindow1.Attach(this);

            _helpPage = new FormHelp();
            _encodePage = new FormEncode();
            _logPage = new FormLog();

            MakePanelTransparent(_encodePage.modernPanel1);
            MakePanelTransparent(_logPage.modernPanel1);
            MakePanelTransparent(_helpPage.modernPanel1);

            while (modernTabListControl1.Items.Count < 3)
                modernTabListControl1.Items.Add(new ModernTabListControl.ModernTabPage());

            modernTabListControl1.Items[0].Text = "编码";
            modernTabListControl1.Items[0].BoundControl = _encodePage;
            modernTabListControl1.Items[1].Text = "日志";
            modernTabListControl1.Items[1].BoundControl = _logPage;
            modernTabListControl1.Items[2].Text = "使用说明";
            modernTabListControl1.Items[2].BoundControl = _helpPage;

            modernTabListControl1.SelectedIndex = 0;
            _encodePage.LogPage = _logPage;

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

        private void modernTabListControl1_SelectedIndexChanged(object sender, EventArgs e) { }

        private void MakePanelTransparent(ModernPanel panel)
        {
            panel.BackColor = Color.Transparent;
            panel.BackColor1 = Color.Transparent;
            panel.BackgroundSource = this;
        }
    }
}