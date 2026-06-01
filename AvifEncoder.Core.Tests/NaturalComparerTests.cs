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
            Assert.IsLessThan(0, _comparer.Compare(null, "a"));
        }

        [TestMethod]
        public void Compare_NullSecond_ReturnsPositive()
        {
            Assert.IsGreaterThan(0, _comparer.Compare("a", null));
        }

        [TestMethod]
        public void Compare_NumericalOrdering()
        {
            // "img2" < "img10" in natural sort
            Assert.IsLessThan(0, _comparer.Compare("img2", "img10"));
        }

        [TestMethod]
        public void Compare_PureNumbers()
        {
            Assert.IsGreaterThan(0, _comparer.Compare("10", "2"));
        }

        [TestMethod]
        public void Compare_CaseInsensitive_ReturnsZero()
        {
            // 大小写不敏感，大写小写应相等
            Assert.AreEqual(0, _comparer.Compare("FILE1", "file1"));
            Assert.AreEqual(0, _comparer.Compare("Photo", "photo"));
        }

        [TestMethod]
        public void Compare_CaseInsensitive_NumericalOrder()
        {
            // 大小写不敏感 + 自然排序
            Assert.IsLessThan(0, _comparer.Compare("FILE2", "FILE10"));
            Assert.IsLessThan(0, _comparer.Compare("Img2", "img10"));
        }

        [TestMethod]
        public void Compare_MixedTextAndNumbers()
        {
            // Natural: file1 < file2 < file10
            Assert.IsLessThan(0, _comparer.Compare("file1", "file2"));
            Assert.IsLessThan(0, _comparer.Compare("file2", "file10"));
        }
    }
}
