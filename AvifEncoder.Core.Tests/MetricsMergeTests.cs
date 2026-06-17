namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class MetricsMergeTests
    {
        [TestMethod]
        public void MergeQualityMetrics_PreservesExistingFieldsWhenIncomingIsPartial()
        {
            var existing = new QualityMetrics
            {
                SSIMULACRA2 = 80.5,
                Butteraugli_Raw = 0.1234,
                XPSNR_Y = 48.8,
                W_XPSNR = 47.1
            };
            var incoming = new QualityMetrics
            {
                Butteraugli_3norm = 1.25,
                GMSD = 0.031
            };

            var merged = AvifPipeline.MergeQualityMetrics(existing, incoming);

            Assert.AreEqual(80.5, merged.SSIMULACRA2);
            Assert.AreEqual(0.1234, merged.Butteraugli_Raw);
            Assert.AreEqual(48.8, merged.XPSNR_Y);
            Assert.AreEqual(47.1, merged.W_XPSNR);
            Assert.AreEqual(1.25, merged.Butteraugli_3norm);
            Assert.AreEqual(0.031, merged.GMSD);
        }

        [TestMethod]
        public void MergeQualityMetrics_OverridesMatchingFieldsWithIncomingValues()
        {
            var existing = new QualityMetrics
            {
                SSIMULACRA2 = 72.0,
                Butteraugli_3norm = 2.0,
                GMSD = 0.08
            };
            var incoming = new QualityMetrics
            {
                Butteraugli_3norm = 1.1,
                GMSD = 0.02
            };

            var merged = AvifPipeline.MergeQualityMetrics(existing, incoming);

            Assert.AreEqual(72.0, merged.SSIMULACRA2);
            Assert.AreEqual(1.1, merged.Butteraugli_3norm);
            Assert.AreEqual(0.02, merged.GMSD);
        }

        [TestMethod]
        public void NeedsXpsnrMetrics_MissingAnyChannel_ReturnsTrue()
        {
            var metrics = new QualityMetrics
            {
                XPSNR_Y = 45.0,
                XPSNR_U = 46.0,
                XPSNR_V = 47.0
            };

            Assert.IsTrue(AvifPipeline.NeedsXpsnrMetrics(metrics));
        }

        [TestMethod]
        public void NeedsXpsnrMetrics_AllChannelsPresent_ReturnsFalse()
        {
            var metrics = new QualityMetrics
            {
                XPSNR_Y = 45.0,
                XPSNR_U = 46.0,
                XPSNR_V = 47.0,
                W_XPSNR = 45.5
            };

            Assert.IsFalse(AvifPipeline.NeedsXpsnrMetrics(metrics));
        }
    }
}
