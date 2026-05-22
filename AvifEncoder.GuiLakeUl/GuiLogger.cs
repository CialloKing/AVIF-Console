using System;
using LakeUI;
using System.Windows.Forms;
using AvifEncoder;

namespace AvifEncoder.GuiLakeUl
{


    public class GuiLogger : ILogger
    {
        private readonly ModernTextBox _rtb;
        public GuiLogger(ModernTextBox rtb) => _rtb = rtb;

        private void Append(string msg)
        {
            if (_rtb.InvokeRequired)
                _rtb.BeginInvoke(new Action(() =>
                {
                    _rtb.Text += $"{msg}{Environment.NewLine}";
                }));
            else
            {
                _rtb.Text += $"{msg}{Environment.NewLine}";
            }
        }

        public void LogInfo(string msg) => Append($"[INFO] {msg}");
        public void LogError(string msg) => Append($"[ERROR] {msg}");
        public void LogMetric(string name, string msg) => Append($"[{name.ToUpper()}] {msg}");
        public void LogSearch(string msg) => Append($"[SEARCH] {msg}");
    }
}
