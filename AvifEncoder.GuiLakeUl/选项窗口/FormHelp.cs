using System;
using System.Windows.Forms;

namespace AvifEncoder.GuiLakeUl.选项窗口
{
    public partial class FormHelp : Form
    {
        public FormHelp()
        {
            InitializeComponent();
            LoadHelpText();
        }

        private void LoadHelpText()
        {
            if (txtHelp != null)
            {
                txtHelp.Text = GetHelpText();


                //txtHelp.Select(0, 0);  // 激活文本选择
            }
        }

        /// <summary>
        /// 返回完整的使用帮助文本（与命令行版本保持一致）
        /// </summary>
        private static string GetHelpText()
        {
            return HelpText.GuiGuide
                + "\n\n===== 命令行帮助文本 =====\n"
                + HelpText.CliHelp
                + HelpText.GuiControlTable;
        }

        private void FormHelp_Load(object sender, EventArgs e)
        {

        }
    }
}