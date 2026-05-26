using System;
using System.Text;
using System.Windows.Forms;
using AvifEncoder;


namespace AvifEncoder.Gui
{

    [Obsolete("AvifEncoder.Gui 已降低更新频率，请使用 AvifEncoder.GuiLakeUl")]
    public class GuiLogger : ILogger
    {
        private readonly RichTextBox _rtb;
        public GuiLogger(RichTextBox rtb) => _rtb = rtb;

        private void Append(string msg)
        {
            if (_rtb.InvokeRequired)
                _rtb.BeginInvoke(new Action(() =>
                {
                    _rtb.AppendText($"{msg}{Environment.NewLine}");
                    _rtb.ScrollToCaret();
                }));
            else
            {
                _rtb.AppendText($"{msg}{Environment.NewLine}");
                _rtb.ScrollToCaret();
            }
        }

        public void LogInfo(string msg) => Append($"[INFO] {msg}");
        public void LogError(string msg) => Append($"[ERROR] {msg}");
        public void LogMetric(string name, string msg) => Append($"[{name.ToUpper()}] {msg}");
        public void LogSearch(string msg) => Append($"[SEARCH] {msg}");
    }
}
