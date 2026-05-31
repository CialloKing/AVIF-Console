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

        #region Validate

        [TestMethod]
        public void Validate_DefaultConfig_ReturnsEmpty()
        {
            var cfg = PresetConfig.CreateFromPreset(CliPreset.Balanced);
            var errors = cfg.Validate();
            Assert.IsEmpty(errors);
        }

        [TestMethod]
        public void Validate_MinCrfGreaterThanMaxCrf_ReturnsError()
        {
            var cfg = new PresetConfig { MinCRF = 50, MaxCRF = 30 };
            var errors = cfg.Validate();
            Assert.IsNotEmpty(errors);
            Assert.Contains("MinCRF", errors[0]);
        }

        [TestMethod]
        public void Validate_InvalidBitDepth_ReturnsError()
        {
            var cfg = new PresetConfig { BitDepth = 12 };
            var errors = cfg.Validate();
            Assert.IsNotEmpty(errors);
            Assert.Contains("BitDepth", errors[0]);
        }

        [TestMethod]
        public void Validate_InvalidMaxJobs_ReturnsError()
        {
            var cfg = new PresetConfig { MaxJobs = -1 };
            var errors = cfg.Validate();
            Assert.IsNotEmpty(errors);
        }

        [TestMethod]
        public void Validate_NegativeSearchCpuUsed_ReturnsError()
        {
            var cfg = new PresetConfig { SearchCpuUsed = -5 };
            var errors = cfg.Validate();
            Assert.IsNotEmpty(errors);
        }

        [TestMethod]
        public void Validate_BaseCrfOutOfRange_NoSearch_ReturnsError()
        {
            var cfg = new PresetConfig { BaseCRF = 100, UseCRFSearch = false };
            var errors = cfg.Validate();
            Assert.IsNotEmpty(errors);
        }

        [TestMethod]
        public void Validate_NegativeFinalCpuUsed_ReturnsError()
        {
            var cfg = new PresetConfig { FinalCpuUsed = -1 };
            var errors = cfg.Validate();
            Assert.IsNotEmpty(errors);
        }

        [TestMethod]
        public void Validate_EmptyEncoder_ReturnsError()
        {
            var cfg = new PresetConfig { Encoder = "" };
            var errors = cfg.Validate();
            Assert.IsNotEmpty(errors);
        }

        #endregion

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
