namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class CliCrashLogTests
    {
        [TestMethod]
        public void AppendCrashLog_AllowsSharedOpenCrashLog()
        {
            string root = Path.Combine(Path.GetTempPath(), $"cli_crash_{Guid.NewGuid():N}");
            string logDir = Path.Combine(root, "log");
            string crashLog = Path.Combine(logDir, "crash.log");
            try
            {
                Directory.CreateDirectory(logDir);
                using (var held = new FileStream(
                    crashLog,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite))
                using (var writer = new StreamWriter(held))
                {
                    writer.WriteLine("held");
                    writer.Flush();

                    Program.AppendCrashLog(root, new InvalidOperationException("boom"));
                }

                string text = File.ReadAllText(crashLog);
                Assert.Contains("held", text);
                Assert.Contains("boom", text);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }
    }
}
