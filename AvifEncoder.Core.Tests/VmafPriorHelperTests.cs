using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class VmafPriorHelperTests
    {
        [TestMethod]
        public void GetPriorFromVmaf_90_ReturnsReasonableRange()
        {
            var (median, lo, hi) = VmafPriorHelper.GetPriorFromVmaf(90);

            Assert.IsTrue(median > 30 && median < 45, $"median={median}");
            Assert.IsLessThan(median, lo, $"lo={lo} >= median={median}");
            Assert.IsGreaterThan(median, hi, $"hi={hi} <= median={median}");
            Assert.IsGreaterThanOrEqualTo(0, lo, $"lo={lo} < 0");
            Assert.IsLessThanOrEqualTo(63, hi, $"hi={hi} > 63");
        }

        [TestMethod]
        public void GetPriorFromVmaf_96_ReturnsNarrowRange()
        {
            var (median, lo, hi) = VmafPriorHelper.GetPriorFromVmaf(96);

            Assert.IsTrue(median > 15 && median < 25, $"median={median}");
        }

        [TestMethod]
        public void GetPriorFromVmaf_93_Interpolates()
        {
            var (median, lo, hi) = VmafPriorHelper.GetPriorFromVmaf(93);

            // Table has exact entry at 93
            Assert.IsTrue(median >= 30 && median <= 34);
        }

        [TestMethod]
        public void GetPriorFromVmaf_BelowMinimum_StillReturnsValid()
        {
            var (median, lo, hi) = VmafPriorHelper.GetPriorFromVmaf(85);

            Assert.IsTrue(median >= 0 && median <= 63);
            Assert.IsLessThanOrEqualTo(median, lo);
            Assert.IsGreaterThanOrEqualTo(median, hi);
        }

        [TestMethod]
        public void GetPriorFromVmaf_AboveMaximum_StillReturnsValid()
        {
            var (median, lo, hi) = VmafPriorHelper.GetPriorFromVmaf(99);

            Assert.IsTrue(median >= 0 && median <= 63);
        }

        [TestMethod]
        public void GetPriorFromVmaf_AllCrfInRange()
        {
            for (double vmaf = 88; vmaf <= 98; vmaf += 0.5)
            {
                var (m, l, h) = VmafPriorHelper.GetPriorFromVmaf(vmaf);
                Assert.IsTrue(m >= 0 && m <= 63, $"VMAF={vmaf}: median={m} out of range");
                Assert.IsTrue(l >= 0 && l <= 63, $"VMAF={vmaf}: lo={l} out of range");
                Assert.IsTrue(h >= 0 && h <= 63, $"VMAF={vmaf}: hi={h} out of range");
            }
        }

        [TestMethod]
        public void GetPriorFromVmaf_MonotonicallyDecreasing()
        {
            int prevMedian = 100;
            for (double vmaf = 89; vmaf <= 97; vmaf += 0.5)
            {
                var (median, _, _) = VmafPriorHelper.GetPriorFromVmaf(vmaf);
                Assert.IsLessThanOrEqualTo(prevMedian,
median, $"VMAF={vmaf}: median={median} > previous={prevMedian}");
                prevMedian = median;
            }
        }
    }
}
