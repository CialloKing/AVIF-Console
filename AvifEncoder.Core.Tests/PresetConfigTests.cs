using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class PresetConfigTests
    {
        [TestMethod]
        public void CreateFromPreset_Fast_ReturnsExpectedConfig()
        {
            var cfg = PresetConfig.CreateFromPreset(CliPreset.Fast);
            Assert.AreEqual(38, cfg.BaseCRF);
            Assert.AreEqual(0.91, cfg.TargetSSIM, 0.001);
            Assert.IsFalse(cfg.UseCRFSearch);
        }

        [TestMethod]
        public void CreateFromPreset_Balanced_ReturnsExpectedConfig()
        {
            var cfg = PresetConfig.CreateFromPreset(CliPreset.Balanced);
            Assert.AreEqual(36, cfg.BaseCRF);
            Assert.AreEqual(0.97, cfg.TargetSSIM, 0.001);
            Assert.IsTrue(cfg.UseCRFSearch);
        }

        [TestMethod]
        public void CreateFromPreset_Best_ReturnsExpectedConfig()
        {
            var cfg = PresetConfig.CreateFromPreset(CliPreset.Best);
            Assert.AreEqual(34, cfg.BaseCRF);
            Assert.AreEqual(0.985, cfg.TargetSSIM, 0.001);
        }

        [TestMethod]
        public void CreateFromPreset_Extreme_ReturnsExpectedConfig()
        {
            var cfg = PresetConfig.CreateFromPreset(CliPreset.Extreme);
            Assert.AreEqual(32, cfg.BaseCRF);
            Assert.AreEqual(0.99, cfg.TargetSSIM, 0.001);
        }

        [TestMethod]
        public void IsAdvancedMetricMode_ReturnsExpected()
        {
            Assert.IsTrue(PresetConfig.IsAdvancedMetricMode("ssimu2"));
            Assert.IsTrue(PresetConfig.IsAdvancedMetricMode("butter3"));
            Assert.IsTrue(PresetConfig.IsAdvancedMetricMode("gmsd"));
            Assert.IsFalse(PresetConfig.IsAdvancedMetricMode("vmaf"));
            Assert.IsFalse(PresetConfig.IsAdvancedMetricMode("ssim"));
        }

        [TestMethod]
        public void IsMetricLowerBetter_ReturnsExpected()
        {
            Assert.IsTrue(PresetConfig.IsMetricLowerBetter("butter3"));
            Assert.IsTrue(PresetConfig.IsMetricLowerBetter("gmsd"));
            Assert.IsFalse(PresetConfig.IsMetricLowerBetter("vmaf"));
            Assert.IsFalse(PresetConfig.IsMetricLowerBetter("ssim"));
        }
    }
}
