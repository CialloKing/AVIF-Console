using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AvifEncoder
{
    /// <summary>编码器测试状态</summary>
    public class EncoderStatus
    {
        public string Name { get; set; } = "";
        public bool Available { get; set; }
        public string Note { get; set; } = "不可用";
    }

    /// <summary>环境检测结果</summary>
    public class EnvironmentCheckResult
    {
        public bool FfmpegAvailable { get; set; }
        public List<EncoderStatus> Encoders { get; set; } = new();
        public bool Ssimulacra2Available { get; set; }
        public bool ButteraugliAvailable { get; set; }
    }

    /// <summary>跨版本共享的启动环境检测器</summary>
    public static class AvifEnvironmentChecker
    {
        /// <summary>
        /// 执行自动检测：ffmpeg、AV1 编码器、高级指标外部工具。
        /// </summary>
        /// <param name="logger">可选日志接口，用于输出过程信息到 GUI 或控制台</param>
        /// <param name="tempDir">临时文件存放目录，默认为系统临时目录</param>
        /// <returns>结构化检测结果</returns>
        public static async Task<EnvironmentCheckResult> CheckEnvironmentAsync(
            ILogger? logger = null,
            string? tempDir = null)
        {
            var result = new EnvironmentCheckResult();
            string workDir = tempDir ?? Path.Combine(Path.GetTempPath(), "AvifEncoder_check");
            Directory.CreateDirectory(workDir);
            string testBmpPath = Path.Combine(workDir, "test_input.bmp");

            void Log(string msg) => logger?.LogInfo(msg);

            try
            {
                // 1. ffmpeg
                string? ffmpeg = EncoderUtils.FindExecutable("ffmpeg");
                result.FfmpegAvailable = ffmpeg != null;
                Log(ffmpeg != null ? "[OK] ffmpeg 已找到" : "[FAIL] ffmpeg 未找到，请确保在 PATH 或程序目录中");

                if (!result.FfmpegAvailable)
                    return result;

                // 2. 获取并测试编码器
                var encoders = await GetAvailableEncodersAsync();
                Log($"检测到 AV1 编码器: {string.Join(", ", encoders.Select(e => e))}");

                // 生成测试图片
                byte[] bmpBytes = CreateTestBmp();
                File.WriteAllBytes(testBmpPath, bmpBytes);

                var tasks = encoders.Select(enc => TestEncoderAsync(enc, testBmpPath, workDir));
                var encoderResults = await Task.WhenAll(tasks);
                result.Encoders = encoderResults.ToList();

                // 打印编码器结果
                Log("编码器可用性测试结果:");
                foreach (var er in result.Encoders)
                    Log($"  {(er.Available ? "[OK]" : "[FAIL]")} {er.Name,-12} {er.Note}");

                // 3. 外部高级工具
                result.Ssimulacra2Available = EncoderUtils.FindExecutable("ssimulacra2") != null;
                result.ButteraugliAvailable = EncoderUtils.FindExecutable("butteraugli_main") != null;
                Log($"外部工具: SSIMULACRA2={(result.Ssimulacra2Available ? "可用" : "不可用")}, " +
                    $"Butteraugli={(result.ButteraugliAvailable ? "可用" : "不可用")}");
            }
            catch (Exception ex)
            {
                Log($"环境检测异常: {ex.Message}");
            }
            finally
            {
                // 清理
                if (File.Exists(testBmpPath)) try { File.Delete(testBmpPath); } catch { }
                // 只删空目录
                if (Directory.Exists(workDir) && !Directory.EnumerateFileSystemEntries(workDir).Any())
                    try { Directory.Delete(workDir); } catch { }
            }
            return result;
        }

        /// <summary>从 ffmpeg -encoders 获取 AV1 编码器列表</summary>
        private static async Task<List<string>> GetAvailableEncodersAsync()
        {
            var list = new List<string>();
            try
            {
                using var p = new Process
                {
                    StartInfo = new ProcessStartInfo("ffmpeg", "-encoders")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                p.Start();
                var outTask = p.StandardOutput.ReadToEndAsync();
                var errTask = p.StandardError.ReadToEndAsync();
                await Task.WhenAll(outTask, errTask, p.WaitForExitAsync());

                string output = await outTask;
                using var reader = new StringReader(output);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.TrimStart();
                    if (trimmed.Length > 0 && trimmed[0] == 'V' && trimmed.Contains("av1", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            string name = parts[1];
                            if (!list.Contains(name))
                                list.Add(name);
                        }
                    }
                }
            }
            catch { /* 忽略 */ }
            return list;
        }

        /// <summary>测试单个编码器是否能成功输出 > 100 字节的 AVIF</summary>
        private static async Task<EncoderStatus> TestEncoderAsync(string enc, string testInput, string testDir)
        {
            bool ok = false;
            string note = "不可用";
            try
            {
                string outFile = Path.Combine(testDir, $"test_{enc}.avif");
                string qpArg = enc switch
                {
                    var e when e.StartsWith("av1_nvenc") => "-qp 30",
                    var e when e.StartsWith("av1_qsv") => "-global_quality 30",
                    var e when e.StartsWith("av1_amf") => "-qp 30",
                    var e when e.StartsWith("av1_vulkan") => "-qp 30",
                    var e when e.StartsWith("av1_vaapi") => "-global_quality 30",
                    _ => "-crf 30"
                };
                string args = $"-y -loglevel error -i \"{testInput}\" -c:v {enc} -pix_fmt yuv420p {qpArg} -frames:v 1 \"{outFile}\"";

                var psi = new ProcessStartInfo("ffmpeg", args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                if (p == null) return new EncoderStatus { Name = enc, Available = false, Note = "无法启动 ffmpeg" };
                string stderr = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();

                if (p.ExitCode == 0 && File.Exists(outFile) && new FileInfo(outFile).Length > 100)
                {
                    ok = true;
                    note = "可用";
                }
                else
                {
                    note = ParseError(stderr);
                }
                if (File.Exists(outFile)) File.Delete(outFile);
            }
            catch (Exception ex)
            {
                note = $"异常: {ex.Message}";
            }
            return new EncoderStatus { Name = enc, Available = ok, Note = note };
        }

        private static string ParseError(string stderr)
        {
            if (stderr.Contains("MFX session")) return "缺少 Intel 驱动";
            if (stderr.Contains("MFT")) return "缺少 Media Foundation 编码器";
            if (stderr.Contains("Impossible to convert")) return "格式转换失败";
            if (stderr.Contains("Function not implemented")) return "功能未实现";
            if (stderr.Contains("Invalid argument")) return "参数无效";
            if (stderr.Contains("Unknown error")) return "未知错误";
            return "不可用";
        }

        /// <summary>生成 256x256 纯红色 BMP 测试图片</summary>
        public static byte[] CreateTestBmp()
        {
            int width = 256, height = 256;
            int rowSize = ((width * 3 + 3) / 4) * 4;
            int pixelDataSize = rowSize * height;
            int fileSize = 54 + pixelDataSize;

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            bw.Write((ushort)0x4D42);
            bw.Write(fileSize);
            bw.Write(0);
            bw.Write(54);

            bw.Write(40);
            bw.Write(width);
            bw.Write(height);
            bw.Write((ushort)1);
            bw.Write((ushort)24);
            bw.Write(0);
            bw.Write(pixelDataSize);
            bw.Write(2835);
            bw.Write(2835);
            bw.Write(0);
            bw.Write(0);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bw.Write((byte)0x00); // B
                    bw.Write((byte)0x00); // G
                    bw.Write((byte)0xFF); // R
                }
                for (int p = width * 3; p < rowSize; p++)
                    bw.Write((byte)0);
            }
            bw.Flush();
            return ms.ToArray();
        }
    }
}