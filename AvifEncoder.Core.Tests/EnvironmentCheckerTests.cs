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

        [TestMethod]
        public async Task CheckEnvironmentAsync_UpdatesLastResultWhenFfmpegIsMissing()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"env_check_missing_{Guid.NewGuid():N}");

            var result = await AvifEnvironmentChecker.CheckEnvironmentAsync(
                logger: null,
                tempDir: tempDir,
                findExecutable: _ => null);

            Assert.IsFalse(result.FfmpegAvailable);
            Assert.AreSame(result, AvifEnvironmentChecker.LastResult);
            Assert.IsFalse(Directory.Exists(tempDir));
        }

        [TestMethod]
        public void TryDeleteFile_DeletesExistingFileAndIgnoresMissingFile()
        {
            string path = Path.Combine(Path.GetTempPath(), $"env_delete_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(path, "temp");

            Assert.IsTrue(AvifEnvironmentChecker.TryDeleteFile(path));
            Assert.IsFalse(File.Exists(path));
            Assert.IsTrue(AvifEnvironmentChecker.TryDeleteFile(path));
        }

        [TestMethod]
        public void TryDeleteFile_DoesNotThrowWhenFileIsLocked()
        {
            string path = Path.Combine(Path.GetTempPath(), $"env_delete_locked_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(path, "temp");
            try
            {
                using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

                bool deleted = AvifEnvironmentChecker.TryDeleteFile(path);

                if (OperatingSystem.IsWindows())
                {
                    Assert.IsFalse(deleted);
                    Assert.IsTrue(File.Exists(path));
                }
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }
    }
}
