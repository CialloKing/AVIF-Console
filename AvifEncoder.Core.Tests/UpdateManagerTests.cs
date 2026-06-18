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

        [TestMethod]
        public void GetUpdateScriptPath_UsesUniqueSiblingBatFile()
        {
            string exePath = Path.Combine("C:\\Apps\\Avif Encoder", "AvifEncoder.GuiLakeUI.exe");
            string exeDir = Path.GetDirectoryName(Path.GetFullPath(exePath))!;

            string first = UpdateManager.GetUpdateScriptPath(exePath);
            string second = UpdateManager.GetUpdateScriptPath(exePath);

            Assert.AreNotEqual(first, second);
            Assert.StartsWith(Path.Combine(exeDir, "_update."), first);
            Assert.EndsWith(".bat", first);
            Assert.AreNotEqual(Path.Combine(exeDir, "_update.bat"), first);
        }

        [TestMethod]
        public void WriteUpdateScriptAtomic_ReplacesExistingFileAndCleansTempFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), $"update_script_{Guid.NewGuid():N}");
            string batPath = Path.Combine(dir, "_update.test.bat");
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(batPath, "old");

                UpdateManager.WriteUpdateScriptAtomic(batPath, "new");

                Assert.AreEqual("new", File.ReadAllText(batPath));
                Assert.IsEmpty(Directory.GetFiles(dir, "_update.test.bat.*.tmp"));
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }
    }
}
