using System.Windows.Forms;

namespace AvifEncoder.GuiLakeUI
{
    public class LogPage : UserControl
    {
        public LogPage()
        {
            Dock = DockStyle.Fill;

            Controls.Add(new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical
            });
        }
    }
}