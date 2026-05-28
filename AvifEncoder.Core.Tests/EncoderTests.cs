using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class EncoderTests
    {
        #region Av1EncoderFactory.Get

        [TestMethod]
        public void Get_LibAom_ReturnsLibAomEncoder()
        {
            var encoder = Av1EncoderFactory.Get("libaom-av1");
            Assert.IsInstanceOfType(encoder, typeof(LibAomEncoder));
        }

        [TestMethod]
        public void Get_SvtAv1_ReturnsSvtAv1Encoder()
        {
            var encoder = Av1EncoderFactory.Get("libsvtav1");
            Assert.IsInstanceOfType(encoder, typeof(SvtAv1Encoder));
        }

        [TestMethod]
        public void Get_Rav1e_ReturnsRav1eEncoder()
        {
            var encoder = Av1EncoderFactory.Get("librav1e");
            Assert.IsInstanceOfType(encoder, typeof(Rav1eEncoder));
        }

        [TestMethod]
        public void Get_Unknown_ReturnsHardwareAv1Encoder()
        {
            var encoder = Av1EncoderFactory.Get("av1_nvenc");
            Assert.IsInstanceOfType(encoder, typeof(HardwareAv1Encoder));
        }

        [TestMethod]
        public void Get_SameKeyTwice_ReturnsSameInstance()
        {
            var e1 = Av1EncoderFactory.Get("libaom-av1");
            var e2 = Av1EncoderFactory.Get("libaom-av1");
            Assert.AreSame(e1, e2);
        }

        #endregion

        #region LibAomEncoder

        [TestMethod]
        public void LibAom_SupportsStillPicture_IsTrue()
        {
            Assert.IsTrue(Av1EncoderFactory.Get("libaom-av1").SupportsStillPicture);
        }

        [TestMethod]
        public void LibAom_SupportsTiles_IsTrue()
        {
            Assert.IsTrue(Av1EncoderFactory.Get("libaom-av1").SupportsTiles);
        }

        [TestMethod]
        public void LibAom_SupportsLossless_IsTrue()
        {
            Assert.IsTrue(Av1EncoderFactory.Get("libaom-av1").SupportsLossless);
        }

        [TestMethod]
        public void LibAom_MinMaxSpeed()
        {
            var e = Av1EncoderFactory.Get("libaom-av1");
            Assert.AreEqual(0, e.MinSpeed);
            Assert.AreEqual(8, e.MaxSpeed);
        }

        [TestMethod]
        public void LibAom_BuildSpeedArg()
        {
            Assert.AreEqual("-cpu-used 4", Av1EncoderFactory.Get("libaom-av1").BuildSpeedArg(4));
        }

        [TestMethod]
        public void LibAom_BuildLosslessArg()
        {
            Assert.AreEqual("-lossless 1", Av1EncoderFactory.Get("libaom-av1").BuildLosslessArg());
        }

        #endregion

        #region SvtAv1Encoder

        [TestMethod]
        public void SvtAv1_SupportsStillPicture_IsFalse()
        {
            Assert.IsFalse(Av1EncoderFactory.Get("libsvtav1").SupportsStillPicture);
        }

        [TestMethod]
        public void SvtAv1_BuildSpeedArg_InvertsPreset()
        {
            // cpuUsed=0 (slowest) → preset=13
            string result = Av1EncoderFactory.Get("libsvtav1").BuildSpeedArg(0);
            Assert.Contains("-preset 13", result);
        }

        [TestMethod]
        public void SvtAv1_BuildTuneArg_Vmaf_Returns3()
        {
            Assert.AreEqual("3", Av1EncoderFactory.Get("libsvtav1").BuildTuneArg("vmaf"));
        }

        [TestMethod]
        public void SvtAv1_BuildTuneArg_Psnr_Returns1()
        {
            Assert.AreEqual("1", Av1EncoderFactory.Get("libsvtav1").BuildTuneArg("psnr"));
        }

        [TestMethod]
        public void SvtAv1_BuildTuneArg_Ssim_Returns2()
        {
            Assert.AreEqual("2", Av1EncoderFactory.Get("libsvtav1").BuildTuneArg("ssim"));
        }

        [TestMethod]
        public void SvtAv1_BuildFullTuneArg_ContainsSvtav1Params()
        {
            string result = Av1EncoderFactory.Get("libsvtav1").BuildFullTuneArg("vmaf");
            Assert.Contains("-svtav1-params", result);
            Assert.Contains("tune=3", result);
            Assert.Contains("keyint=1", result);
            Assert.Contains("avif=1", result);
        }

        #endregion

        #region Rav1eEncoder

        [TestMethod]
        public void Rav1e_BuildTuneArg_Psychovisual()
        {
            Assert.AreEqual("psychovisual", Av1EncoderFactory.Get("librav1e").BuildTuneArg("vmaf"));
            Assert.AreEqual("psychovisual", Av1EncoderFactory.Get("librav1e").BuildTuneArg("ssim"));
        }

        [TestMethod]
        public void Rav1e_BuildTuneArg_Default_Empty()
        {
            Assert.AreEqual("", Av1EncoderFactory.Get("librav1e").BuildTuneArg("psnr"));
        }

        [TestMethod]
        public void Rav1e_BuildFullTuneArg_IncludesDashDashTune()
        {
            string result = Av1EncoderFactory.Get("librav1e").BuildFullTuneArg("vmaf");
            Assert.Contains("--tune psychovisual", result);
        }

        #endregion

        #region HardwareAv1Encoder

        [TestMethod]
        public void Hardware_AllFlagsAreFalse()
        {
            var e = Av1EncoderFactory.Get("av1_qsv");
            Assert.IsFalse(e.SupportsLossless);
            Assert.IsFalse(e.SupportsStillPicture);
            Assert.IsFalse(e.SupportsTiles);
            Assert.IsFalse(e.SupportsRowMt);
            Assert.IsFalse(e.SupportsAomParams);
        }

        [TestMethod]
        public void Hardware_BuildSpeedArg_ReturnsEmpty()
        {
            Assert.AreEqual("", Av1EncoderFactory.Get("av1_amf").BuildSpeedArg(5));
        }

        [TestMethod]
        public void Hardware_BuildLosslessArg_ReturnsEmpty()
        {
            Assert.AreEqual("", Av1EncoderFactory.Get("av1_vaapi").BuildLosslessArg());
        }

        [TestMethod]
        public void Hardware_BuildFullTuneArg_ReturnsEmpty()
        {
            Assert.AreEqual("", Av1EncoderFactory.Get("av1_nvenc").BuildFullTuneArg("vmaf"));
        }

        #endregion

        #region BuildQualityArg

        [TestMethod]
        public void BuildQualityArg_LibAom_ReturnsCrf()
        {
            Assert.AreEqual("-crf 30", Av1EncoderFactory.Get("libaom-av1").BuildQualityArg(30));
        }

        [TestMethod]
        public void BuildQualityArg_SvtAv1_ReturnsCrf()
        {
            Assert.AreEqual("-crf 25", Av1EncoderFactory.Get("libsvtav1").BuildQualityArg(25));
        }

        [TestMethod]
        public void BuildQualityArg_Nvenc_ReturnsCq()
        {
            Assert.AreEqual("-cq 30", Av1EncoderFactory.Get("av1_nvenc").BuildQualityArg(30));
        }

        [TestMethod]
        public void BuildQualityArg_Qsv_ReturnsGlobalQuality()
        {
            Assert.AreEqual("-global_quality 25", Av1EncoderFactory.Get("av1_qsv").BuildQualityArg(25));
        }

        [TestMethod]
        public void BuildQualityArg_Amf_ReturnsQpCombined()
        {
            string result = Av1EncoderFactory.Get("av1_amf").BuildQualityArg(20);
            Assert.Contains("-qp_i 20", result);
            Assert.Contains("-qp_p 20", result);
        }

        [TestMethod]
        public void BuildQualityArg_UnknownHardware_FallsBackToCrf()
        {
            Assert.AreEqual("-crf 30", Av1EncoderFactory.Get("av1_unknown").BuildQualityArg(30));
        }

        #endregion
    }
}
