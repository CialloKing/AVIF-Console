using System.Collections.Concurrent;
using System.Reflection;

namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class LoggingTests
    {
        [TestMethod]
        public void FileLogger_DisposeUnsubscribesProcessExitHandlerAndIgnoresLateWrites()
        {
            string root = Path.Combine(Path.GetTempPath(), $"avif_logger_{Guid.NewGuid():N}");
            try
            {
                var logger = new FileLogger(root);
                logger.LogError("before dispose");

                Assert.IsNotNull(GetProcessExitHandler(logger));
                Assert.IsGreaterThan(0, GetWriterCount(logger));

                logger.Dispose();
                logger.LogInfo("after dispose");
                logger.LogError("after dispose");
                logger.LogMetric("ssim", "after dispose");

                Assert.IsNull(GetProcessExitHandler(logger));
                Assert.AreEqual(0, GetWriterCount(logger));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [TestMethod]
        public void LoggerSetInstance_DisposesPreviousDisposableLogger()
        {
            var first = new DisposableTestLogger();
            var second = new DisposableTestLogger();
            try
            {
                Logger.SetInstance(first);
                Logger.SetInstance(second);

                Assert.AreEqual(1, first.DisposeCount);
                Assert.AreEqual(0, second.DisposeCount);
            }
            finally
            {
                Logger.SetInstance(new NullLogger());
            }
        }

        [TestMethod]
        public void FileLogger_AllowsTwoInstancesToAppendSameLogDirectory()
        {
            string root = Path.Combine(Path.GetTempPath(), $"avif_logger_multi_{Guid.NewGuid():N}");
            try
            {
                using (var first = new FileLogger(root))
                using (var second = new FileLogger(root))
                {
                    first.LogInfo("from first");
                    second.LogInfo("from second");
                }

                string runLog = Directory
                    .GetFiles(Path.Combine(root, "log"), "run_*.log")
                    .Single();
                string text = File.ReadAllText(runLog);
                Assert.Contains("from first", text);
                Assert.Contains("from second", text);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        private static EventHandler? GetProcessExitHandler(FileLogger logger)
            => (EventHandler?)typeof(FileLogger)
                .GetField("_processExitHandler", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(logger);

        private static int GetWriterCount(FileLogger logger)
        {
            var writers = (ConcurrentDictionary<string, StreamWriter>)typeof(FileLogger)
                .GetField("_writers", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(logger)!;
            return writers.Count;
        }

        private sealed class DisposableTestLogger : ILogger, IDisposable
        {
            public int DisposeCount { get; private set; }
            public void LogInfo(string msg) { }
            public void LogError(string msg) { }
            public void LogMetric(string metricName, string msg) { }
            public void LogSearch(string msg) { }
            public void Dispose() => DisposeCount++;
        }
    }
}
