using System.Windows.Forms;

namespace AvifEncoder.GuiLakeUI
{
    public class BasicPage : UserControl
    {
        public BasicPage()
        {
            Dock = DockStyle.Fill;

            Controls.Add(new Label
            {
                Text = "基础设置页面",
                Dock = DockStyle.Top
            });

            Controls.Add(new TextBox
            {
                Dock = DockStyle.Top,
                PlaceholderText = "输入目录"
            });

            Controls.Add(new TextBox
            {
                Dock = DockStyle.Top,
                PlaceholderText = "输出目录"
            });
        }
    }
}