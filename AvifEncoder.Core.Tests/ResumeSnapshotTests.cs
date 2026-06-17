using System.Reflection;
using System.Text.Json;

namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class ResumeSnapshotTests
    {
        [TestMethod]
        public void RestoreXpsnrTargetChannelFromSnapshot_UsesPersistedChannel()
        {
            var config = new PresetConfig { MetricMode = "xpsnr", XpsnrTargetChannel = "w" };
            using var doc = JsonDocument.Parse("""{"XpsnrTargetChannel":"Y"}""");

            AvifPipeline.RestoreXpsnrTargetChannelFromSnapshot(config, doc.RootElement);

            Assert.AreEqual("y", config.XpsnrTargetChannel);
        }

        [TestMethod]
        public void RestoreXpsnrTargetChannelFromSnapshot_FallsBackToMetricModeForOldSnapshots()
        {
            var config = new PresetConfig { MetricMode = "xpsnr_v" };
            using var doc = JsonDocument.Parse("""{}""");

            AvifPipeline.RestoreXpsnrTargetChannelFromSnapshot(config, doc.RootElement);

            Assert.AreEqual("v", config.XpsnrTargetChannel);
        }

        [TestMethod]
        public void SaveSnapshot_PersistsXpsnrTargetChannel()
        {
            string root = Path.Combine(Path.GetTempPath(), $"avif_resume_snapshot_{Guid.NewGuid():N}");
            string inputDir = Path.Combine(root, "input");
            string outputDir = Path.Combine(root, "output");
            Directory.CreateDirectory(inputDir);

            var config = PresetConfig.CreateFromPreset(CliPreset.Fast);
            config.SetQualityTarget(47.5, "xpsnr_u");
            config.MaxJobs = 1;

            try
            {
                using var pipeline = new AvifPipeline(inputDir, outputDir, config,
                    logger: new NullLogger());
                typeof(AvifPipeline)
                    .GetMethod("SaveSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(pipeline, new object?[] { Array.Empty<string>(), null });

                string snapshotPath = Path.Combine(outputDir, ".session", "snapshot.json");
                using var doc = JsonDocument.Parse(File.ReadAllText(snapshotPath));
                var cfg = doc.RootElement.GetProperty("config");

                Assert.AreEqual("xpsnr_u", cfg.GetProperty("MetricMode").GetString());
                Assert.AreEqual(47.5, cfg.GetProperty("XpsnrTargetValue").GetDouble(), 0.001);
                Assert.AreEqual("u", cfg.GetProperty("XpsnrTargetChannel").GetString());
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }
    }
}
