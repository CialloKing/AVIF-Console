using System;
using System.Drawing;
using System.Windows.Forms;
using LakeUI;

namespace AvifEncoder.GuiLakeUl
{
    // Main window that uses ThisIsYourWindow component to provide LakeUI titlebar,
    // and hosts existing Form1 inside a ModernPanel.
    public class MainWindow : Form
    {
        private ThisIsYourWindow thisIsYourWindow;
        private ModernPanel contentPanel;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "AVIF 转换器";
            this.Size = new Size(1200, 900);

            // Create and attach LakeUI window decorator
            thisIsYourWindow = new ThisIsYourWindow();
            thisIsYourWindow.Attach(this);

            contentPanel = new ModernPanel();
            contentPanel.Dock = DockStyle.Fill;
            this.Controls.Add(contentPanel);

            // Host the existing Form1 inside the ModernPanel
            var form1 = new Form1();
            form1.TopLevel = false;
            form1.FormBorderStyle = FormBorderStyle.None;
            form1.Dock = DockStyle.Fill;
            contentPanel.Controls.Add(form1);
            form1.Show();
        }
    }
}
