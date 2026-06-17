namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class RecomputeMetricsTests
    {
        [TestMethod]
        public void NeedsRecomputeMetricRow_AllMetricsPresent_ReturnsFalse()
        {
            string[] fields =
            [
                "file.avif",
                "source.png",
                "88.0000",
                "1.0000",
                "0.7500",
                "0.1200"
            ];

            Assert.IsFalse(AvifPipeline.NeedsRecomputeMetricRow(
                fields, idxSsimu2: 2, idxButterR: 3, idxButter3: 4, idxGmsd: 5));
        }

        [TestMethod]
        public void NeedsRecomputeMetricRow_Ssimu2PresentButButterMissing_ReturnsTrue()
        {
            string[] fields =
            [
                "file.avif",
                "source.png",
                "88.0000",
                "",
                "",
                "0.1200"
            ];

            Assert.IsTrue(AvifPipeline.NeedsRecomputeMetricRow(
                fields, idxSsimu2: 2, idxButterR: 3, idxButter3: 4, idxGmsd: 5));
        }

        [TestMethod]
        public void NeedsRecomputeMetricRow_MissingTrailingColumns_ReturnsTrue()
        {
            string[] fields =
            [
                "file.avif",
                "source.png",
                "88.0000"
            ];

            Assert.IsTrue(AvifPipeline.NeedsRecomputeMetricRow(
                fields, idxSsimu2: 2, idxButterR: 3, idxButter3: 4, idxGmsd: 5));
        }
    }
}
