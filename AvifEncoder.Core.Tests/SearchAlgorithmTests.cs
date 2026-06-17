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

        [TestMethod]
        public async Task BinarySearch_NaN_SkipsAndContinues()
        {
            int callCount = 0;
            Func<int, Task<double>> getScore = crf =>
            {
                callCount++;
                return Task.FromResult(crf == 30 ? double.NaN : 0.95);
            };

            var cfg = new PresetConfig { MetricMode = "ssim", NativeTargetValue = 0.95 };
            var (bestCrf, _) = await AvifPipeline.StandardBinarySearch(
                "test.png", 0, cfg, "yuv420p", false,
                "test", 0.95, getScore,
                CancellationToken.None, 20, 50);

            Assert.IsTrue(callCount > 0, "Search should have been performed");
        }

        [TestMethod]
        public async Task BinarySearch_LowerIsBetter_WithNegationWrapper()
        {
            // ★ 复现 HybridSearchCRFAsync:151-161 的取反包装逻辑
            //   越小越好指标（butter3/gmsd）通过取反映射到 >= 比较
            double butterTarget = 2.0;
            bool lowerIsBetter = true;

            Func<int, Task<double>> rawGetScore = crf =>
            {
                // 模拟：CRF 越低质量越好 → butteraugli 值越小
                double score = crf switch
                {
                    <= 24 => 0.5,   // 远超目标
                    25 => 1.0,
                    26 => 1.3,
                    27 => 1.6,
                    28 => 1.8,
                    29 => 1.95,  // 刚好达标 (< 2.0)
                    30 => 2.05,  // 不达标
                    _ => 3.0
                };
                return Task.FromResult(score);
            };

            // 取反包装（与 HybridSearchCRFAsync 逻辑一致）
            Func<int, Task<double>> getScore;
            double searchTarget;
            if (lowerIsBetter)
            {
                var original = rawGetScore;
                getScore = async crf =>
                {
                    double s = await original(crf);
                    return AvifPipeline.NormalizeLowerIsBetterSearchScore(s);
                };
                searchTarget = -butterTarget;  // 2.0 → -2.0
            }
            else
            {
                getScore = rawGetScore;
                searchTarget = butterTarget;
            }

            var cfg = new PresetConfig { MetricMode = "butter3", NativeTargetValue = butterTarget };
            var (bestCrf, evals) = await AvifPipeline.StandardBinarySearch(
                "test.png", 0, cfg, "yuv420p", false,
                "test", searchTarget, getScore,
                CancellationToken.None, 20, 40, lowerIsBetter: true);

            // 达标 CRF≈29（score=1.95≤2.0），搜索应找到 29 附近
            Assert.IsTrue(bestCrf >= 27 && bestCrf <= 31,
                $"Expected CRF near 29 for lower-is-better butterTarget=2.0, got {bestCrf} in {evals} evals");
        }

        [TestMethod]
        public async Task BinarySearch_LowerIsBetter_FailedScoreIsNotTreatedAsPass()
        {
            double butterTarget = 2.0;
            Func<int, Task<double>> rawGetScore = crf =>
            {
                double score = crf switch
                {
                    <= 29 => 1.8,  // 达标
                    30 => 2.2,     // 不达标
                    35 => -1,      // 评估失败，旧逻辑取反前保留 -1，会被误判为 >= -2
                    _ => 3.0
                };
                return Task.FromResult(score);
            };

            Func<int, Task<double>> getScore = async crf =>
                AvifPipeline.NormalizeLowerIsBetterSearchScore(await rawGetScore(crf));

            var cfg = new PresetConfig { MetricMode = "butter3", NativeTargetValue = butterTarget };
            var (bestCrf, _) = await AvifPipeline.StandardBinarySearch(
                "test.png", 0, cfg, "yuv420p", false,
                "test", -butterTarget, getScore,
                CancellationToken.None, 20, 40, lowerIsBetter: true);

            Assert.AreEqual(29, bestCrf);
        }

        [TestMethod]
        public async Task BinarySearch_NaN_SplitsAndContinuesSearch()
        {
            int callCount = 0;
            Func<int, Task<double>> getScore = crf =>
            {
                callCount++;
                return Task.FromResult(crf == 30 ? double.NaN : (crf <= 28 ? 0.96 : 0.90));
            };

            var cfg = new PresetConfig { MetricMode = "ssim", NativeTargetValue = 0.95 };
            var (bestCrf, _) = await AvifPipeline.StandardBinarySearch(
                "test.png", 0, cfg, "yuv420p", false,
                "test", 0.95, getScore,
                CancellationToken.None, 20, 40);

            Assert.AreEqual(28, bestCrf);
            Assert.IsTrue(callCount >= 3, $"Search should continue after NaN, calls={callCount}");
        }
    }
}
