namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class DynamicConcurrencyLimiterTests
    {
        [TestMethod]
        public async Task SetMaxShrink_DoesNotGrantWaiterUntilRunningCountFallsBelowTarget()
        {
            using var limiter = new DynamicConcurrencyLimiter(3);
            await limiter.WaitAsync();
            await limiter.WaitAsync();
            await limiter.WaitAsync();

            Assert.AreEqual(1, limiter.SetMax(1));
            Task<bool> waiter = limiter.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(waiter.IsCompleted);

            limiter.Release();
            await Task.Delay(50);
            Assert.IsFalse(waiter.IsCompleted);

            limiter.Release();
            await Task.Delay(50);
            Assert.IsFalse(waiter.IsCompleted);

            limiter.Release();
            Assert.IsTrue(await waiter.WaitAsync(TimeSpan.FromSeconds(1)));
        }

        [TestMethod]
        public async Task SetMaxExpand_GrantsQueuedWaitersImmediately()
        {
            using var limiter = new DynamicConcurrencyLimiter(1);
            await limiter.WaitAsync();

            Task<bool> waiter = limiter.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(waiter.IsCompleted);

            Assert.AreEqual(2, limiter.SetMax(2));
            Assert.IsTrue(await waiter.WaitAsync(TimeSpan.FromSeconds(1)));
        }

        [TestMethod]
        public async Task WaitAsyncTimeout_RemovesPendingWaiter()
        {
            using var limiter = new DynamicConcurrencyLimiter(1);
            await limiter.WaitAsync();

            Assert.IsFalse(await limiter.WaitAsync(TimeSpan.FromMilliseconds(20)));
            limiter.Release();

            Assert.IsTrue(await limiter.WaitAsync(TimeSpan.FromSeconds(1)));
        }
    }
}
