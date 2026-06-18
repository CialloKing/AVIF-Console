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

        [TestMethod]
        public async Task ForEachBoundedAsync_DoesNotExceedMaxConcurrency()
        {
            int running = 0;
            int maxObserved = 0;
            var items = Enumerable.Range(0, 64).ToArray();

            await AvifPipeline.ForEachBoundedAsync(items, 4, async (_, _) =>
            {
                int current = Interlocked.Increment(ref running);
                int observed;
                do
                {
                    observed = Volatile.Read(ref maxObserved);
                    if (current <= observed)
                        break;
                }
                while (Interlocked.CompareExchange(ref maxObserved, current, observed) != observed);

                await Task.Delay(10);
                Interlocked.Decrement(ref running);
            });

            Assert.IsLessThanOrEqualTo(4, maxObserved);
        }

        [TestMethod]
        public async Task ForEachBoundedAsync_StopsSchedulingWhenCanceled()
        {
            int started = 0;
            using var cts = new CancellationTokenSource();
            var items = Enumerable.Range(0, 100).ToArray();

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await AvifPipeline.ForEachBoundedAsync(items, 4, async (_, token) =>
                {
                    int current = Interlocked.Increment(ref started);
                    if (current == 1)
                        await cts.CancelAsync();

                    await Task.Delay(5, token);
                }, cts.Token));

            Assert.IsLessThan(100, started);
        }
    }
}
