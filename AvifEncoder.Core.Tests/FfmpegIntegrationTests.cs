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
}
