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
            Assert.IsFalse(script.Contains("\"AvifEncoder.GuiLakeUI.exe.new\""));
        }
    }
}
