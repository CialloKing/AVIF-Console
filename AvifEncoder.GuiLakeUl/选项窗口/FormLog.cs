using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace AvifEncoder.GuiLakeUl.选项窗口
{
    public partial class FormLog : Form
    {
        public FormLog()
        {
            InitializeComponent();
        }

        public void AppendLog(string message)
        {
            if (txtLog.InvokeRequired)
                txtLog.BeginInvoke(new Action(() => AppendLog(message)));
            else
                txtLog.AppendText($"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
        }

        private void modernPanel1_Scroll(object sender, ScrollEventArgs e)
        {

        }
    }
}
