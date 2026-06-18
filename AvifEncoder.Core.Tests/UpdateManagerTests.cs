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
        public void BuildUpdateScript_BacksUpCurrentExeBeforeReplacing()
        {
            string exePath = Path.Combine("C:\\Apps\\Avif Encoder", "AvifEncoder.GuiLakeUI.exe");
            string newPath = Path.Combine(Path.GetTempPath(), $"download_{Guid.NewGuid():N}.new");

            string script = UpdateManager.BuildUpdateScript(exePath, newPath);

            Assert.Contains(Path.GetFullPath(exePath) + ".bak", script,
                "Should backup current exe before replacing");
        }

        [TestMethod]
        public void BuildUpdateScript_DoesNotDeleteBeforeReplace()
        {
            string exePath = Path.Combine("C:\\Apps\\Avif Encoder", "AvifEncoder.GuiLakeUI.exe");
            string newPath = Path.Combine(Path.GetTempPath(), $"download_{Guid.NewGuid():N}.new");

            string script = UpdateManager.BuildUpdateScript(exePath, newPath);

            // ★ 不应先 del 旧 exe（旧行为），改为 rename+backup
            Assert.DoesNotContain("del \"" + Path.GetFullPath(exePath) + "\"", script);
        }

        [TestMethod]
        public void BuildUpdateScript_RestoresBackupOnMoveFailure()
        {
            string exePath = Path.Combine("C:\\Apps\\Avif Encoder", "AvifEncoder.GuiLakeUI.exe");
            string newPath = Path.Combine(Path.GetTempPath(), $"download_{Guid.NewGuid():N}.new");
            string fullBackup = Path.GetFullPath(exePath) + ".bak";

            string script = UpdateManager.BuildUpdateScript(exePath, newPath);

            // ★ 移动失败时有恢复备份的逻辑
            Assert.Contains(fullBackup, script);
            Assert.Contains($"move /y \"{fullBackup}\" \"{Path.GetFullPath(exePath)}\"", script,
                "Should restore backup on move failure");
        }

        [TestMethod]
        public void BuildUpdateScript_OnlyStartsOnSuccess()
        {
            string exePath = Path.Combine("C:\\Apps\\Avif Encoder", "AvifEncoder.GuiLakeUI.exe");
            string newPath = Path.Combine(Path.GetTempPath(), $"download_{Guid.NewGuid():N}.new");

            string script = UpdateManager.BuildUpdateScript(exePath, newPath);

            // ★ start 应在 success 分支内（"if exist exe" 之后，restore backup 之前）
            int successIdx = script.IndexOf("start \"\"");
            int restoreIdx = script.IndexOf($"move /y \"{Path.GetFullPath(exePath)}.bak\"");
            Assert.IsTrue(successIdx > 0);
            Assert.IsTrue(successIdx < restoreIdx,
                "start should be in success branch, before restore backup logic");
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
