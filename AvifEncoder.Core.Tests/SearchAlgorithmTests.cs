using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class SearchAlgorithmTests
    {
        [TestMethod]
        public async Task BinarySearch_FindsOptimalCrf()
        {
            // 原生 SSIM 值：CRF 25→0.98, 30→0.98, 35→0.98, 40→0.90
            // 目标 0.95，最优 CRF=35
            Func<int, Task<double>> getScore = crf =>
                Task.FromResult(crf <= 35 ? 0.98 : 0.90);

            var cfg = new PresetConfig { MetricMode = "ssim", NativeTargetValue = 0.95 };

            var (bestCrf, evals) = await AvifPipeline.StandardBinarySearch(
                "test.png", 0, cfg, "yuv420p", false,
                "test", 0.95, getScore,
                CancellationToken.None, 20, 50);

            Assert.AreEqual(35, bestCrf);
            Assert.IsLessThanOrEqualTo(8, evals, $"Too many evaluations: {evals}");
        }

        [TestMethod]
        public async Task BinarySearch_AllPass_ReturnsHi()
        {
            // 全部达标 → 应返回 hi
            Func<int, Task<double>> getScore = _ =>
                Task.FromResult(0.99);

            var cfg = new PresetConfig { MetricMode = "ssim" };

            var (bestCrf, _) = await AvifPipeline.StandardBinarySearch(
                "test.png", 0, cfg, "yuv420p", false,
                "test", 0.95, getScore,
                CancellationToken.None, 10, 30);

            Assert.AreEqual(30, bestCrf);
        }

        [TestMethod]
        public async Task BinarySearch_NonePass_ReturnsNegative()
        {
            // 全不达标 → 返回 -1
            Func<int, Task<double>> getScore = _ =>
                Task.FromResult(0.50);

            var cfg = new PresetConfig { MetricMode = "ssim" };

            var (bestCrf, _) = await AvifPipeline.StandardBinarySearch(
                "test.png", 0, cfg, "yuv420p", false,
                "test", 0.95, getScore,
                CancellationToken.None, 10, 30);

            Assert.AreEqual(-1, bestCrf);
        }

        [TestMethod]
        public async Task BinarySearch_SmallRange_Exact()
        {
            // CRF 5→达标, 6→不达标 → 最优=5
            Func<int, Task<double>> getScore = crf =>
                Task.FromResult(crf == 5 ? 0.96 : 0.50);

            var cfg = new PresetConfig { MetricMode = "ssim" };

            var (bestCrf, _) = await AvifPipeline.StandardBinarySearch(
                "test.png", 0, cfg, "yuv420p", false,
                "test", 0.95, getScore,
                CancellationToken.None, 5, 6);

            Assert.AreEqual(5, bestCrf);
        }

        [TestMethod]
        public async Task BinarySearch_InvalidRange_ReturnsNegative()
        {
            // MinCRF > MaxCRF — 应在输入验证或搜索逻辑中安全降级
            Func<int, Task<double>> getScore = _ =>
                Task.FromResult(0.99);

            var cfg = new PresetConfig { MetricMode = "ssim" };

            var (bestCrf, _) = await AvifPipeline.StandardBinarySearch(
                "test.png", 0, cfg, "yuv420p", false,
                "test", 0.95, getScore,
                CancellationToken.None, 40, 20);
            // 无效区间应返回 -1 而非崩溃
            Assert.AreEqual(-1, bestCrf);
        }

        [TestMethod]
        public async Task BinarySearch_Cancellation_Throws()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Func<int, Task<double>> getScore = _ =>
                Task.FromResult(0.99);

            var cfg = new PresetConfig { MetricMode = "ssim" };

            try
            {
                await AvifPipeline.StandardBinarySearch(
                    "test.png", 0, cfg, "yuv420p", false,
                    "test", 0.95, getScore,
                    cts.Token, 20, 50);
                Assert.Fail("Should have thrown");
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }
    }
}
