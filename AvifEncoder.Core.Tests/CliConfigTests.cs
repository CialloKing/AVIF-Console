using System.Reflection;

namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class CliConfigTests
    {
        [TestMethod]
        public void BuildPresetConfig_TargetXpsnrHonorsExplicitChannelMetric()
        {
            PresetConfig config = BuildConfig(
                "--metric", "xpsnr_y",
                "--target-xpsnr", "48",
                "--dry-run");

            Assert.AreEqual("xpsnr_y", config.MetricMode);
            Assert.AreEqual(48, config.XpsnrTargetValue!.Value, 0.001);
            Assert.AreEqual("y", config.XpsnrTargetChannel);
        }

        [TestMethod]
        public void BuildPresetConfig_RepeatedMetricClearsPreviousAdvancedMetric()
        {
            PresetConfig config = BuildConfig(
                "--metric", "ssimu2",
                "--metric", "vmaf",
                "--quality", "95",
                "--dry-run");

            Assert.AreEqual("vmaf", config.MetricMode);
            Assert.AreEqual(95, config.NativeTargetValue!.Value, 0.001);
            Assert.IsNull(config.Ssimu2TargetValue);
        }

        private static PresetConfig BuildConfig(params string[] args)
        {
            Type programType = typeof(PresetConfig).Assembly
                .GetType("AvifEncoder.Program")
                ?? Type.GetType("AvifEncoder.Program, 图片avif压缩控制台")
                ?? throw new AssertFailedException("CLI Program type not found.");

            MethodInfo parse = programType.GetMethod("ParseCommandLineArgs",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new AssertFailedException("ParseCommandLineArgs not found.");
            MethodInfo build = programType.GetMethod("BuildPresetConfig",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new AssertFailedException("BuildPresetConfig not found.");

            object opts = parse.Invoke(null, new object[] { args })
                ?? throw new AssertFailedException("CLI parser returned null.");
            return (PresetConfig)build.Invoke(null, new[] { opts })!;
        }
    }
}
