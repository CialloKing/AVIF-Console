using System;
using System.Drawing;
using System.Windows.Forms;
using LakeUI;

namespace AvifEncoder.GuiLakeUl
{
    // Modernized main window styled like LakeUI.Demo: left ModernTabListControl + right ModernPanel content.
    public class MainWindow : Form
    {
        private ThisIsYourWindow? thisIsYourWindow;
        private ModernTabListControl? tabList;
        private ModernPanel? contentPanel;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "AVIF 转换器";
            this.Size = new Size(1200, 900);

            // D2D text quality (as in LakeUI.Demo)
            D2DHelper.GlobalTextQuality = D2DHelper.TextQualityMode.ClearType;

            // Create and attach LakeUI window decorator
            thisIsYourWindow = new ThisIsYourWindow();
            thisIsYourWindow.Attach(this);

            // Left: ModernTabListControl
            tabList = new ModernTabListControl();
            tabList.Dock = DockStyle.Left;
            tabList.Width = 260;
            try { tabList.GetType().GetProperty("BackgroundSource")?.SetValue(tabList, this); } catch { }

            // Right: content ModernPanel
            contentPanel = new ModernPanel();
            contentPanel.Dock = DockStyle.Fill;
            contentPanel.BackColor = Color.Transparent;
            // Some LakeUI panels expose BackColor1 for gradient; set if available via reflection
            try { contentPanel.GetType().GetProperty("BackColor1")?.SetValue(contentPanel, Color.Transparent); } catch { }
            try { contentPanel.GetType().GetProperty("BackgroundSource")?.SetValue(contentPanel, this); } catch { }

            this.Controls.Add(contentPanel);
            this.Controls.Add(tabList);

            // Create Form1 instance and extract its logical panels to host as separate pages
            var form1 = new Form1();
            form1.TopLevel = false;
            form1.FormBorderStyle = FormBorderStyle.None;
            form1.Dock = DockStyle.Fill;

            // The Form1 Designer created multiple ModernPanel pnlBasic/pnlEncode/pnlQuality/pnlLog and added them to the Form.
            // We move those panels into our contentPanel so they become direct children and can be bound to tab pages.
            string[] panelNames = new[] { "pnlBasic", "pnlEncode", "pnlQuality", "pnlLog" };
            foreach (var name in panelNames)
            {
                var matches = form1.Controls.Find(name, true);
                if (matches != null && matches.Length > 0)
                {
                    var pnl = matches[0];
                    // Detach from Form1 and add to contentPanel
                    form1.Controls.Remove(pnl);
                    pnl.Dock = DockStyle.Fill;
                    contentPanel.Controls.Add(pnl);

                    // Create corresponding tab
                    var tab = new ModernTabListControl.ModernTabPage(name == "pnlBasic" ? "基本" :
                                                                      name == "pnlEncode" ? "编码" :
                                                                      name == "pnlQuality" ? "质量" :
                                                                      "日志");
                    tab.BoundControl = pnl;
                    tabList.Items.Add(tab);
                }
            }

            // Dispose the empty Form1 since its panels have been moved
            try { form1.Dispose(); } catch { }

            // Ensure items refreshed and select first
            tabList.RefreshItems();
            tabList.SelectedIndex = 0;
        }
    }
}
