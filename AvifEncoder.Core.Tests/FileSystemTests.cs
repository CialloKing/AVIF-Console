using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class FileSystemTests
    {
        [TestMethod]
        public void WriteAllTextAtomic_ReplacesExistingFileAndCleansTempFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), $"fs_atomic_{Guid.NewGuid():N}");
            string path = Path.Combine(dir, "avif_stats.csv");
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, "old", Encoding.UTF8);

                var fs = new PresetConfig.RealFileSystem();
                fs.WriteAllTextAtomic(path, "new", new UTF8Encoding(false));

                Assert.AreEqual("new", File.ReadAllText(path, Encoding.UTF8));
                Assert.IsEmpty(Directory.GetFiles(dir, "avif_stats.csv.*.tmp"));
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
