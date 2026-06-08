using System;
using System.Text;
using System.Windows.Forms;
using AvifEncoder;


namespace AvifEncoder.Gui
{


    public class GuiLogger : ILogger
    {
        private readonly RichTextBox _rtb;
        public GuiLogger(RichTextBox rtb) => _rtb = rtb;

        // ★ 窗口关闭后 _rtb 被 Dispose，后台编码线程仍可能通过 ILogger 接口调用 Append。
        //    BeginInvoke 在已释放控件上会抛 ObjectDisposedException，必须检查 IsDisposed。
        private void Append(string msg)
        {
            if (_rtb.IsDisposed) return;
            if (_rtb.InvokeRequired)
            {
                try { _rtb.BeginInvoke(new Action(() => AppendCore(msg))); } catch { }
            }
            else
            {
                AppendCore(msg);
            }
        }

        private void AppendCore(string msg)
        {
            if (_rtb.IsDisposed) return;
            _rtb.AppendText($"{msg}{Environment.NewLine}");
            _rtb.ScrollToCaret();
        }

        public void LogInfo(string msg) => Append($"[INFO] {msg}");
        public void LogError(string msg) => Append($"[ERROR] {msg}");
        public void LogMetric(string name, string msg) => Append($"[{name.ToUpper()}] {msg}");
        public void LogSearch(string msg) => Append($"[SEARCH] {msg}");
    }
}
