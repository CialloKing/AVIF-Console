using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class SetQualityTargetTests
    {
        [TestMethod]
        public void Vmaf_95_StoresNativeValue()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(95, "vmaf");
            Assert.AreEqual(95, cfg.NativeTargetValue!.Value, 0.001);
            Assert.AreEqual(95, cfg.GetEffectiveTarget(), 0.001);
            Assert.AreEqual("vmaf", cfg.MetricMode);
        }

        [TestMethod]
        public void Vmaf_0_StoresNativeValue()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(0, "vmaf");
            Assert.AreEqual(0, cfg.NativeTargetValue!.Value, 0.001);
        }

        [TestMethod]
        public void Ssim_098_StoresNativeValue()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(0.98, "ssim");
            Assert.AreEqual(0.98, cfg.NativeTargetValue!.Value, 0.001);
            Assert.AreEqual(0.98, cfg.GetEffectiveTarget(), 0.001);
        }

        [TestMethod]
        public void Psnr_40_StoresNativeValue()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(40, "psnr");
            Assert.AreEqual(40, cfg.NativeTargetValue!.Value, 0.01);
            Assert.AreEqual(40, cfg.GetEffectiveTarget(), 0.01);
        }

        [TestMethod]
        public void Psnr_50_StoresNativeValue()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(50, "psnr");
            Assert.AreEqual(50, cfg.NativeTargetValue!.Value, 0.01);
        }

        [TestMethod]
        public void Psnr_30_StoresNativeValue()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(30, "psnr");
            Assert.AreEqual(30, cfg.NativeTargetValue!.Value, 0.01);
        }

        [TestMethod]
        public void Preset_WithoutQ_UsesTargetSSIM()
        {
            var cfg = PresetConfig.CreateFromPreset(CliPreset.Balanced);
            cfg.MetricMode = "vmaf";
            // NativeTargetValue 未设置 → 从 TargetSSIM=0.97 反算 VMAF=97
            Assert.IsNull(cfg.NativeTargetValue);
            Assert.AreEqual(97, cfg.GetEffectiveTarget(), 0.01);
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
            Assert.AreEqual(95, cfg.NativeTargetValue!.Value, 0.001);
        }

        [TestMethod]
        public void Ssimu2_Negative_StoresAsIs()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(-50, "ssimu2");
            Assert.AreEqual(-50, cfg.Ssimu2TargetValue!.Value, 0.01);
        }

        [TestMethod]
        public void Mix_Mode_StoresNativeValue()
        {
            var cfg = new PresetConfig();
            cfg.SetQualityTarget(0.95, "mix");
            Assert.AreEqual(0.95, cfg.NativeTargetValue!.Value, 0.001);
            Assert.AreEqual(0.95, cfg.GetEffectiveTarget(), 0.001);
        }
    }
}
