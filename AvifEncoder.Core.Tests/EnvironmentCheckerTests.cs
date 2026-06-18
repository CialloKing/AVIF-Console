using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class EnvironmentCheckerTests
    {
        [TestMethod]
        public void CreateDefaultCheckWorkDir_ReturnsUniqueIsolatedDirectory()
        {
            string first = AvifEnvironmentChecker.CreateDefaultCheckWorkDir();
            string second = AvifEnvironmentChecker.CreateDefaultCheckWorkDir();

            Assert.AreNotEqual(first, second);
            Assert.AreEqual("AvifEncoder_check", Path.GetFileName(Path.GetDirectoryName(first)!));
            Assert.AreEqual("AvifEncoder_check", Path.GetFileName(Path.GetDirectoryName(second)!));
            Assert.AreEqual(32, Path.GetFileName(first).Length);
            Assert.AreEqual(32, Path.GetFileName(second).Length);
        }
    }
}
