using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class FileScopedFailTrackerTests
    {
        [TestMethod]
        public void IsBlacklisted_NewCrf_ReturnsFalse()
        {
            var tracker = new FileScopedFailTracker();
            Assert.IsFalse(tracker.IsBlacklisted(30));
        }

        [TestMethod]
        public void IsBlacklisted_AfterTwoFails_ReturnsTrue()
        {
            var tracker = new FileScopedFailTracker();
            tracker.RecordFailedAttempt(30);
            tracker.RecordFailedAttempt(30);
            Assert.IsTrue(tracker.IsBlacklisted(30));
        }

        [TestMethod]
        public void IsBlacklisted_WithinAvoidRadius_ReturnsTrue()
        {
            var tracker = new FileScopedFailTracker();
            tracker.RecordFailedAttempt(30);
            tracker.RecordFailedAttempt(30);
            // CRF 28 is within radius 2 of 30
            Assert.IsTrue(tracker.IsBlacklisted(28));
            Assert.IsTrue(tracker.IsBlacklisted(32));
        }

        [TestMethod]
        public void IsBlacklisted_OutsideAvoidRadius_ReturnsFalse()
        {
            var tracker = new FileScopedFailTracker();
            tracker.RecordFailedAttempt(30);
            tracker.RecordFailedAttempt(30);
            Assert.IsFalse(tracker.IsBlacklisted(27));
            Assert.IsFalse(tracker.IsBlacklisted(33));
        }

        [TestMethod]
        public void ClearCrf_RemovesRecord()
        {
            var tracker = new FileScopedFailTracker();
            tracker.RecordFailedAttempt(30);
            tracker.ClearCrf(30);
            Assert.IsFalse(tracker.IsBlacklisted(30));
        }

        [TestMethod]
        public void Reset_ClearsAll()
        {
            var tracker = new FileScopedFailTracker();
            tracker.RecordFailedAttempt(30);
            tracker.RecordFailedAttempt(30);
            tracker.Reset();
            Assert.IsFalse(tracker.IsBlacklisted(30));
        }

        [TestMethod]
        public void RecordFailedAttempt_Concurrent_DoesNotCrash()
        {
            // 验证并发写入不崩溃 (锁保护确认)
            var tracker = new FileScopedFailTracker();
            var tasks = new System.Threading.Tasks.Task[20];
            for (int i = 0; i < tasks.Length; i++)
            {
                int crf = i / 2;
                tasks[i] = System.Threading.Tasks.Task.Run(() =>
                {
                    for (int j = 0; j < 100; j++)
                    {
                        tracker.RecordFailedAttempt(crf);
                        tracker.IsBlacklisted(crf);
                    }
                });
            }
            System.Threading.Tasks.Task.WaitAll(tasks);
            // 不抛异常即通过
        }

        [TestMethod]
        public void FindSafeCrfInInterval_ReturnsValidCrf()
        {
            var tracker = new FileScopedFailTracker();
            tracker.RecordFailedAttempt(35);
            tracker.RecordFailedAttempt(35);

            int safe = tracker.FindSafeCrfInInterval(35, 30, 40);
            Assert.AreNotEqual(-1, safe);
            Assert.IsFalse(tracker.IsBlacklisted(safe));
            Assert.IsTrue(safe >= 30 && safe <= 40);
        }
    }
}
