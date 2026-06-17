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

        [TestMethod]
        public void RestoreMaxJobsFromSnapshot_UpdatesRuntimeLimiterWhenNotUserSpecified()
        {
            var pipeline = CreatePipelineWithJobs(out var config, out string root, userSpecified: false);
            try
            {
                using var doc = JsonDocument.Parse("""{"MaxJobs":2}""");

                pipeline.RestoreMaxJobsFromSnapshot(doc.RootElement);

                Assert.AreEqual(2, config.MaxJobs);
                Assert.AreEqual(2, GetFfmpegSlotMax(pipeline));
            }
            finally
            {
                pipeline.Dispose();
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [TestMethod]
        public void RestoreMaxJobsFromSnapshot_KeepsUserSpecifiedRuntimeJobs()
        {
            var pipeline = CreatePipelineWithJobs(out var config, out string root, userSpecified: true);
            try
            {
                using var doc = JsonDocument.Parse("""{"MaxJobs":2}""");

                pipeline.RestoreMaxJobsFromSnapshot(doc.RootElement);

                Assert.AreEqual(1, config.MaxJobs);
                Assert.AreEqual(1, GetFfmpegSlotMax(pipeline));
            }
            finally
            {
                pipeline.Dispose();
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [TestMethod]
        public void SetMaxJobs_CanShrinkAndExpandWorkersRepeatedly()
        {
            var pipeline = CreatePipelineWithJobs(out _, out string root, userSpecified: true);
            try
            {
                Assert.AreEqual(3, pipeline.SetMaxJobs(3));
                Assert.AreEqual(3, GetActiveWorkerTokenCount(pipeline));
                Assert.AreEqual(1, pipeline.SetMaxJobs(1));
                Assert.AreEqual(1, GetActiveWorkerTokenCount(pipeline));
                Assert.AreEqual(3, pipeline.SetMaxJobs(3));
                Assert.AreEqual(3, GetActiveWorkerTokenCount(pipeline));
            }
            finally
            {
                pipeline.Dispose();
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [TestMethod]
        public async Task SetMaxJobs_RemovesStoppedWorkerTasksAfterShrink()
        {
            var pipeline = CreatePipelineWithJobs(out _, out string root, userSpecified: true);
            try
            {
                Assert.AreEqual(4, pipeline.SetMaxJobs(4));
                await WaitForWorkerTaskCountAsync(pipeline, 4);

                Assert.AreEqual(1, pipeline.SetMaxJobs(1));
                Assert.AreEqual(1, GetActiveWorkerTokenCount(pipeline));
                await WaitForWorkerTaskCountAsync(pipeline, 1);

                Assert.AreEqual(3, pipeline.SetMaxJobs(3));
                await WaitForWorkerTaskCountAsync(pipeline, 3);

                Assert.AreEqual(1, pipeline.SetMaxJobs(1));
                await WaitForWorkerTaskCountAsync(pipeline, 1);
            }
            finally
            {
                pipeline.Dispose();
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [TestMethod]
        public void Dispose_UnsubscribesProcessExitHandler()
        {
            var pipeline = CreatePipelineWithJobs(out _, out string root, userSpecified: true);
            try
            {
                Assert.IsNotNull(GetProcessExitHandler(pipeline));

                pipeline.Dispose();

                Assert.IsNull(GetProcessExitHandler(pipeline));
            }
            finally
            {
                pipeline.Dispose();
                try { Directory.Delete(root, true); } catch { }
            }
        }

        private static AvifPipeline CreatePipelineWithJobs(
            out PresetConfig config, out string root, bool userSpecified)
        {
            root = Path.Combine(Path.GetTempPath(), $"avif_resume_jobs_{Guid.NewGuid():N}");
            string inputDir = Path.Combine(root, "input");
            string outputDir = Path.Combine(root, "output");
            Directory.CreateDirectory(inputDir);

            config = PresetConfig.CreateFromPreset(CliPreset.Fast);
            config.MaxJobs = 1;
            config.UserSpecifiedMaxJobs = true;
            var pipeline = new AvifPipeline(inputDir, outputDir, config, logger: new NullLogger());
            config.UserSpecifiedMaxJobs = userSpecified;
            return pipeline;
        }

        private static int GetFfmpegSlotMax(AvifPipeline pipeline)
        {
            var slots = typeof(AvifPipeline)
                .GetField("_ffmpegSlots", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(pipeline);
            return (int)slots!.GetType().GetProperty("CurrentMax")!.GetValue(slots)!;
        }

        private static int GetActiveWorkerTokenCount(AvifPipeline pipeline)
        {
            var tokens = typeof(AvifPipeline)
                .GetField("_fileWorkerTokens", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(pipeline);
            return (int)tokens!.GetType().GetProperty("Count")!.GetValue(tokens)!;
        }

        private static EventHandler? GetProcessExitHandler(AvifPipeline pipeline)
            => (EventHandler?)typeof(AvifPipeline)
                .GetField("_processExitHandler", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(pipeline);

        private static int GetWorkerTaskCount(AvifPipeline pipeline)
        {
            var workers = typeof(AvifPipeline)
                .GetField("_fileWorkers", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(pipeline);
            return (int)workers!.GetType().GetProperty("Count")!.GetValue(workers)!;
        }

        private static async Task WaitForWorkerTaskCountAsync(AvifPipeline pipeline, int expected)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline)
            {
                if (GetWorkerTaskCount(pipeline) == expected)
                    return;
                await Task.Delay(20);
            }

            Assert.AreEqual(expected, GetWorkerTaskCount(pipeline));
        }
    }
}
