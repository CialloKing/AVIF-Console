using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace AvifEncoder
{
    public static class Logger
    {
        private static volatile ILogger? _instance;
        private static readonly object _instanceLock = new();

        /// <summary>初始化默认文件日志器（控制台/批处理场景）</summary>
        public static void Init(string outputDir)
        {
            SetInstance(new FileLogger(outputDir));
        }

        /// <summary>注入自定义日志器（如 GuiLogger）</summary>
        public static void SetInstance(ILogger logger)
        {
            ILogger? previous;
            lock (_instanceLock)
            {
                previous = _instance;
                _instance = logger;
            }
            if (!ReferenceEquals(previous, logger) && previous is IDisposable disposable)
                disposable.Dispose();
        }

        // 静态方法全部委托给 ILogger 实例
        public static void Log(string msg) => _instance?.LogInfo(msg);
        public static void SSIM(string input, int crf, double ssim)
            => _instance?.LogMetric("ssim", $"{input} | CRF={crf} | SSIM={ssim}");
        public static void CRF(string msg) => _instance?.LogMetric("crf", msg);
        public static void Error(string msg) => _instance?.LogError(msg);
        public static void Search(string msg) => _instance?.LogSearch(msg);
    }

    /// <summary>日志接口，解耦具体日志实现</summary>
    public interface ILogger
    {
        void LogInfo(string msg);
        void LogError(string msg);
        void LogMetric(string metricName, string msg);
        void LogSearch(string msg);   // 新增：搜索阶段专用日志
    }

    /// <summary>基于文件的日志实现，使用 StreamWriter 保持文件句柄避免重复打开。</summary>
    public class FileLogger : ILogger, IDisposable
    {
        private readonly object _lock = new();
        private readonly string _logDir;
        private readonly ConcurrentDictionary<string, StreamWriter> _writers = new();
        private EventHandler? _processExitHandler;
        private int _disposed;

        public FileLogger(string outputDir, PresetConfig.IFileSystem? fileSystem = null)
        {
            _logDir = Path.Combine(outputDir, "log");
            Directory.CreateDirectory(_logDir);

            // 清理30天前的 run 日志
            try
            {
                var cutoff = DateTime.Now.AddDays(-30);
                foreach (var f in Directory.GetFiles(_logDir, "run_*.log"))
                {
                    if (File.GetCreationTime(f) < cutoff)
                        File.Delete(f);
                }
            }
            catch { }

            LogInfo("===== NEW SESSION START =====");
            LogInfo($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            // ★ 进程退出时确保释放所有 StreamWriter
            _processExitHandler = (_, _) => Dispose();
            AppDomain.CurrentDomain.ProcessExit += _processExitHandler;
        }

        private StreamWriter GetWriter(string fileName)
        {
            if (!_writers.TryGetValue(fileName, out var writer))
            {
                string path = Path.Combine(_logDir, fileName);
                var stream = new FileStream(
                    path,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite);
                writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                _writers[fileName] = writer;
            }
            return writer;
        }

        public void LogInfo(string msg)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            lock (_lock)
            {
                if (_disposed != 0) return;
                GetWriter($"run_{DateTime.Now:yyyy-MM-dd}.log")
                    .WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
            }
        }

        public void LogError(string msg)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            lock (_lock)
            {
                if (_disposed != 0) return;
                string line = $"[{DateTime.Now:HH:mm:ss}] [ERROR] {msg}";
                GetWriter($"run_{DateTime.Now:yyyy-MM-dd}.log").WriteLine(line);
                GetWriter("error.log").WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
            }
        }

        public void LogMetric(string metricName, string msg)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            string fileName = metricName.ToLower() switch
            {
                "ssim" => "ssim_trace.log",
                "crf" => "crf_search.log",
                _ => $"metric_{metricName}.log"
            };
            lock (_lock)
            {
                if (_disposed != 0) return;
                GetWriter(fileName).WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
            }
        }

        public void LogSearch(string msg)
        {
            LogMetric("crf", msg);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            if (_processExitHandler != null)
            {
                AppDomain.CurrentDomain.ProcessExit -= _processExitHandler;
                _processExitHandler = null;
            }
            lock (_lock)
            {
                foreach (var w in _writers.Values)
                    try { w.Dispose(); } catch { }
                _writers.Clear();
            }
        }
    }

    /// <summary>组合日志器，将消息广播到多个 ILogger 实例。</summary>
    public class CompositeLogger : ILogger, IDisposable
    {
        private readonly ILogger[] _loggers;
        public CompositeLogger(params ILogger[] loggers)
        {
            _loggers = loggers ?? Array.Empty<ILogger>();
        }
        public void LogInfo(string m)
        {
            foreach (var l in _loggers)
            {
                try { l.LogInfo(m); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[Logger] LogInfo 异常: {ex.Message}"); }
            }
        }
        public void LogError(string m)
        {
            foreach (var l in _loggers)
            {
                try { l.LogError(m); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[Logger] LogError 异常: {ex.Message}"); }
            }
        }
        public void LogMetric(string mt, string m)
        {
            foreach (var l in _loggers)
            {
                try { l.LogMetric(mt, m); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[Logger] LogMetric 异常: {ex.Message}"); }
            }
        }
        public void LogSearch(string m)
        {
            foreach (var l in _loggers)
            {
                try { l.LogSearch(m); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[Logger] LogSearch 异常: {ex.Message}"); }
            }
        }

        public void Dispose()
        {
            foreach (var l in _loggers)
            {
                if (l is IDisposable disposable)
                {
                    try { disposable.Dispose(); } catch { }
                }
            }
        }
    }
}
