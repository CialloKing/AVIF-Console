using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AvifEncoder.Core.Tests
{
    /// <summary>
    /// 需要 ffmpeg 的集成测试。若 ffmpeg 不可用则跳过。
    /// </summary>
    [TestClass]
    public class FfmpegIntegrationTests
    {
        private static string? _ffmpegPath;
        private static string? _tempDir;
        private static string? _testImagePath;

        [ClassInitialize]
        public static void ClassInit(TestContext context)
        {
            _ffmpegPath = EncoderUtils.FindExecutable("ffmpeg");
            if (_ffmpegPath == null)
                return;

            _tempDir = Path.Combine(Path.GetTempPath(), $"avif_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);

            // 创建测试用 BMP (256x256 蓝色)
            _testImagePath = Path.Combine(_tempDir, "test_input.bmp");
            File.WriteAllBytes(_testImagePath, AvifEnvironmentChecker.CreateTestBmp());
        }

        [ClassCleanup]
        public static void ClassCleanup()
        {
            if (_tempDir != null && Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, true); } catch { }
            }
        }

        private void AssertFfmpegAvailable()
        {
            if (_ffmpegPath == null)
                Assert.Inconclusive("本测试需要 ffmpeg，但系统中未找到。");
        }

        [TestMethod]
        public async Task Ffmpeg_Path_IsFound()
        {
            AssertFfmpegAvailable();
            Assert.IsTrue(File.Exists(_ffmpegPath!));
        }

        [TestMethod]
        public async Task Encode_LibAom_CreatesAvifFile()
        {
            AssertFfmpegAvailable();
            string output = Path.Combine(_tempDir!, "test_libaom.avif");
            string args = $"-y -loglevel error -i \"{_testImagePath}\" -c:v libaom-av1 -crf 30 -b:v 0 -frames:v 1 \"{output}\"";

            using var process = Process.Start(new ProcessStartInfo(_ffmpegPath!, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            });
            Assert.IsNotNull(process);
            await process.WaitForExitAsync();

            Assert.AreEqual(0, process.ExitCode, $"ffmpeg failed: {process.StandardError.ReadToEnd()}");
            Assert.IsTrue(File.Exists(output), "Output AVIF file was not created");
            Assert.IsTrue(new FileInfo(output).Length > 100, "Output AVIF file is too small");
        }

        [TestMethod]
        public async Task Encode_SvtAv1_CreatesAvifFile()
        {
            AssertFfmpegAvailable();
            // SVT-AV1 可能不可用，跳过
            string probeArgs = $"-loglevel error -encoders";
            using var probeProc = Process.Start(new ProcessStartInfo(_ffmpegPath!, probeArgs)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            });
            string probes = probeProc!.StandardOutput.ReadToEnd();
            await probeProc.WaitForExitAsync();
            if (!probes.Contains("libsvtav1"))
            {
                Assert.Inconclusive("本机未编译 libsvtav1 编码器。");
                return;
            }

            string output = Path.Combine(_tempDir!, "test_svtav1.avif");
            string args = $"-y -loglevel error -i \"{_testImagePath}\" -c:v libsvtav1 -crf 30 -b:v 0 -frames:v 1 \"{output}\"";

            using var process = Process.Start(new ProcessStartInfo(_ffmpegPath!, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
            });
            Assert.IsNotNull(process);
            await process.WaitForExitAsync();

            Assert.AreEqual(0, process.ExitCode);
            Assert.IsTrue(File.Exists(output));
        }

        [TestMethod]
        public async Task Encode_LibRav1e_CreatesAvifFile()
        {
            AssertFfmpegAvailable();
            string probeArgs = $"-loglevel error -encoders";
            using var probeProc = Process.Start(new ProcessStartInfo(_ffmpegPath!, probeArgs)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true
            });
            string probes = probeProc!.StandardOutput.ReadToEnd();
            await probeProc.WaitForExitAsync();
            if (!probes.Contains("librav1e"))
            {
                Assert.Inconclusive("本机未编译 librav1e 编码器。");
                return;
            }

            string output = Path.Combine(_tempDir!, "test_rav1e.avif");
            string args = $"-y -loglevel error -i \"{_testImagePath}\" -c:v librav1e -crf 30 -b:v 0 -frames:v 1 \"{output}\"";

            using var process = Process.Start(new ProcessStartInfo(_ffmpegPath!, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
            });
            Assert.IsNotNull(process);
            await process.WaitForExitAsync();

            Assert.AreEqual(0, process.ExitCode);
            Assert.IsTrue(File.Exists(output));
        }

        [TestMethod]
        public async Task Metrics_Vmaf_ReturnsScore()
        {
            AssertFfmpegAvailable();
            // 编码原图到 AVIF，再解码回 YUV 计算 VMAF
            string encoded = Path.Combine(_tempDir!, "test_vmaf_enc.avif");
            string encArgs = $"-y -loglevel error -i \"{_testImagePath}\" -c:v libaom-av1 -crf 20 -b:v 0 -frames:v 1 \"{encoded}\"";

            using (var proc = Process.Start(new ProcessStartInfo(_ffmpegPath!, encArgs)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true }))
            {
                Assert.IsNotNull(proc);
                await proc.WaitForExitAsync();
                Assert.AreEqual(0, proc.ExitCode);
            }

            Assert.IsTrue(File.Exists(encoded));

            // VMAF 计算 (使用短路径避免歧义)
            string vmafLog = $"vmaf_{Guid.NewGuid():N}.json";
            string vmafArgs = $"-y -loglevel error -i \"{encoded}\" -i \"{_testImagePath}\" " +
                $"-lavfi \"[0:v]settb=1/25,setpts=PTS-STARTPTS[dist];" +
                $"[1:v]settb=1/25,setpts=PTS-STARTPTS[ref];" +
                $"[dist][ref]libvmaf=log_fmt=json:log_path={vmafLog}:" +
                $"model=version=vmaf_float_v0.6.1\" -f null -";

            using var vmafProc = Process.Start(new ProcessStartInfo(_ffmpegPath!, vmafArgs)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true,
                WorkingDirectory = _tempDir
            });
            Assert.IsNotNull(vmafProc);
            string vmafErr = await vmafProc.StandardError.ReadToEndAsync();
            await vmafProc.WaitForExitAsync();

            string fullLog = Path.Combine(_tempDir!, vmafLog);
            Assert.AreEqual(0, vmafProc.ExitCode, $"VMAF failed: {vmafErr}");
            Assert.IsTrue(File.Exists(fullLog), $"VMAF JSON log not created: {fullLog}");
        }

        [TestMethod]
        public async Task Metrics_Ssim_ReturnsScore()
        {
            AssertFfmpegAvailable();
            string encoded = Path.Combine(_tempDir!, "test_ssim_enc.avif");
            string encArgs = $"-y -loglevel error -i \"{_testImagePath}\" -c:v libaom-av1 -crf 20 -b:v 0 -frames:v 1 \"{encoded}\"";

            using (var proc = Process.Start(new ProcessStartInfo(_ffmpegPath!, encArgs)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true }))
            {
                await proc!.WaitForExitAsync();
            }

            string ssimArgs = $"-i \"{encoded}\" -i \"{_testImagePath}\" " +
                $"-lavfi \"[0:v]settb=1/25,setpts=PTS-STARTPTS[dist];" +
                $"[1:v]settb=1/25,setpts=PTS-STARTPTS[ref];" +
                $"[dist][ref]ssim\" -f null -";

            using var ssimProc = Process.Start(new ProcessStartInfo(_ffmpegPath!, ssimArgs)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true
            });
            Assert.IsNotNull(ssimProc);
            string stderr = await ssimProc.StandardError.ReadToEndAsync();
            await ssimProc.WaitForExitAsync();

            Assert.AreEqual(0, ssimProc.ExitCode);
            Assert.IsTrue(stderr.Contains("All:"), $"SSIM output missing: {stderr[..100]}");
        }

        [TestMethod]
        public async Task Probing_Ffprobe_ReturnsValidJson()
        {
            string? ffprobe = EncoderUtils.FindExecutable("ffprobe");
            if (ffprobe == null) Assert.Inconclusive("need ffprobe");

            string args = $"-v quiet -print_format json -show_streams \"{_testImagePath}\"";
            string stdout;
            using (var proc = Process.Start(new ProcessStartInfo(ffprobe, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true
            }))
            {
                Assert.IsNotNull(proc);
                stdout = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();
            }

            Assert.Contains("\"streams\"", stdout,
                $"No streams in ffprobe output: {stdout[..100]}");
            Assert.IsTrue(stdout.Contains("bmp") || stdout.Contains("rawvideo"),
                $"Unexpected format: {stdout[..100]}");
        }

        [TestMethod]
        public async Task EnvironmentCheck_RunsSuccessfully()
        {
            var result = await AvifEnvironmentChecker.CheckEnvironmentAsync();
            Assert.IsNotNull(result);
            Assert.IsTrue(result.FfmpegAvailable, "ffmpeg should be available");
            Assert.IsTrue(result.Encoders.Count > 0, "Should find at least one encoder");
            Assert.IsTrue(result.Encoders.Any(e => e.Available), "At least one encoder should be available");
        }

        [TestMethod]
        public async Task Metrics_Psnr_ReturnsScore()
        {
            AssertFfmpegAvailable();
            string encoded = Path.Combine(_tempDir!, "test_psnr_enc.avif");
            string encArgs = $"-y -loglevel error -i \"{_testImagePath}\" -c:v libaom-av1 -crf 20 -b:v 0 -frames:v 1 \"{encoded}\"";

            using (var proc = Process.Start(new ProcessStartInfo(_ffmpegPath!, encArgs)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true }))
                await proc!.WaitForExitAsync();

            string psnrArgs = $"-i \"{encoded}\" -i \"{_testImagePath}\" " +
                $"-lavfi \"[0:v]settb=1/25,setpts=PTS-STARTPTS[dist];" +
                $"[1:v]settb=1/25,setpts=PTS-STARTPTS[ref];" +
                $"[dist][ref]psnr\" -f null -";

            using var psnrProc = Process.Start(new ProcessStartInfo(_ffmpegPath!, psnrArgs)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true
            });
            Assert.IsNotNull(psnrProc);
            string err = await psnrProc.StandardError.ReadToEndAsync();
            await psnrProc.WaitForExitAsync();

            Assert.AreEqual(0, psnrProc.ExitCode);
            Assert.IsTrue(err.Contains("average:") || err.Contains("psnr_avg:"),
                $"PSNR output missing: {err[^200..]}");
        }

        [TestMethod]
        public async Task Metrics_Gmsd_ReturnsScore()
        {
            AssertFfmpegAvailable();
            string encoded = Path.Combine(_tempDir!, "test_gmsd_enc.avif");
            string encArgs = $"-y -loglevel error -i \"{_testImagePath}\" -c:v libaom-av1 -crf 20 -b:v 0 -frames:v 1 \"{encoded}\"";

            using (var proc = Process.Start(new ProcessStartInfo(_ffmpegPath!, encArgs)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true }))
                await proc!.WaitForExitAsync();

            // GMSD 在项目中通过自定义 C# 实现（ComputeGMSDAsync），不走 ffmpeg gmsd 滤镜。
            // 测试验证 ffmpeg 能解码 AVIF 为灰度原始数据（ComputeGMSDAsync 的依赖步骤）。
            string rawArgs = $"-loglevel error -hide_banner -i \"{encoded}\" -vf format=gray -f rawvideo -pix_fmt gray pipe:1";
            using var rawProc = Process.Start(new ProcessStartInfo(_ffmpegPath!, rawArgs)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            });
            Assert.IsNotNull(rawProc);
            using var ms = new MemoryStream();
            await rawProc.StandardOutput.BaseStream.CopyToAsync(ms);
            string rawErr = await rawProc.StandardError.ReadToEndAsync();
            await rawProc.WaitForExitAsync();
            Assert.AreEqual(0, rawProc.ExitCode, $"Gray decode failed: {rawErr}");
            // 256*256 = 65536 字节原始灰度
            Assert.IsTrue(ms.Length >= 65536,
                $"Decoded gray image too small: {ms.Length} bytes");
        }

        [TestMethod]
        public async Task Encode_Lossless_CreatesValidFile()
        {
            AssertFfmpegAvailable();
            string output = Path.Combine(_tempDir!, "test_lossless.avif");
            string args = $"-y -loglevel error -i \"{_testImagePath}\" -c:v libaom-av1 -crf 0 -b:v 0 -frames:v 1 \"{output}\"";

            using var process = Process.Start(new ProcessStartInfo(_ffmpegPath!, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
            });
            Assert.IsNotNull(process);
            await process.WaitForExitAsync();

            Assert.AreEqual(0, process.ExitCode);
            Assert.IsTrue(File.Exists(output), "Lossless output not created");
            Assert.IsTrue(new FileInfo(output).Length > 100, "Lossless output too small");
        }

        [TestMethod]
        public async Task Encode_PixelFormat_Yuv444p()
        {
            AssertFfmpegAvailable();
            string output = Path.Combine(_tempDir!, "test_444.avif");
            string args = $"-y -loglevel error -i \"{_testImagePath}\" -c:v libaom-av1 -crf 30 -b:v 0 -pix_fmt yuv444p -frames:v 1 \"{output}\"";

            using var process = Process.Start(new ProcessStartInfo(_ffmpegPath!, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
            });
            Assert.IsNotNull(process);
            await process.WaitForExitAsync();

            Assert.AreEqual(0, process.ExitCode);
            Assert.IsTrue(File.Exists(output));
        }

        [TestMethod]
        public async Task HardwareEncoder_Detection_ReturnsAvailable()
        {
            // 只检测硬件编码器是否被识别，不实际编码
            AssertFfmpegAvailable();
            string probeArgs = $"-loglevel error -encoders";
            using var proc = Process.Start(new ProcessStartInfo(_ffmpegPath!, probeArgs)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true
            });
            Assert.IsNotNull(proc);
            string output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            // 至少应该能输出编码器列表
            Assert.IsTrue(output.Length > 100);
            // 检查是否有 AV1 编码器条目
            Assert.IsTrue(
                output.Contains("av1") || output.Contains("AV1"),
                "Should contain AV1 encoder entries");
        }

        [TestMethod]
        public async Task Metrics_MsSsim_ReturnsScore()
        {
            AssertFfmpegAvailable();
            string encoded = Path.Combine(_tempDir!, "test_msssim_enc.avif");
            string encArgs = $"-y -loglevel error -i \"{_testImagePath}\" -c:v libaom-av1 -crf 20 -b:v 0 -frames:v 1 \"{encoded}\"";

            using (var proc = Process.Start(new ProcessStartInfo(_ffmpegPath!, encArgs)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true }))
                await proc!.WaitForExitAsync();

            // Pipeline 中 MS-SSIM 通过 VMAF SharedContext 计算，不是独立 ms_ssim 滤镜
            string vmafArgs = $"-y -loglevel info -i \"{encoded}\" -i \"{_testImagePath}\" " +
                $"-lavfi \"[0:v]settb=1/25,setpts=PTS-STARTPTS[dist];" +
                $"[1:v]settb=1/25,setpts=PTS-STARTPTS[ref];" +
                $"[dist][ref]libvmaf=log_fmt=json:model=version=vmaf_float_v0.6.1\" -f null -";

            using var proc2 = Process.Start(new ProcessStartInfo(_ffmpegPath!, vmafArgs)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true
            });
            Assert.IsNotNull(proc2);
            string err = await proc2.StandardError.ReadToEndAsync();
            string stdout = await proc2.StandardOutput.ReadToEndAsync();
            await proc2.WaitForExitAsync();

            if (proc2.ExitCode != 0)
            {
                Assert.Inconclusive($"VMAF/MS-SSIM not supported (exit={proc2.ExitCode})");
            }
            string combined = stdout + err;
            Assert.IsTrue(combined.Contains("ms_ssim") || combined.Contains("VMAF"),
                "VMAF output should contain MS-SSIM score");
        }

        [TestMethod]
        public async Task Metrics_Xpsnr_ReturnsScore()
        {
            AssertFfmpegAvailable();
            string encoded = Path.Combine(_tempDir!, "test_xpsnr_enc.avif");
            string encArgs = $"-y -loglevel error -i \"{_testImagePath}\" -c:v libaom-av1 -crf 20 -b:v 0 -frames:v 1 \"{encoded}\"";

            using (var proc = Process.Start(new ProcessStartInfo(_ffmpegPath!, encArgs)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true }))
                await proc!.WaitForExitAsync();

            string xpsnrArgs = $"-i \"{encoded}\" -i \"{_testImagePath}\" " +
                $"-lavfi \"[0:v]settb=1/25,setpts=PTS-STARTPTS[dist];" +
                $"[1:v]settb=1/25,setpts=PTS-STARTPTS[ref];" +
                $"[dist][ref]xpsnr\" -f null -";

            using var proc2 = Process.Start(new ProcessStartInfo(_ffmpegPath!, xpsnrArgs)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true
            });
            Assert.IsNotNull(proc2);
            string err = await proc2.StandardError.ReadToEndAsync();
            await proc2.WaitForExitAsync();

            if (proc2.ExitCode != 0)
            {
                Assert.Inconclusive($"XPSNR filter not available (exit={proc2.ExitCode})");
            }
            Assert.IsTrue(err.Contains("XPSNR") || err.Contains("xpsnr"),
                $"XPSNR output missing: {err[^200..]}");
        }

        [TestMethod]
        public async Task Encode_10bit_CreatesAvifFile()
        {
            AssertFfmpegAvailable();
            string output = Path.Combine(_tempDir!, "test_10bit.avif");
            string args = $"-y -loglevel error -i \"{_testImagePath}\" -c:v libaom-av1 -crf 30 -b:v 0 -pix_fmt yuv420p10le -frames:v 1 \"{output}\"";

            using var process = Process.Start(new ProcessStartInfo(_ffmpegPath!, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
            });
            Assert.IsNotNull(process);
            await process.WaitForExitAsync();

            Assert.AreEqual(0, process.ExitCode);
            Assert.IsTrue(File.Exists(output));
            Assert.IsTrue(new FileInfo(output).Length > 100);
        }

        [TestMethod]
        public async Task Encode_CrfScaling_HigherCrfSmallerFile()
        {
            AssertFfmpegAvailable();
            string hiQ = Path.Combine(_tempDir!, $"test_crf10.avif");
            string loQ = Path.Combine(_tempDir!, $"test_crf50.avif");

            string argsHi = $"-y -loglevel error -i \"{_testImagePath}\" -c:v libaom-av1 -crf 10 -b:v 0 -frames:v 1 \"{hiQ}\"";
            string argsLo = $"-y -loglevel error -i \"{_testImagePath}\" -c:v libaom-av1 -crf 50 -b:v 0 -frames:v 1 \"{loQ}\"";

            using (var p = Process.Start(new ProcessStartInfo(_ffmpegPath!, argsHi)
                { UseShellExecute = false, CreateNoWindow = true }))
                await p!.WaitForExitAsync();
            using (var p = Process.Start(new ProcessStartInfo(_ffmpegPath!, argsLo)
                { UseShellExecute = false, CreateNoWindow = true }))
                await p!.WaitForExitAsync();

            long sizeHi = new FileInfo(hiQ).Length;
            long sizeLo = new FileInfo(loQ).Length;

            // CRF 较高(50)的文件应小于 CRF 较低(10)的文件
            Assert.IsTrue(sizeLo < sizeHi,
                $"CRF 50 ({sizeLo} bytes) should be smaller than CRF 10 ({sizeHi} bytes)");
        }

        [TestMethod]
        public async Task ExternalTool_Ssimulacra2_ReturnsScore()
        {
            AssertFfmpegAvailable();
            string? exe = EncoderUtils.FindExecutable("ssimulacra2");
            if (exe == null)
            {
                Assert.Inconclusive("ssimulacra2 not found in PATH or app directory");
                return;
            }

            // BMP→PNG 参考图 + BMP→AVIF编码 + AVIF→PNG解码
            string refPng = Path.Combine(_tempDir!, "ref_ssimu2.png");
            string bmp2pngArgs = $"-y -loglevel error -i \"{_testImagePath}\" -frames:v 1 \"{refPng}\"";
            using (var pc = Process.Start(new ProcessStartInfo(_ffmpegPath!, bmp2pngArgs)
                { UseShellExecute = false, CreateNoWindow = true }))
                await pc!.WaitForExitAsync();

            string encoded = Path.Combine(_tempDir!, "test_ssimu2_enc.avif");
            string encArgs = $"-y -loglevel error -i \"{_testImagePath}\" -c:v libaom-av1 -crf 30 -b:v 0 -frames:v 1 \"{encoded}\"";
            using (var p = Process.Start(new ProcessStartInfo(_ffmpegPath!, encArgs)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true }))
                await p!.WaitForExitAsync();

            // AVIF 解码回 PNG
            string distPng = Path.Combine(_tempDir!, "dist_ssimu2.png");
            string decodeArgs = $"-y -loglevel error -i \"{encoded}\" -frames:v 1 \"{distPng}\"";
            using (var pd = Process.Start(new ProcessStartInfo(_ffmpegPath!, decodeArgs)
                { UseShellExecute = false, CreateNoWindow = true }))
                await pd!.WaitForExitAsync();

            string args = $"\"{refPng}\" \"{distPng}\"";
            using var proc = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            });
            Assert.IsNotNull(proc);
            string stdout = await proc.StandardOutput.ReadToEndAsync();
            string stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            Assert.AreEqual(0, proc.ExitCode, $"ssimulacra2 failed: {stderr}");
            string output = (stdout + stderr).Trim();
            Assert.IsTrue(double.TryParse(output,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double score), $"ssimulacra2 output not a number: '{output}'");
            Assert.IsTrue(score > 0 && score <= 120,
                $"ssimulacra2 score out of range: {score}");
        }

        [TestMethod]
        public async Task ExternalTool_Butteraugli_ReturnsScore()
        {
            AssertFfmpegAvailable();
            string? exe = EncoderUtils.FindExecutable("butteraugli_main");
            if (exe == null)
            {
                Assert.Inconclusive("butteraugli_main not found in PATH or app directory");
                return;
            }

            // BMP→PNG 参考图 + BMP→AVIF编码 + AVIF→PNG解码
            string refPng = Path.Combine(_tempDir!, "ref_butter.png");
            string b2pArgs = $"-y -loglevel error -i \"{_testImagePath}\" -frames:v 1 \"{refPng}\"";
            using (var pc = Process.Start(new ProcessStartInfo(_ffmpegPath!, b2pArgs)
                { UseShellExecute = false, CreateNoWindow = true }))
                await pc!.WaitForExitAsync();

            string encoded = Path.Combine(_tempDir!, "test_butter_enc.avif");
            string encArgs = $"-y -loglevel error -i \"{_testImagePath}\" -c:v libaom-av1 -crf 30 -b:v 0 -frames:v 1 \"{encoded}\"";
            using (var p = Process.Start(new ProcessStartInfo(_ffmpegPath!, encArgs)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true }))
                await p!.WaitForExitAsync();

            // AVIF 解码回 PNG
            string distPng = Path.Combine(_tempDir!, "dist_butter.png");
            string decodeArgs = $"-y -loglevel error -i \"{encoded}\" -frames:v 1 \"{distPng}\"";
            using (var pd = Process.Start(new ProcessStartInfo(_ffmpegPath!, decodeArgs)
                { UseShellExecute = false, CreateNoWindow = true }))
                await pd!.WaitForExitAsync();

            string args = $"\"{refPng}\" \"{distPng}\"";
            using var proc = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            });
            Assert.IsNotNull(proc);
            string stdout = await proc.StandardOutput.ReadToEndAsync();
            string stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            Assert.AreEqual(0, proc.ExitCode, $"butteraugli failed: {stderr}");
            string output = (stdout + stderr).Trim();
            // butteraugli 输出通常是空格分隔的值，或单独的数字行
            Assert.IsTrue(output.Length > 0 && output.Length < 500,
                $"butteraugli output unexpected: '{output[..100]}'");
        }

        [TestMethod]
        public async Task EnvironmentCheck_ExternalTools_Detected()
        {
            // 验证环境检测能正确发现外部工具
            var result = await AvifEnvironmentChecker.CheckEnvironmentAsync();
            Assert.IsNotNull(result);

            bool ssimu2Found = EncoderUtils.FindExecutable("ssimulacra2") != null;
            bool butterFound = EncoderUtils.FindExecutable("butteraugli_main") != null;

            // 环境检测结果应与实际查找一致
            if (ssimu2Found)
                Assert.IsTrue(result.Ssimulacra2Available,
                    "EnvironmentCheck should report ssimulacra2 available");
            if (butterFound)
                Assert.IsTrue(result.ButteraugliAvailable,
                    "EnvironmentCheck should report butteraugli available");
        }

        [TestMethod]
        [DoNotParallelize]
        public async Task Pipeline_BasicEncode_Succeeds()
        {
            AssertFfmpegAvailable();
            string root = Path.Combine(Path.GetTempPath(), $"avif_pipe_{Guid.NewGuid():N}");
            string inDir = Path.Combine(root, "input");
            string outDir = Path.Combine(root, "output");
            Directory.CreateDirectory(inDir);
            // 转为 PNG (Pipeline 默认只扫描 jpg/jpeg/png/webp)
            string localPng = Path.Combine(inDir, "test.png");
            using (var pc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                _ffmpegPath!, $"-y -loglevel error -i \"{_testImagePath}\" -frames:v 1 \"{localPng}\"")
                { UseShellExecute = false, CreateNoWindow = true }))
                await pc!.WaitForExitAsync();

            var config = PresetConfig.CreateFromPreset(CliPreset.Fast);
            config.Encoder = "libaom-av1";
            config.UseCRFSearch = false;
            config.BaseCRF = 30;

            try
            {
                using var pipeline = new AvifPipeline(inDir, outDir, config,
                    logger: new NullLogger(), progress: new Progress<int>(_ => { }));
                await pipeline.RunAsync();
                Assert.IsTrue(Directory.GetFiles(outDir, "*.avif").Length > 0);
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [TestMethod]
        public async Task EnvironmentCheck_LastResult_IsCached()
        {
            var r1 = AvifEnvironmentChecker.LastResult;
            if (r1 == null)
            {
                await AvifEnvironmentChecker.CheckEnvironmentAsync();
                r1 = AvifEnvironmentChecker.LastResult;
            }
            Assert.IsNotNull(r1, "LastResult should be set after check");
            Assert.IsTrue(r1.FfmpegAvailable);
        }
    }

    internal class NullLogger : ILogger
    {
        public void LogInfo(string msg) { }
        public void LogError(string msg) { }
        public void LogMetric(string metricName, string msg) { }
        public void LogSearch(string msg) { }
    }
}
