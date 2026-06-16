using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class PixelFormatTests
    {
        [TestMethod]
        public void BuildPixFmtAttempts_NoAlpha_Standard()
        {
            var cfg = new PresetConfig { BitDepth = 8 };
            var fmts = AvifPipeline.BuildPixFmtAttempts(cfg, "yuv444p", false);

            Assert.IsGreaterThanOrEqualTo(1, fmts.Count);
            Assert.Contains("yuv444p", fmts);
        }

        [TestMethod]
        public void BuildPixFmtAttempts_Alpha_ReturnsMoreFormats()
        {
            var cfg = new PresetConfig { BitDepth = 8 };
            var fmtsNoAlpha = AvifPipeline.BuildPixFmtAttempts(cfg, "yuv444p", false);
            var fmtsAlpha = AvifPipeline.BuildPixFmtAttempts(cfg, "yuv444p", true);

            Assert.IsGreaterThanOrEqualTo(fmtsAlpha.Count,
fmtsNoAlpha.Count, $"Alpha should produce at least as many formats (got {fmtsAlpha.Count} vs {fmtsNoAlpha.Count})");
        }

        [TestMethod]
        public void BuildPixFmtAttempts_10Bit_ProducesOutput()
        {
            var cfg = new PresetConfig { BitDepth = 10 };
            var fmts = AvifPipeline.BuildPixFmtAttempts(cfg, "yuv420p", false);

            Assert.IsGreaterThanOrEqualTo(1, fmts.Count);
            Assert.IsTrue(fmts.All(f => f.Length > 0), "All formats should be non-empty strings");
        }

        [TestMethod]
        public void BuildPixFmtAttempts_ReturnsUniqueFormats()
        {
            var cfg = new PresetConfig { BitDepth = 8 };
            var fmts = AvifPipeline.BuildPixFmtAttempts(cfg, "yuv444p", true);

            var distinct = new HashSet<string>(fmts);
            Assert.HasCount(distinct.Count, fmts, "Formats should be unique");
        }

        [TestMethod]
        public void BuildPixFmtAttempts_AllFormatsContainChroma()
        {
            var cfg = new PresetConfig { BitDepth = 8 };
            var fmts = AvifPipeline.BuildPixFmtAttempts(cfg, "yuv444p", false);

            foreach (var fmt in fmts)
            {
                Assert.IsTrue(fmt.Contains("444") || fmt.Contains("420") || fmt.Contains("422"),
                    $"Format {fmt} doesn't contain chroma info");
            }
        }

        [TestMethod]
        public void BuildPixFmtAttempts_ManualSource_WithAlpha_DowngradesChroma()
        {
            // ★ 关闭 AutoSource 才能覆盖多格式降级核心逻辑
            var cfg = new PresetConfig { BitDepth = 8, AutoSource = false };
            var fmts = AvifPipeline.BuildPixFmtAttempts(cfg, "yuva444p", true);

            Assert.IsGreaterThanOrEqualTo(3, fmts.Count,
                $"Manual source with alpha should produce at least 3 formats, got {fmts.Count}: {string.Join(", ", fmts)}");
            Assert.Contains("yuva444p", fmts);
            Assert.Contains("yuva420p", fmts);
        }
    }
}
