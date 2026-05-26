using System;
using System.IO;

namespace AvifEncoder
{
    public static class Logger
    {
        private static ILogger? _instance;

        /// <summary>初始化默认文件日志器（控制台/批处理场景）</summary>
        public static void Init(string outputDir)
        {
            _instance = new FileLogger(outputDir);
        }

        /// <summary>注入自定义日志器（如 GuiLogger）</summary>
        public static void SetInstance(ILogger logger)
        {
            _instance = logger;
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

    /// <summary>基于文件的日志实现，兼容原 Logger 行为</summary>
    public class FileLogger : ILogger
    {
        private readonly object _lock = new();
        private readonly string _logDir;
        private readonly PresetConfig.IFileSystem _fs;   // 改为完整限定名


        public FileLogger(string outputDir, PresetConfig.IFileSystem? fileSystem = null)  // 改为完整限定名
        {
            _fs = fileSystem ?? new PresetConfig.RealFileSystem();
            _logDir = Path.Combine(outputDir, "log");
            _fs.CreateDirectory(_logDir);

            // 清理30天前的 run 日志（原有逻辑不变）
            try
            {
                var cutoff = DateTime.Now.AddDays(-30);
                foreach (var f in _fs.GetFiles(_logDir, "run_*.log"))
                {
                    if (_fs.GetCreationTime(f) < cutoff)
                        _fs.DeleteFile(f);
                }
            }
            catch { }

            LogInfo("===== NEW SESSION START =====");
            LogInfo($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        public void LogInfo(string msg)
        {
            lock (_lock)
                _fs.AppendAllText(
                    Path.Combine(_logDir, $"run_{DateTime.Now:yyyy-MM-dd}.log"),
                    $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        }

        public void LogError(string msg)
        {
            lock (_lock)
            {
                // 错误日志同时写入 run 日志和 error.log
                _fs.AppendAllText(
                    Path.Combine(_logDir, $"run_{DateTime.Now:yyyy-MM-dd}.log"),
                    $"[{DateTime.Now:HH:mm:ss}] [ERROR] {msg}\n");
                _fs.AppendAllText(
                    Path.Combine(_logDir, "error.log"),
                    $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            }
        }

        public void LogMetric(string metricName, string msg)
        {
            string fileName = metricName.ToLower() switch
            {
                "ssim" => "ssim_trace.log",
                "crf" => "crf_search.log",
                _ => $"metric_{metricName}.log"
            };

            lock (_lock)
                _fs.AppendAllText(
                    Path.Combine(_logDir, fileName),
                    $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        }

        // 搜索专用日志：写入 crf_search.log
        public void LogSearch(string msg)
        {
            LogMetric("crf", msg);
        }
    }

    /// <summary>组合日志器，将消息广播到多个 ILogger 实例。</summary>
    public class CompositeLogger : ILogger
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
                l.LogInfo(m);
            }
        }
        public void LogError(string m)
        {
            foreach (var l in _loggers)
            {
                l.LogError(m);
            }
        }
        public void LogMetric(string mt, string m)
        {
            foreach (var l in _loggers)
            {
                l.LogMetric(mt, m);
            }
        }
        public void LogSearch(string m)
        {
            foreach (var l in _loggers)
            {
                l.LogSearch(m);
            }
        }
    }
}