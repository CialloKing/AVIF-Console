using System.Windows.Forms;

namespace AvifEncoder.GuiLakeUI
{
    public class QualityPage : UserControl
    {
        public QualityPage()
        {
            Dock = DockStyle.Fill;

            Controls.Add(new Label
            {
                Text = "质量控制",
                Dock = DockStyle.Top
            });

            Controls.Add(new NumericUpDown
            {
                Dock = DockStyle.Top,
                Minimum = 0,
                Maximum = 100,
                Value = 95
            });
        }
    }
}