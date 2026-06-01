using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class MetricsScoreTests
    {
        [TestMethod]
        public void GetSearchScore_Ssim_ReturnsDirectValue()
        {
            var m = new QualityMetrics { SSIM = 0.975 };
            double score = AvifPipeline.GetSearchScore(m, "ssim");
            Assert.AreEqual(0.975, score, 0.001);
        }

        [TestMethod]
        public void GetSearchScore_Vmaf_ReturnsRawValue()
        {
            var m = new QualityMetrics { VMAF = 95.0 };
            double score = AvifPipeline.GetSearchScore(m, "vmaf");
            Assert.AreEqual(95.0, score, 0.01);
        }

        [TestMethod]
        public void GetSearchScore_Psnr_ReturnsRawValue()
        {
            var m = new QualityMetrics { PSNR_Y = 40 };
            double score = AvifPipeline.GetSearchScore(m, "psnr");
            Assert.AreEqual(40.0, score, 0.01);
        }

        [TestMethod]
        public void GetSearchScore_MsSsim_ReturnsDirectValue()
        {
            var m = new QualityMetrics { MS_SSIM = 0.985 };
            double score = AvifPipeline.GetSearchScore(m, "msssim");
            Assert.AreEqual(0.985, score, 0.001);
        }

        [TestMethod]
        public void GetSearchScore_Xpsnr_ReturnsWeightedValue()
        {
            var m = new QualityMetrics { W_XPSNR = 48.5 };
            double score = AvifPipeline.GetSearchScore(m, "xpsnr");
            Assert.AreEqual(48.5, score, 0.1);
        }

        [TestMethod]
        public void GetSearchScore_Ssimu2_ReturnsDirectValue()
        {
            var m = new QualityMetrics { SSIMULACRA2 = 88 };
            double score = AvifPipeline.GetSearchScore(m, "ssimu2");
            Assert.AreEqual(88, score, 0.1);
        }

        [TestMethod]
        public void GetSearchScore_Butter3_ReturnsDirectValue()
        {
            var m = new QualityMetrics { Butteraugli_3norm = 1.5 };
            double score = AvifPipeline.GetSearchScore(m, "butter3");
            Assert.AreEqual(1.5, score, 0.01);
        }

        [TestMethod]
        public void GetSearchScore_Gmsd_ReturnsDirectValue()
        {
            var m = new QualityMetrics { GMSD = 0.15 };
            double score = AvifPipeline.GetSearchScore(m, "gmsd");
            Assert.AreEqual(0.15, score, 0.01);
        }

        [TestMethod]
        public void GetSearchScore_NaN_ReturnsMinusOne()
        {
            var m = new QualityMetrics { SSIM = double.NaN };
            double score = AvifPipeline.GetSearchScore(m, "ssim");
            Assert.AreEqual(-1, score);
        }

        [TestMethod]
        public void ComputeMixScore_NormalScores_ReturnsWeightedSum()
        {
            var m = new QualityMetrics { VMAF = 95, XPSNR_Y = 50, SSIM = 0.98, MS_SSIM = 0.97, PSNR_Y = 42 };
            double score = MetricRegistry.ComputeMixScore(m);
            Assert.IsTrue(score > 0 && score < 1, $"MixScore={score} out of expected range");
        }

        [TestMethod]
        public void ComputeMixScore_ZeroScores_ReturnsZero()
        {
            var m = new QualityMetrics();
            double score = MetricRegistry.ComputeMixScore(m);
            Assert.AreEqual(0.0, score, 0.01);
        }

        [TestMethod]
        public void GetSearchScore_UnknownMode_ReturnsSsim()
        {
            var m = new QualityMetrics { SSIM = 0.88 };
            double score = AvifPipeline.GetSearchScore(m, "unknown_mode");
            Assert.AreEqual(0.88, score, 0.001);
        }
    }
}
