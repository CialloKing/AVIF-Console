using System;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Forms;
using LakeUI;

namespace AvifEncoder.GuiLakeUI.选项窗口
{
    public partial class FormLog : Form
    {
        public FormLog()
        {
            InitializeComponent();
            // 启动时立即写入一条初始日志，避免页面空白
            AppendLog("日志系统已初始化，等待环境检测...");
        }

        public void AppendLog(string message)
        {
            string line = $"{DateTime.Now:HH:mm:ss} {message}";
            if (txtLog.InvokeRequired)
            {
                txtLog.BeginInvoke(new Action(() => AppendLog(message)));
            }
            else
            {
                txtLog.AppendLine(line);
                // 滚动到文本末尾，保持最新日志可见
                txtLog.ScrollToBottom();
            }
        }


        //public void AppendLog(string message)
        //{
        //    string line = $"{DateTime.Now:HH:mm:ss} {message}";
        //    if (txtLog.InvokeRequired)
        //    {
        //        txtLog.BeginInvoke(new Action(() => AppendLog(message)));
        //    }
        //    else
        //    {
        //        // 强制使用 \n 换行，适配 ModernTextBox 的渲染要求
        //        txtLog.AppendText(line + "\n");
        //        // 或者更安全的写法：统一替换所有 \r\n 和 \r 为 \n
        //        // txtLog.AppendText((line + "\n").Replace("\r\n", "\n").Replace("\r", "\n"));
        //        txtLog.ScrollToBottom();
        //    }
        //}


        private void modernPanel1_Scroll(object sender, ScrollEventArgs e)
        {
            // 空实现（如不需要可删除）
        }

        [MemberNotNull(nameof(txtLog), nameof(modernPanel1))]
        private void InitializeComponent()
        {
            modernPanel1 = new ModernPanel();
            txtLog = new ModernTextBox();
            modernPanel1.SuspendLayout();
            SuspendLayout();
            // 
            // modernPanel1
            // 
            modernPanel1.BackColor = Color.Transparent;
            modernPanel1.BackColor1 = Color.Transparent;
            modernPanel1.BorderColor = Color.Transparent;
            modernPanel1.Controls.Add(txtLog);
            modernPanel1.Dock = DockStyle.Fill;
            modernPanel1.ForeColor = Color.Transparent;
            modernPanel1.Location = new Point(0, 0);
            modernPanel1.Name = "modernPanel1";
            modernPanel1.Size = new Size(1114, 681);
            modernPanel1.TabIndex = 0;
            // 
            // txtLog
            // 
            txtLog.AnimationFPS = 0;
            txtLog.BackColor1 = Color.Transparent;
            txtLog.BorderColor = Color.Transparent;
            txtLog.BorderColorFocus = Color.Transparent;
            txtLog.Dock = DockStyle.Fill;
            txtLog.ForeColor = Color.White;
            txtLog.Location = new Point(1, 1);
            txtLog.Margin = new Padding(2);
            txtLog.MaxUndoCount = 0;
            txtLog.MultiLine = true;
            txtLog.Name = "txtLog";
            txtLog.ReadOnly = true;
            txtLog.SelectionColor = Color.FromArgb(180, 128, 128, 128);
            txtLog.Size = new Size(1111, 678);
            txtLog.TabIndex = 0;
            txtLog.Text = "日志页";
            txtLog.WordWrap = false;
            // 
            // FormLog
            // 
            AutoSize = true;
            BackColor = Color.Black;
            ClientSize = new Size(1114, 681);
            Controls.Add(modernPanel1);
            Name = "FormLog";
            modernPanel1.ResumeLayout(false);
            ResumeLayout(false);
        }

        private void txtLog_TextChanged(object sender, EventArgs e)
        {
            // 空实现（如不需要可删除）
        }

        private ModernTextBox txtLog;
        public ModernPanel modernPanel1;
    }
}