using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AvifEncoder
{
    /// <summary>封装外部进程调用的接口，便于替换和测试</summary>
    public interface IProcessRunner
    {
        /// <summary>
        /// 运行指定的可执行文件，返回 (退出码, 标准输出, 标准错误)。
        /// </summary>
        Task<(int exitCode, string stdout, string stderr)> RunAsync(
            string fileName,
            string arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default);
    }

    /// <summary>使用真实操作系统进程的默认实现</summary>
    public class RealProcessRunner : IProcessRunner
    {
        public async Task<(int exitCode, string stdout, string stderr)> RunAsync(
            string fileName,
            string arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(fileName, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            try
            {
                await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(linkedCts.Token));
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    try { await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
                }
            }

            string stdout = await stdoutTask;
            string stderr = await stderrTask;
            return (process.ExitCode, stdout, stderr);
        }
    }





    /// <summary>封装进度统计、ETA 计算与进度行格式化</summary>
    public class ProgressTracker
    {
        private DateTime _startTime;
        private int _totalFiles;
        private int _processedCount;

        public DateTime StartTime => _startTime;
        public int ProcessedCount => Volatile.Read(ref _processedCount);
        public int TotalFiles => _totalFiles;

        public void Start(DateTime startTime) => _startTime = startTime;
        public void SetTotalFiles(int count) => _totalFiles = count;

        public void MarkFileProcessed()
        {
            Interlocked.Increment(ref _processedCount);
        }

        public string GetProgressLine(EncodeResult? r)
        {
            int done = ProcessedCount, total = TotalFiles;
            double pct = done * 100.0 / total;
            var elapsed = DateTime.Now - _startTime;
            string eta = "计算中...";
            if (done > 0 && done < total)
                eta = FormatTimeSpanLocal(TimeSpan.FromSeconds(elapsed.TotalSeconds / done * (total - done)));
            else if (done == total)
                eta = "已完成";
            string line = $"[{done}/{total} {pct,5:F4}%]";

            if (r != null)
            {
                if (r.Skipped)
                    return $"{line} [SKIP] 跳过 {r.FileName} | {r.OriginalFileName}";
                if (r.Success)
                {
                    string qualityStr = $"VMAF={r.FinalVMAF?.ToString("F4") ?? "N/A"}  PSNR-Y={r.FinalPSNR_Y?.ToString("F4") ?? "N/A"}dB  SSIM={r.FinalSSIM:F4}  MS-SSIM={r.FinalMSSSIM?.ToString("F4") ?? "N/A"}";
                    if (r.FinalWXPSNR.HasValue)
                        qualityStr += $"  W‑XPSNR={r.FinalWXPSNR.Value:F2} dB";
                    // ★ 添加高级指标
                    if (r.FinalSSIMULACRA2.HasValue)
                        qualityStr += $"  SSIMU2={r.FinalSSIMULACRA2.Value:F4}";
                    if (r.FinalButteraugli_3norm.HasValue)
                        qualityStr += $"  Butter3={r.FinalButteraugli_3norm.Value:F4}";
                    if (r.FinalGMSD.HasValue)
                        qualityStr += $"  GMSD={r.FinalGMSD.Value:F4}";
                    return $"{line} [OK] {r.FileName} | {r.OriginalFileName} | CRF:{r.UsedCRF} | " +
                           $"{FormatSizeLocal(r.OriginalSize)} -> {FormatSizeLocal(r.OutputSize)} | " +
                           $"{r.CompressionRatio:P1} | {qualityStr} | 总耗时:{r.TotalTime.TotalSeconds:F4}s | 剩余 {eta}";
                }
                return $"{line} [FAIL] 失败 | {r.OriginalFileName} | 原因:{r.ErrorMessage} | 总耗时:{r.TotalTime.TotalSeconds:F4}s | 剩余 {eta}";
            }
            return $"{line} [SKIP] 跳过";
        }

        private static string FormatSizeLocal(long b) => b switch
        {
            >= 1_048_576 => $"{b / 1_048_576.0:F2} MB",
            >= 1024 => $"{b / 1024.0:F2} KB",
            _ => $"{b} B"
        };

        private static string FormatTimeSpanLocal(TimeSpan t) => t switch
        {
            { TotalHours: >= 1 } => $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s",
            { TotalMinutes: >= 1 } => $"{(int)t.TotalMinutes}m {t.Seconds}s",
            _ => $"{t.TotalSeconds:F4}s"
        };
    }


    /// <summary>编码器类型判断与通用工具方法</summary>
    public static class EncoderUtils
    {
        public static bool IsLibAom(string encoder) =>
            encoder.StartsWith("libaom-av1", StringComparison.OrdinalIgnoreCase);

        public static bool IsSvtAv1(string encoder) =>
            encoder.StartsWith("libsvtav1", StringComparison.OrdinalIgnoreCase);

        public static bool IsRav1e(string encoder) =>
            encoder.StartsWith("librav1e", StringComparison.OrdinalIgnoreCase);

        /// <summary>是否为软件编码器（lib 开头）</summary>
        public static bool IsSoftwareEncoder(string encoder) =>
            encoder.StartsWith("lib", StringComparison.OrdinalIgnoreCase);

        /// <summary>是否支持 still-picture 参数</summary>
        public static bool SupportsStillPicture(string encoder) =>
            IsLibAom(encoder);

        /// <summary>是否支持 AOM 高级参数（目前仅 libaom-av1 支持）</summary>
        public static bool SupportsAomParams(string encoder) =>
            IsLibAom(encoder);

        /// <summary>在 PATH 环境变量中查找可执行文件</summary>
        public static string? FindExecutable(string name)
        {
            // 若 Windows 且传入名称已带 .exe，去除以避免重复追加
            bool isWindows = OperatingSystem.IsWindows();
            string cleanName = name;
            if (isWindows && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                cleanName = name[..^4];  // 去掉 .exe

            // 1. 优先在应用程序所在目录中查找
            string? appDir = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(appDir))
            {
                string localFile = Path.Combine(appDir,
                    isWindows ? $"{cleanName}.exe" : cleanName);
                if (File.Exists(localFile))
                    return localFile;
            }

            // 2. 在当前工作目录查找
            try
            {
                string cwd = Environment.CurrentDirectory;
                if (!string.IsNullOrEmpty(cwd))
                {
                    string cwdFile = Path.Combine(cwd,
                        isWindows ? $"{cleanName}.exe" : cleanName);
                    if (File.Exists(cwdFile))
                        return cwdFile;
                }
            }
            catch { /* 忽略 */ }

            // 3. 回退到 PATH
            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator);
            foreach (var p in paths ?? Array.Empty<string>())
            {
                string full = Path.Combine(p,
                    isWindows ? $"{cleanName}.exe" : cleanName);
                if (File.Exists(full))
                    return full;
            }

            return null;
        }
    }


    /// <summary>
    /// 全局 Windows Job Object，用于将子进程生命周期与主进程绑定。
    /// 主进程退出（包括崩溃、taskkill、关窗等）时，操作系统自动终止本 Job 内的所有子进程。
    /// </summary>
    internal static class JobObjectHelper
    {
        private static IntPtr hJob;
        private static readonly bool isSupported = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        static JobObjectHelper()
        {
            if (!isSupported) return;

            // 1. 创建 Job Object
            IntPtr tempJob = CreateJobObject(IntPtr.Zero, null);
            if (tempJob == IntPtr.Zero)
            {
                Trace.TraceError($"无法创建全局 Job Object。Win32 错误码: {Marshal.GetLastWin32Error()}");
                return;
            }

            // 2. 设置 JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            var basicLimits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = (uint)JOB_OBJECT_LIMIT.KILL_ON_JOB_CLOSE
                }
            };

            int size = Marshal.SizeOf(basicLimits);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(basicLimits, ptr, false);
            try
            {
                if (!SetInformationJobObject(tempJob, JOBOBJECTINFOCLASS.ExtendedLimitInformation, ptr, (uint)size))
                {
                    Trace.TraceError($"无法设置 Job Object 的 KILL_ON_JOB_CLOSE 标志。Win32 错误码: {Marshal.GetLastWin32Error()}");
                    CloseHandle(tempJob);
                    return;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            // 3. 设置成功后才赋值给静态字段
            hJob = tempJob;
            Trace.WriteLine("全局 Job Object 已创建并配置完成。");
        }

        /// <summary>
        /// 将 <paramref name="process"/> 分配至全局 Job Object。
        /// 失败时会记录错误，但不影响正常流程（仅表示无法启用额外保护）。
        /// </summary>
        public static bool AssignProcess(Process process)
        {
            if (!isSupported || hJob == IntPtr.Zero) return false;

            bool ok = AssignProcessToJobObject(hJob, process.Handle);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                Trace.TraceWarning($"AssignProcessToJobObject 失败，PID={process.Id}，Win32 错误码: {err}");
            }
            return ok;
        }

        // ---------- P/Invoke 定义 ----------
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(
            IntPtr hJob, JOBOBJECTINFOCLASS JobObjectInfoClass,
            IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [Flags]
        private enum JOB_OBJECT_LIMIT : uint
        {
            KILL_ON_JOB_CLOSE = 0x2000
        }

        private enum JOBOBJECTINFOCLASS
        {
            ExtendedLimitInformation = 9
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
    }
}
