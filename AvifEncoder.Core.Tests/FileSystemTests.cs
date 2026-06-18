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

        [TestMethod]
        public void CopyFileAtomic_ReplacesExistingFileAndCleansTempFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), $"fs_copy_atomic_{Guid.NewGuid():N}");
            string source = Path.Combine(dir, "cache.avif");
            string dest = Path.Combine(dir, "output.avif");
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(source, "cached", Encoding.UTF8);
                File.WriteAllText(dest, "old", Encoding.UTF8);

                var fs = new PresetConfig.RealFileSystem();
                fs.CopyFileAtomic(source, dest, overwrite: true);

                Assert.AreEqual("cached", File.ReadAllText(dest, Encoding.UTF8));
                Assert.IsEmpty(Directory.GetFiles(dir, "output.avif.*.tmp"));
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }

        [TestMethod]
        public async Task WriteAllBytesAtomicAsync_ReplacesExistingFileAndCleansTempFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), $"fs_bytes_atomic_{Guid.NewGuid():N}");
            string path = Path.Combine(dir, "frame_metrics.csv");
            try
            {
                Directory.CreateDirectory(dir);
                await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("old"));

                var fs = new PresetConfig.RealFileSystem();
                await fs.WriteAllBytesAtomicAsync(path, Encoding.UTF8.GetBytes("new"));

                Assert.AreEqual("new", await File.ReadAllTextAsync(path, Encoding.UTF8));
                Assert.IsEmpty(Directory.GetFiles(dir, "frame_metrics.csv.*.tmp"));
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }

        [TestMethod]
        public void AppendAllTextWithHeader_WritesHeaderOnceAndAppendsRows()
        {
            string dir = Path.Combine(Path.GetTempPath(), $"fs_append_header_{Guid.NewGuid():N}");
            string path = Path.Combine(dir, "failed_verification.csv");
            try
            {
                var fs = new PresetConfig.RealFileSystem();

                fs.AppendAllTextWithHeader(path, "A,B", "1,2\n", Encoding.UTF8);
                fs.AppendAllTextWithHeader(path, "A,B", "3,4\n", Encoding.UTF8);

                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                CollectionAssert.AreEqual(new[] { "A,B", "1,2", "3,4" }, lines);
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }

        [TestMethod]
        public void AppendAllTextWithHeader_SeparatesExistingFileWithoutTrailingNewline()
        {
            string dir = Path.Combine(Path.GetTempPath(), $"fs_append_newline_{Guid.NewGuid():N}");
            string path = Path.Combine(dir, "failed_verification.csv");
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, "A,B\n1,2", Encoding.UTF8);

                var fs = new PresetConfig.RealFileSystem();
                fs.AppendAllTextWithHeader(path, "A,B", "3,4\n", Encoding.UTF8);

                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                CollectionAssert.AreEqual(new[] { "A,B", "1,2", "3,4" }, lines);
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
