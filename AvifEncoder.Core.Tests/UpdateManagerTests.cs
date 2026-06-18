namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class UpdateManagerTests
    {
        [TestMethod]
        public void BuildUpdateScript_UsesProvidedDownloadedPath()
        {
            string exePath = Path.Combine("C:\\Apps\\Avif Encoder", "AvifEncoder.GuiLakeUI.exe");
            string newPath = Path.Combine(Path.GetTempPath(), $"download_{Guid.NewGuid():N}.new");

            string script = UpdateManager.BuildUpdateScript(exePath, newPath);

            Assert.Contains(Path.GetFullPath(newPath), script);
            Assert.Contains(Path.GetFullPath(exePath), script);
            Assert.DoesNotContain("\"AvifEncoder.GuiLakeUI.exe.new\"", script);
        }

        [TestMethod]
        public void GetDownloadTempPath_UsesUniqueSiblingNewFile()
        {
            string exePath = Path.Combine("C:\\Apps\\Avif Encoder", "AvifEncoder.GuiLakeUI.exe");

            string first = UpdateManager.GetDownloadTempPath(exePath);
            string second = UpdateManager.GetDownloadTempPath(exePath);

            Assert.AreNotEqual(first, second);
            Assert.StartsWith(exePath + ".", first);
            Assert.EndsWith(".new", first);
            Assert.AreNotEqual(exePath + ".new", first);
        }
    }
}
