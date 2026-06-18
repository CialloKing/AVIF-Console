using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AvifEncoder
{
    public class EncoderStatus
    {
        public string Name { get; set; } = "";
        public bool Available { get; set; }
        public string Note { get; set; } = "不可用";
    }

    public class EncoderVersionInfo
    {
        public string FfmpegVersion { get; set; } = "";
        public Dictionary<string, string> EncoderVersions { get; set; } = [];
    }

    public class EnvironmentCheckResult
    {
        public bool FfmpegAvailable { get; set; }
        public List<EncoderStatus> Encoders { get; set; } = [];
        public bool Ssimulacra2Available { get; set; }
        public bool ButteraugliAvailable { get; set; }
        public EncoderVersionInfo VersionInfo { get; set; } = new();
    }

    public static class AvifEnvironmentChecker
    {
        /// <summary>缓存最近一次检测结果，供 UI 动态获取</summary>
        public static EnvironmentCheckResult? LastResult { get; private set; }

        public static Task<EnvironmentCheckResult> CheckEnvironmentAsync(
    ILogger? logger = null,
    string? tempDir = null)
            => CheckEnvironmentAsync(logger, tempDir, EncoderUtils.FindExecutable);

        internal static async Task<EnvironmentCheckResult> CheckEnvironmentAsync(
            ILogger? logger,
            string? tempDir,
            Func<string, string?> findExecutable)
        {
            var result = new EnvironmentCheckResult();
            bool ownsWorkDir = tempDir == null;
            string workDir = tempDir ?? CreateDefaultCheckWorkDir();
            string runId = Guid.NewGuid().ToString("N");
            string testBmpPath = Path.Combine(workDir, $"test_input_{runId}.bmp");

            void Log(string msg) => logger?.LogInfo(msg);

            try
            {
                Directory.CreateDirectory(workDir);

                // 1. ffmpeg 检查
                string? ffmpeg = findExecutable("ffmpeg");
                result.FfmpegAvailable = ffmpeg != null;
                Log(ffmpeg != null ? "[OK] ffmpeg 已找到" : "[FAIL] ffmpeg 未找到，请确保在 PATH 或程序目录中");

                if (!result.FfmpegAvailable)
                    return result;

                // 1.5 获取 ffmpeg 及编码器库版本
                Log("\n正在检测 ffmpeg 及编码器库版本...");
                result.VersionInfo = await GetEncoderVersionInfoAsync(ffmpeg!);
                Log($"  ffmpeg: {result.VersionInfo.FfmpegVersion}");
                foreach (var kv in result.VersionInfo.EncoderVersions)
                {
                    Log($"  {kv.Key}: {kv.Value}");
                }

                // 2. 获取编码器列表
                Log("\n正在检测可用的 AV1 编码器...");
                var encoders = await GetAvailableEncodersAsync(ffmpeg!);
                Log($"当前 ffmpeg 支持的 AV1 编码器: {string.Join(", ", encoders)}");

                // 3. 测试编码器
                Log("\n正在测试编码器实际可用性...");
                byte[] bmpBytes = CreateTestBmp();
                File.WriteAllBytes(testBmpPath, bmpBytes);

                var tasks = encoders.Select(enc => TestEncoderAsync(enc, testBmpPath, workDir, ffmpeg!, runId));
                var encoderResults = await Task.WhenAll(tasks);
                result.Encoders = [.. encoderResults];

                // 4. 输出编码器测试结果（旧版格式）
                Log("\n编码器可用性测试结果");
                Log("----------------------------------------");

                var availableList = result.Encoders.Where(e => e.Available).ToList();
                var unavailableList = result.Encoders.Where(e => !e.Available).ToList();

                if (availableList.Any())
                {
                    Log("[可用的编码器]");
                    var softAvail = availableList.Where(e => e.Name.StartsWith("lib")).ToList();
                    var hardAvail = availableList.Where(e => !e.Name.StartsWith("lib")).ToList();

                    if (softAvail.Any())
                    {
                        Log("  -- 软件编码器（推荐） --");
                        foreach (var enc in softAvail)
                            Log($"  [OK] {enc.Name,-12}  (--encoder {enc.Name})");
                    }
                    if (hardAvail.Any())
                    {
                        Log("  -- 硬件编码器 --");
                        foreach (var enc in hardAvail)
                            Log($"  [OK] {enc.Name,-12}  (--encoder {enc.Name})");
                    }
                }

                if (unavailableList.Any())
                {
                    Log("\n[不可用的编码器]");
                    foreach (var enc in unavailableList)
                        Log($"  [FAIL] {enc.Name,-12} ({enc.Note})");
                }

                Log("----------------------------------------");
                Log("提示: 同一编码器可能因图片格式/尺寸在运行时降级或回退，属正常保护机制。");

                // 5. 外部工具检测
                Log("\n外部指标工具可用性检测");
                Log("----------------------------------------");
                result.Ssimulacra2Available = findExecutable("ssimulacra2") != null;
                result.ButteraugliAvailable = findExecutable("butteraugli_main") != null;
                Log($"  SSIMULACRA2: {(result.Ssimulacra2Available ? "[OK] 已找到" : "[FAIL] 未找到")} (ssimulacra2.exe)");
                Log($"  Butteraugli: {(result.ButteraugliAvailable ? "[OK] 已找到" : "[FAIL] 未找到")} (butteraugli_main.exe)");
                Log("\n未找到的工具无法计算相应的指标，请不要设置为目标质量");
                Log("----------------------------------------");

                if (!result.Ssimulacra2Available || !result.ButteraugliAvailable)
                {
                    Log("提示: 将 ssimulacra2.exe / butteraugli_main.exe 放到程序所在目录或 PATH 中即可使对应指标可用。");
                }
            }
            catch (Exception ex)
            {
                Log($"环境检测异常: {ex.Message}");
            }
            finally
            {
                if (File.Exists(testBmpPath)) try { File.Delete(testBmpPath); } catch { }
                if (Directory.Exists(workDir))
                {
                    try
                    {
                        if (ownsWorkDir)
                        {
                            Directory.Delete(workDir, recursive: true);
                        }
                        else if (!Directory.EnumerateFileSystemEntries(workDir).Any())
                        {
                            Directory.Delete(workDir);
                        }
                    }
                    catch { }
                }

                LastResult = result;
            }
            return result;
        }

        // ========== 以下私有方法保持不变 ==========
        internal static string CreateDefaultCheckWorkDir()
            => Path.Combine(
                Path.GetTempPath(),
                "AvifEncoder_check",
                Guid.NewGuid().ToString("N"));

        private static async Task<List<string>> GetAvailableEncodersAsync(string ffmpegPath)
        {
            var list = new List<string>();
            try
            {
                var (_, output, _) = await new RealProcessRunner().RunAsync(
                    ffmpegPath, "-encoders", TimeSpan.FromSeconds(30));
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
            catch
            {
            }
            return list;
        }

        private static async Task<EncoderStatus> TestEncoderAsync(
            string enc,
            string testInput,
            string testDir,
            string ffmpegPath,
            string runId)
        {
            bool ok = false;
            string note = "不可用";
            string outFile = Path.Combine(testDir, $"test_{enc}_{runId}.avif");
            try
            {
                string qpArg = enc switch
                {
                    var e when e.StartsWith("av1_nvenc") => "-qp 30",
                    var e when e.StartsWith("av1_qsv") => "-global_quality 30",
                    var e when e.StartsWith("av1_amf") => "-qp 30",
                    var e when e.StartsWith("av1_vulkan") => "-qp 30",
                    var e when e.StartsWith("av1_vaapi") => "-global_quality 30",
                    _ => "-crf 30"
                };
                string escInput = EncodeHelpers.EscapeArg(testInput);
                string escOutput = EncodeHelpers.EscapeArg(outFile);
                string args = $"-y -loglevel error -i \"{escInput}\" -c:v {enc} -pix_fmt yuv420p {qpArg} -frames:v 1 \"{escOutput}\"";

                try
                {
                    var (exitCode, _, stderr) = await new RealProcessRunner().RunAsync(
                        ffmpegPath, args, TimeSpan.FromSeconds(30));

                    if (exitCode == 0 && File.Exists(outFile) && new FileInfo(outFile).Length > 100)
                    {
                        ok = true;
                        note = "可用";
                    }
                    else
                    {
                        note = ParseError(stderr);
                    }
                }
                catch
                {
                    note = "超时";
                }
            }
            catch (Exception ex)
            {
                note = $"异常: {ex.Message}";
            }
            finally
            {
                if (File.Exists(outFile)) File.Delete(outFile);
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

        /// <summary> 获取 ffmpeg 及编码器库版本信息 </summary>
        public static async Task<EncoderVersionInfo> GetEncoderVersionInfoAsync(string ffmpegPath)
        {
            var info = new EncoderVersionInfo();
            try
            {
                var (_, stdout, stderr) = await new RealProcessRunner().RunAsync(
                    ffmpegPath, "-version", TimeSpan.FromSeconds(10));

                string output = stdout + stderr;

                // 提取 ffmpeg 版本（第一行）
                var ffmpegMatch = System.Text.RegularExpressions.Regex.Match(
                    output, @"^ffmpeg\s+version\s+([^\s]+)");
                if (ffmpegMatch.Success)
                {
                    info.FfmpegVersion = ffmpegMatch.Groups[1].Value;
                }

                // 提取各编码器库版本（-version 输出中的 configuration/lib 信息）
                // 常见格式: libaom-av1 3.9.1 / libsvtav1 2.3.0 / librav1e 0.7.1
                var libPatterns = new (string key, string pattern)[]
                {
                    ("libaom-av1", @"libaom[^\s]*\s+([\d\.]+)"),
                    ("libsvtav1",  @"svtav1[^\s]*\s+([\d\.]+)"),
                    ("librav1e",   @"rav1e[^\s]*\s+([\d\.]+)"),
                };

                foreach (var (key, pattern) in libPatterns)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(
                        output, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        info.EncoderVersions[key] = m.Groups[1].Value;
                    }
                }

                // 若以上未匹配到，尝试从 --enable-lib 行中提取
                if (info.EncoderVersions.Count == 0)
                {
                    var configLine = System.Text.RegularExpressions.Regex.Match(
                        output, @"configuration:\s*(.+)");
                    if (configLine.Success)
                    {
                        string config = configLine.Groups[1].Value;
                        foreach (var (key, _) in libPatterns)
                        {
                            var m2 = System.Text.RegularExpressions.Regex.Match(
                                config, $@"{key.Replace("-", @"[^\s]*")}[^\s]*\s+([\d\.]+)",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (m2.Success)
                            {
                                info.EncoderVersions[key] = m2.Groups[1].Value;
                            }
                        }
                    }
                }
            }
            catch
            {
                // 静默失败，版本信息非关键路径
            }
            return info;
        }

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
                    bw.Write((byte)0x00);
                    bw.Write((byte)0x00);
                    bw.Write((byte)0xFF);
                }
                for (int p = width * 3; p < rowSize; p++)
                    bw.Write((byte)0);
            }
            bw.Flush();
            return ms.ToArray();
        }
    }
}
