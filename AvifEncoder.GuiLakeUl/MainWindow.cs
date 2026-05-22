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

            // Host the existing Form1 inside the ModernPanel and bind to a tab
            var form1 = new Form1();
            form1.TopLevel = false;
            form1.FormBorderStyle = FormBorderStyle.None;
            form1.Dock = DockStyle.Fill;
            contentPanel.Controls.Add(form1);
            form1.Show();

            // Create a tab page and bind the Form1 as its content
            var page = new ModernTabListControl.ModernTabPage("主界面");
            page.BoundControl = form1;
            tabList.Items.Add(page);

            // Ensure items refreshed and select first
            tabList.RefreshItems();
            tabList.SelectedIndex = 0;
        }
    }
}
