using System.IO;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class AppConfigTests
    {
        private string _tempFile = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempFile = Path.Combine(Path.GetTempPath(), $"test_config_{Guid.NewGuid():N}.json");
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (File.Exists(_tempFile))
            {
                File.Delete(_tempFile);
            }
        }

        [TestMethod]
        public void SaveAndLoad_RoundTrip_PreservesAllFields()
        {
            var original = new AppConfig
            {
                FontFamily = "Consolas",
                FontSize = 12f,
                EncodeEncoder = "libsvtav1",
                EncodePreset = "extreme",
                EncodeJobs = 4,
                EncodeSearch = true,
                EncodeCrfMin = 10,
                EncodeCrfMax = 50,
                EncodeQualityValue = 95.0,
            };

            AppConfigHelper.SaveToFile(original, _tempFile);
            Assert.IsTrue(File.Exists(_tempFile));

            var loaded = AppConfigHelper.LoadFromFile(_tempFile);
            Assert.IsNotNull(loaded);
            Assert.AreEqual("Consolas", loaded!.FontFamily);
            Assert.AreEqual(12f, loaded.FontSize);
            Assert.AreEqual("libsvtav1", loaded.EncodeEncoder);
            Assert.AreEqual("extreme", loaded.EncodePreset);
            Assert.AreEqual(4, loaded.EncodeJobs);
            Assert.IsTrue(loaded.EncodeSearch);
            Assert.AreEqual(10, loaded.EncodeCrfMin);
            Assert.AreEqual(50, loaded.EncodeCrfMax);
            Assert.AreEqual(95.0, loaded.EncodeQualityValue);
        }

        [TestMethod]
        public void LoadFromFile_Nonexistent_ReturnsNull()
        {
            var result = AppConfigHelper.LoadFromFile(@"Z:\nonexistent\file.json");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void LoadFromFile_InvalidJson_ReturnsNull()
        {
            File.WriteAllText(_tempFile, "not valid json {{{");
            var result = AppConfigHelper.LoadFromFile(_tempFile);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void SaveToFile_CreatesDirectory()
        {
            string deepPath = Path.Combine(Path.GetTempPath(), $"test_deep_{Guid.NewGuid():N}", "sub", "config.json");
            try
            {
                AppConfigHelper.SaveToFile(new AppConfig(), deepPath);
                Assert.IsTrue(File.Exists(deepPath));
            }
            finally
            {
                string dir = Path.GetDirectoryName(deepPath)!;
                while (dir.Length > Path.GetTempPath().Length)
                {
                    if (Directory.Exists(dir))
                    {
                        Directory.Delete(dir, true);
                    }
                    dir = Path.GetDirectoryName(dir)!;
                }
            }
        }

        [TestMethod]
        public void SaveAndLoad_EncodingFields_RoundTrip()
        {
            var original = new AppConfig
            {
                EncodeEncoder = "libsvtav1",
                EncodePreset = "extreme",
                EncodeJobs = 8,
                EncodeSearch = true,
                EncodeCrfMin = 10,
                EncodeCrfMax = 50,
                EncodeQualityValue = 95.0,
                EncodeChroma = "444",
                EncodeBitDepth = "10",
                EncodeSweep = true,
            };
            AppConfigHelper.SaveToFile(original, _tempFile);
            var loaded = AppConfigHelper.LoadFromFile(_tempFile);
            Assert.IsNotNull(loaded);
            Assert.AreEqual("libsvtav1", loaded!.EncodeEncoder);
            Assert.AreEqual("extreme", loaded.EncodePreset);
            Assert.AreEqual(8, loaded.EncodeJobs);
            Assert.IsTrue(loaded.EncodeSearch);
            Assert.AreEqual(10, loaded.EncodeCrfMin);
            Assert.AreEqual(50, loaded.EncodeCrfMax);
            Assert.AreEqual("444", loaded.EncodeChroma);
            Assert.AreEqual("10", loaded.EncodeBitDepth);
            Assert.IsTrue(loaded.EncodeSweep);
        }

        [TestMethod]
        public void Serialize_IncludesEncodingFields()
        {
            var cfg = new AppConfig
            {
                EncodeEncoder = "libaom-av1",
                EncodeSweep = true,
                EncodeConflict = 1,
            };

            string json = JsonSerializer.Serialize(cfg);
            Assert.Contains("EncodeEncoder", json);
            Assert.Contains("EncodeSweep", json);
            Assert.Contains("EncodeConflict", json);
        }
    }
}
