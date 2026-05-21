using System.Windows.Forms;

namespace AvifEncoder.GuiLakeUI
{
    public class EncodePage : UserControl
    {
        public EncodePage()
        {
            Dock = DockStyle.Fill;

            Controls.Add(new Label
            {
                Text = "编码器设置",
                Dock = DockStyle.Top
            });

            Controls.Add(new ComboBox
            {
                Dock = DockStyle.Top,
                Items = { "libaom-av1", "libsvtav1", "rav1e" },
                SelectedIndex = 0
            });
        }
    }
}