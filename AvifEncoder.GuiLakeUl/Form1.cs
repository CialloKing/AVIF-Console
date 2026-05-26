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

        private FormOtherOptions? _otherOptionsPage;

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
            _otherOptionsPage = new FormOtherOptions();          // ★ 新增

            MakePanelTransparent(_encodePage.modernPanel1);
            MakePanelTransparent(_logPage.modernPanel1);
            MakePanelTransparent(_helpPage.modernPanel1);
            MakePanelTransparent(_otherOptionsPage.modernPanel1); // ★ 新增透明处理

            // ★ 标签页数量改为 4
            while (modernTabListControl1.Items.Count < 4)
                modernTabListControl1.Items.Add(new ModernTabListControl.ModernTabPage());

            modernTabListControl1.Items[0].Text = "编码";
            modernTabListControl1.Items[0].BoundControl = _encodePage;
            modernTabListControl1.Items[1].Text = "日志";
            modernTabListControl1.Items[1].BoundControl = _logPage;
            modernTabListControl1.Items[2].Text = "使用说明";
            modernTabListControl1.Items[2].BoundControl = _helpPage;
            // ★ 新增第四个标签页
            modernTabListControl1.Items[3].Text = "其他选项";
            modernTabListControl1.Items[3].BoundControl = _otherOptionsPage;

            modernTabListControl1.SelectedIndex = 0;
            _encodePage.LogPage = _logPage!;

            await RunStartupCheckAsync();

            TryLoadDefaultConfig();

            // ★ 启动后静默检查更新
            _ = CheckForUpdateSilentlyAsync();

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
        /// <summary>
        /// 递归为指定控件及其所有子控件设置字体
        /// </summary>
        /// <summary>
        /// 递归为指定控件及其所有子控件设置字体，同时强制重绘湖景控件。
        /// </summary>
        public void ApplyGlobalFont(Font font)
        {
            if (font == null) return;
            this.SuspendLayout();
            try
            {
                SetFontRecursive(this, font, isRoot: true);
            }
            finally
            {
                // 不调用 PerformLayout，避免自动布局破坏原有位置
                this.ResumeLayout(false);
                // ★ 强制整个窗口重绘，确保 LakeUI 控件应用新字体
                this.Refresh();
            }
        }
        private void TryLoadDefaultConfig()
        {
            // 优先读程序所在目录，再读工作目录，使用第一个合法 json
            string exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? ".";
            string workDir = Environment.CurrentDirectory;
            string jsonName = "app_settings.json";

            AppConfig? config = null;

            foreach (string dir in new[] { exeDir, workDir })
            {
                if (dir == workDir && string.Equals(
                    Path.GetFullPath(exeDir),
                    Path.GetFullPath(workDir),
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue; // 同一个目录，跳过第二次读
                }

                string candidate = Path.Combine(dir, jsonName);
                config = AppConfigHelper.LoadFromFile(candidate);
                if (config != null)
                {
                    break;
                }
            }

            if (config == null)
            {
                return;
            }




            // 字体（仅当配置了字体时才应用，不影响其他设置）
            if (!string.IsNullOrWhiteSpace(config.FontFamily))
            {
                try
                {
                    var font = new Font(config.FontFamily, config.FontSize);
                    ApplyGlobalFont(font);
                    _otherOptionsPage?.SyncCurrentFont(font);
                }
                catch { /* 忽略无效字体 */ }
            }

            // 编码设置（始终尝试恢复）
            try
            {
                _encodePage?.ApplyConfig(config);
            }
            catch { /* 忽略编码设置恢复失败 */ }

            // 窗口状态
            try
            {
                if (config.Maximized)
                {
                    WindowState = FormWindowState.Maximized;
                }
                else
                {
                    WindowState = FormWindowState.Normal;
                    if (config.WindowLeft >= 0 && config.WindowTop >= 0)
                    {
                        Left = config.WindowLeft;
                        Top = config.WindowTop;
                    }
                    if (config.WindowWidth > 0 && config.WindowHeight > 0)
                    {
                        Width = config.WindowWidth;
                        Height = config.WindowHeight;
                    }
                }
            }
            catch { /* 忽略窗口状态加载失败 */ }
        }

        /// <summary>
        /// 为 LakeUI 对话框提供统一的附着，避免窗口主题/动画异常。
        /// </summary>
        public void AttachDialog(Form dialog)
        {
            thisIsYourWindow1.Attach(dialog);
        }
        private void SetFontRecursive(Control parent, Font font, bool isRoot = false)
        {
            foreach (Control ctrl in parent.Controls)
            {
                ctrl.Font = font;
                ctrl.Invalidate();
                ctrl.Update();   // 立即重绘，确保 LakeUI 绘制新字体
                if (ctrl.Controls.Count > 0)
                    SetFontRecursive(ctrl, font);
            }
            if (isRoot)
            {
                foreach (var page in new Form?[] { _encodePage, _logPage, _helpPage, _otherOptionsPage })
                {
                    if (page != null && !page.IsDisposed)
                    {
                        page.SuspendLayout();
                        try
                        {
                            SetFontRecursive(page, font);
                        }
                        finally
                        {
                            page.ResumeLayout(false);   // 不触发自动排列
                        }
                    }
                }
            }
        }

        // ★ 静默检查更新
        // ★ 静默检查更新
        private async Task CheckForUpdateSilentlyAsync()
        {
            try
            {
                await Task.Delay(3000);

                var manager = new UpdateManager();
                var release = await manager.CheckForUpdateAsync();

                if (release != null && release.Success)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        using var frm = new FormUpdate();
                        frm.StartPosition = FormStartPosition.CenterParent;
                        frm.ShowDialog(this);
                    }));
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// 供 FormOtherOptions 保存时调用，收集编码页的控件状态。
        /// </summary>
        public void BuildEncodeConfig(AppConfig cfg)
        {
            _encodePage?.BuildConfig(cfg);
        }

        /// <summary>
        /// 供 FormOtherOptions 加载时调用，恢复编码页的控件状态。
        /// </summary>
        public void ApplyEncodeConfig(AppConfig cfg)
        {
            _encodePage?.ApplyConfig(cfg);
        }

    }
}