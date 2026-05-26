using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class NaturalComparerTests
    {
        private readonly NaturalComparer _comparer = new();

        [TestMethod]
        public void Compare_SameString_ReturnsZero()
        {
            Assert.AreEqual(0, _comparer.Compare("hello", "hello"));
        }

        [TestMethod]
        public void Compare_NullFirst_ReturnsNegative()
        {
            Assert.IsTrue(_comparer.Compare(null, "a") < 0);
        }

        [TestMethod]
        public void Compare_NullSecond_ReturnsPositive()
        {
            Assert.IsTrue(_comparer.Compare("a", null) > 0);
        }

        [TestMethod]
        public void Compare_NumericalOrdering()
        {
            // "img2" < "img10" in natural sort
            Assert.IsTrue(_comparer.Compare("img2", "img10") < 0);
        }

        [TestMethod]
        public void Compare_PureNumbers()
        {
            Assert.IsTrue(_comparer.Compare("10", "2") > 0);
        }

        [TestMethod]
        public void Compare_MixedTextAndNumbers()
        {
            // Natural: file1 < file2 < file10
            Assert.IsTrue(_comparer.Compare("file1", "file2") < 0);
            Assert.IsTrue(_comparer.Compare("file2", "file10") < 0);
        }
    }
}
