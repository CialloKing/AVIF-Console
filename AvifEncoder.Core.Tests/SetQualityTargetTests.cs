using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class SetQualityTargetTests
    {
        [TestMethod]
        public void Vmaf_95_NormalizesTo095()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(95, "vmaf");
            Assert.AreEqual(0.95, cfg.TargetSSIM, 0.001);
            Assert.AreEqual("vmaf", cfg.MetricMode);
        }

        [TestMethod]
        public void Vmaf_0_ClampsTo0()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(0, "vmaf");
            Assert.AreEqual(0, cfg.TargetSSIM, 0.001);
        }

        [TestMethod]
        public void Ssim_098_Stays098()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(0.98, "ssim");
            Assert.AreEqual(0.98, cfg.TargetSSIM, 0.001);
        }

        [TestMethod]
        public void Psnr_40_ConvertsTo05()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(40, "psnr");
            Assert.AreEqual(0.5, cfg.TargetSSIM, 0.01);
        }

        [TestMethod]
        public void Psnr_50_ConvertsTo1()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(50, "psnr");
            Assert.AreEqual(1.0, cfg.TargetSSIM, 0.01);
        }

        [TestMethod]
        public void Psnr_30_ConvertsTo0()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(30, "psnr");
            Assert.AreEqual(0, cfg.TargetSSIM, 0.01);
        }

        [TestMethod]
        public void Xpsnr_StoresRawValue()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(48.5, "xpsnr");
            Assert.AreEqual(48.5, cfg.XpsnrTargetValue!.Value, 0.01);
            Assert.AreEqual(0, cfg.TargetSSIM);
            Assert.AreEqual("w", cfg.XpsnrTargetChannel);
        }

        [TestMethod]
        public void Xpsnr_Y_StoresChannel()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(48, "xpsnr_y");
            Assert.AreEqual("y", cfg.XpsnrTargetChannel);
        }

        [TestMethod]
        public void Ssimu2_StoresRawValue()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(85, "ssimu2");
            Assert.AreEqual(85, cfg.Ssimu2TargetValue!.Value, 0.01);
            Assert.AreEqual(0, cfg.TargetSSIM);
        }

        [TestMethod]
        public void Butter3_StoresRawValue()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(2.5, "butter3");
            Assert.AreEqual(2.5, cfg.Butteraugli3TargetValue!.Value, 0.01);
            Assert.IsTrue(cfg.MetricLowerIsBetter!.Value);
        }

        [TestMethod]
        public void Gmsd_StoresRawValue()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(0.15, "gmsd");
            Assert.AreEqual(0.15, cfg.GmsdTargetValue!.Value, 0.001);
            Assert.IsTrue(cfg.MetricLowerIsBetter!.Value);
        }

        [TestMethod]
        public void SwitchingMode_ClearsPreviousTarget()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(48, "xpsnr");
            Assert.IsNotNull(cfg.XpsnrTargetValue);

            cfg.SetQualityTarget(95, "vmaf");
            Assert.IsNull(cfg.XpsnrTargetValue);
            Assert.IsNull(cfg.Ssimu2TargetValue);
            Assert.AreEqual(0.95, cfg.TargetSSIM, 0.001);
        }

        [TestMethod]
        public void Ssimu2_Negative_StoresAsIs()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(-50, "ssimu2");
            Assert.AreEqual(-50, cfg.Ssimu2TargetValue!.Value, 0.01);
        }

        [TestMethod]
        public void Mix_Mode_Normalizes()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(0.95, "mix");
            Assert.AreEqual(0.95, cfg.TargetSSIM, 0.001);
        }
    }
}
