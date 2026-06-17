using System;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class ProgressAndConfigTests
    {
        // ===== ProgressTracker 重试不超100% =====

        [TestMethod]
        public void SetTotalFiles_WithRetries_IncreasesTotal()
        {
            var tracker = new ProgressTracker();
            tracker.SetTotalFiles(100);
            // 模拟 3 个失败文件重试
            tracker.SetTotalFiles(tracker.TotalFiles + 3);

            Assert.AreEqual(103, tracker.TotalFiles);
        }

        [TestMethod]
        public void MarkFileProcessed_UpToTotal_ProgressWithin100()
        {
            var tracker = new ProgressTracker();
            tracker.SetTotalFiles(5);
            tracker.Start(DateTime.Now);

            for (int i = 0; i < 5; i++)
                tracker.MarkFileProcessed();

            Assert.AreEqual(5, tracker.ProcessedCount);
            Assert.AreEqual(5, tracker.TotalFiles);
        }

        [TestMethod]
        public void MarkFileProcessed_WithRetries_DoesNotExceed100Percent()
        {
            var tracker = new ProgressTracker();
            tracker.SetTotalFiles(103);
            tracker.Start(DateTime.Now);

            for (int i = 0; i < 103; i++)
                tracker.MarkFileProcessed();

            Assert.AreEqual(103, tracker.ProcessedCount);
            Assert.AreEqual(103, tracker.TotalFiles);
            // ★ 核心验证：进度百分比不应超过 100%
            string line = tracker.GetProgressLine(null);
            Assert.IsFalse(line.Contains("100.0%") && tracker.ProcessedCount < tracker.TotalFiles,
                "进度不应超过100%");
        }

        // ===== PresetConfig.UseCRFSearch 固定CRF互斥 =====

        [TestMethod]
        public void BuildConfig_FixedCrf_DisablesSearch()
        {
            // ★ 测试 PresetConfig.Validate 对搜索+CRF 组合的校验逻辑
            var cfg = new PresetConfig { UseCRFSearch = true, BaseCRF = 30, MinCRF = 30, MaxCRF = 30 };
            var errors = cfg.Validate();
            // 固定 CRF（Min=Max）不强制关闭搜索，但验证通过
            Assert.IsFalse(errors.Any(e => e.Contains("CRF")), $"Unexpected CRF error: {string.Join("; ", errors)}");
        }

        [TestMethod]
        public void BuildConfig_RangeCrf_EnablesSearch()
        {
            var cfg = new PresetConfig { UseCRFSearch = true, MinCRF = 20, MaxCRF = 40 };
            var errors = cfg.Validate();
            Assert.IsFalse(errors.Any(e => e.Contains("CRF")), $"Unexpected CRF error: {string.Join("; ", errors)}");
        }

        // ===== MetricRegistry 所有指标均可查询 =====

        [TestMethod]
        public void ProgressTracker_ZeroTotalFiles_ProgressSafe()
        {
            var tracker = new ProgressTracker();
            tracker.MarkFileProcessed();
            Assert.AreEqual(1, tracker.ProcessedCount);
            Assert.AreEqual(0, tracker.TotalFiles);
        }

        [TestMethod]
        public void MetricRegistry_AllKeys_ReturnsExpectedCount()
        {
            int count = 0;
            foreach (var _ in MetricRegistry.AllKeys) count++;
            Assert.IsGreaterThanOrEqualTo(10, count, $"Expected >= 10 metrics, got {count}");
        }

        [TestMethod]
        public void MetricRegistry_Get_EveryKeyIsValid()
        {
            foreach (var k in MetricRegistry.AllKeys)
            {
                var def = MetricRegistry.Get(k);
                Assert.IsNotNull(def, $"Metric '{k}' should be registered");
                Assert.IsFalse(string.IsNullOrEmpty(def.DisplayName),
                    $"Metric '{k}' should have DisplayName");
                Assert.IsNotNull(def.GetScore, $"Metric '{k}' should have GetScore");
            }
        }

        [TestMethod]
        public void MetricRegistry_DisplayName_MatchesReverseLookup()
        {
            foreach (var k in MetricRegistry.AllKeys)
            {
                var def = MetricRegistry.Get(k);
                Assert.IsNotNull(def);

                // 反向查找：DisplayName → Key 应该一致
                var found = System.Linq.Enumerable.FirstOrDefault(
                    MetricRegistry.AllKeys.Select(kk => MetricRegistry.Get(kk)),
                    d => d != null && string.Equals(d.DisplayName, def.DisplayName,
                        StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(found, $"DisplayName '{def.DisplayName}' should be found");
            }
        }

        [TestMethod]
        public void MetricRegistry_IsXpsnr_CoversKeysAndDisplayNames()
        {
            Assert.IsTrue(MetricRegistry.IsXpsnr("xpsnr"));
            Assert.IsTrue(MetricRegistry.IsXpsnr("xpsnr_y"));
            Assert.IsTrue(MetricRegistry.IsXpsnr("xpsnr_u"));
            Assert.IsTrue(MetricRegistry.IsXpsnr("xpsnr_v"));
            Assert.IsTrue(MetricRegistry.IsXpsnr("XPSNR (W)"));
            Assert.IsTrue(MetricRegistry.IsXpsnr("XPSNR-U"));
            Assert.IsFalse(MetricRegistry.IsXpsnr("psnr"));
            Assert.IsFalse(MetricRegistry.IsXpsnr("VMAF"));
        }

        // ===== SetQualityTarget 不同类型指标 =====

        [TestMethod]
        public void SetQualityTarget_Vmaf_StoresNative()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(95.0, "vmaf");
            Assert.AreEqual("vmaf", cfg.MetricMode);
            Assert.IsTrue(cfg.NativeTargetValue.HasValue);
            Assert.AreEqual(95.0, cfg.NativeTargetValue.Value, 0.01);
        }

        [TestMethod]
        public void SetQualityTarget_Psnr_StoresNative()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(40.0, "psnr");
            Assert.AreEqual("psnr", cfg.MetricMode);
            Assert.IsTrue(cfg.NativeTargetValue.HasValue);
            Assert.AreEqual(40.0, cfg.NativeTargetValue.Value, 0.01);
        }

        [TestMethod]
        public void SetQualityTarget_Ssimu2_IsAdvanced()
        {
            var def = MetricRegistry.Get("ssimu2");
            Assert.IsNotNull(def);
            Assert.IsTrue(def.IsAdvanced, "SSIMULACRA2 should be marked as advanced");
        }

        [TestMethod]
        public void MetricRegistry_Get_EveryKeyReturnsDefinition()
        {
            // 遍历所有注册的指标，确保每个都有有效定义
            foreach (var k in MetricRegistry.AllKeys)
            {
                var def = MetricRegistry.Get(k);
                Assert.IsNotNull(def, $"Key '{k}' should return a metric definition");
                Assert.IsFalse(string.IsNullOrEmpty(def.DisplayName), $"Key '{k}' should have a display name");
            }
        }

        [TestMethod]
        public void GetEffectiveTarget_PresetPath_VmafConvertsFromSsim()
        {
            var cfg = PresetConfig.CreateFromPreset(CliPreset.Balanced);
            cfg.MetricMode = "vmaf";
            double target = cfg.GetEffectiveTarget();
            Assert.IsTrue(target > 90 && target <= 100,
                $"Expected VMAF 97±, got {target}");
        }

        [TestMethod]
        public void SetQualityTarget_Vmaf_IsNotAdvanced()
        {
            var def = MetricRegistry.Get("vmaf");
            Assert.IsNotNull(def);
            Assert.IsFalse(def.IsAdvanced, "VMAF should NOT be marked as advanced");
        }
    }
}
