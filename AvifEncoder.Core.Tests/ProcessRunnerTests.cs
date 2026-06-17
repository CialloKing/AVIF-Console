using System.Diagnostics;

namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class ProcessRunnerTests
    {
        [TestMethod]
        public async Task RunAsync_CapturesStdoutAndStderr()
        {
            var runner = new RealProcessRunner();
            var (exitCode, stdout, stderr) = await runner.RunAsync(
                "cmd.exe",
                "/c \"echo hello & echo error 1>&2\"",
                TimeSpan.FromSeconds(5));

            Assert.AreEqual(0, exitCode);
            Assert.IsTrue(stdout.Contains("hello"));
            Assert.IsTrue(stderr.Contains("error"));
        }

        [TestMethod]
        public async Task RunAsync_Timeout_CancelsAndReturnsPromptly()
        {
            var runner = new RealProcessRunner();
            var sw = Stopwatch.StartNew();
            bool canceled = false;
            try
            {
                await runner.RunAsync(
                    "powershell.exe",
                    "-NoProfile -Command \"Write-Output start; Start-Sleep -Seconds 30\"",
                    TimeSpan.FromMilliseconds(250));
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }

            sw.Stop();
            Assert.IsTrue(canceled);
            Assert.IsTrue(sw.Elapsed < TimeSpan.FromSeconds(5));
        }
    }
}
