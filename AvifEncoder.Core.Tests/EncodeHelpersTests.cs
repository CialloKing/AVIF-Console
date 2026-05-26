using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AvifEncoder.Core.Tests
{
    [TestClass]
    public class EncodeHelpersTests
    {
        #region ClampCrf

        [TestMethod]
        public void ClampCrf_AboveMax_Returns63()
        {
            Assert.AreEqual(63, EncodeHelpers.ClampCrf(67));
            Assert.AreEqual(63, EncodeHelpers.ClampCrf(int.MaxValue));
        }

        [TestMethod]
        public void ClampCrf_BelowMin_Returns0()
        {
            Assert.AreEqual(0, EncodeHelpers.ClampCrf(-5));
            Assert.AreEqual(0, EncodeHelpers.ClampCrf(int.MinValue));
        }

        [TestMethod]
        public void ClampCrf_InRange_ReturnsSame()
        {
            Assert.AreEqual(0, EncodeHelpers.ClampCrf(0));
            Assert.AreEqual(30, EncodeHelpers.ClampCrf(30));
            Assert.AreEqual(63, EncodeHelpers.ClampCrf(63));
        }

        #endregion

        #region IsJpeg

        [TestMethod]
        public void IsJpeg_JpgExtension_ReturnsTrue()
        {
            Assert.IsTrue(EncodeHelpers.IsJpeg("photo.jpg"));
            Assert.IsTrue(EncodeHelpers.IsJpeg("photo.JPEG"));
            Assert.IsTrue(EncodeHelpers.IsJpeg("photo.Jpg"));
            Assert.IsTrue(EncodeHelpers.IsJpeg("/path/to/photo.jpeg"));
        }

        [TestMethod]
        public void IsJpeg_OtherExtension_ReturnsFalse()
        {
            Assert.IsFalse(EncodeHelpers.IsJpeg("photo.png"));
            Assert.IsFalse(EncodeHelpers.IsJpeg("photo.webp"));
            Assert.IsFalse(EncodeHelpers.IsJpeg("photo.avif"));
            Assert.IsFalse(EncodeHelpers.IsJpeg("photo"));
        }

        #endregion

        #region FormatSize

        [TestMethod]
        public void FormatSize_Bytes()
        {
            Assert.AreEqual("512 B", EncodeHelpers.FormatSize(512));
            Assert.AreEqual("0 B", EncodeHelpers.FormatSize(0));
            Assert.AreEqual("1023 B", EncodeHelpers.FormatSize(1023));
        }

        [TestMethod]
        public void FormatSize_KB()
        {
            Assert.AreEqual("1.00 KB", EncodeHelpers.FormatSize(1024));
            Assert.AreEqual("1.50 KB", EncodeHelpers.FormatSize(1536));
            Assert.AreEqual("1024.00 KB", EncodeHelpers.FormatSize(1024 * 1024 - 1));
        }

        [TestMethod]
        public void FormatSize_MB()
        {
            Assert.AreEqual("1.00 MB", EncodeHelpers.FormatSize(1048576));
            Assert.AreEqual("2.50 MB", EncodeHelpers.FormatSize(2621440));
        }

        #endregion

        #region FormatTimeSpan

        [TestMethod]
        public void FormatTimeSpan_Seconds()
        {
            var t = TimeSpan.FromSeconds(45.5);
            string result = EncodeHelpers.FormatTimeSpan(t);
            Assert.Contains("45.5000s", result);
        }

        [TestMethod]
        public void FormatTimeSpan_Minutes()
        {
            var t = TimeSpan.FromMinutes(3.5);
            string result = EncodeHelpers.FormatTimeSpan(t);
            Assert.IsTrue(result.Contains("3m") && result.Contains("30s"));
        }

        [TestMethod]
        public void FormatTimeSpan_Hours()
        {
            var t = TimeSpan.FromHours(2.5);
            string result = EncodeHelpers.FormatTimeSpan(t);
            Assert.IsTrue(result.Contains("2h") && result.Contains("30m"));
        }

        #endregion

        #region CsvEscape

        [TestMethod]
        public void CsvEscape_Comma_WrapsInQuotes()
        {
            Assert.AreEqual("\"a,b\"", EncodeHelpers.CsvEscape("a,b"));
        }

        [TestMethod]
        public void CsvEscape_Quote_DoublesQuote()
        {
            Assert.AreEqual("\"a\"\"b\"", EncodeHelpers.CsvEscape("a\"b"));
        }

        [TestMethod]
        public void CsvEscape_Newline_WrapsInQuotes()
        {
            Assert.AreEqual("\"a\nb\"", EncodeHelpers.CsvEscape("a\nb"));
        }

        [TestMethod]
        public void CsvEscape_Plain_ReturnsSame()
        {
            Assert.AreEqual("hello", EncodeHelpers.CsvEscape("hello"));
        }

        [TestMethod]
        public void CsvEscape_Empty_ReturnsEmpty()
        {
            Assert.AreEqual("", EncodeHelpers.CsvEscape(""));
            Assert.AreEqual("", EncodeHelpers.CsvEscape(null!));
        }

        #endregion

        #region NormalizePathForExternalTool

        [TestMethod]
        public void NormalizePathForExternalTool_RemovesLongPathPrefix()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.AreEqual(@"C:\path", EncodeHelpers.NormalizePathForExternalTool(@"\\?\C:\path"));
            }
        }

        [TestMethod]
        public void NormalizePathForExternalTool_KeepsNormalPath()
        {
            Assert.AreEqual(@"C:\path", EncodeHelpers.NormalizePathForExternalTool(@"C:\path"));
        }

        #endregion

        #region GetRowMtArg

        [TestMethod]
        public void GetRowMtArg_LibAom_SerialEncode_ReturnsRowMt0()
        {
            var cfg = new PresetConfig { Encoder = "libaom-av1", SerialEncode = true };
            Assert.AreEqual("-row-mt 0", EncodeHelpers.GetRowMtArg(cfg));
        }

        [TestMethod]
        public void GetRowMtArg_LibAom_NonSerial_ReturnsRowMt1()
        {
            var cfg = new PresetConfig { Encoder = "libaom-av1", SerialEncode = false };
            Assert.AreEqual("-row-mt 1", EncodeHelpers.GetRowMtArg(cfg));
        }

        [TestMethod]
        public void GetRowMtArg_SvtAv1_ReturnsEmpty()
        {
            var cfg = new PresetConfig { Encoder = "libsvtav1" };
            Assert.AreEqual("", EncodeHelpers.GetRowMtArg(cfg));
        }

        #endregion

        #region Sha256

        [TestMethod]
        public void Sha256_SameInput_ReturnsSameHash()
        {
            string h1 = EncodeHelpers.Sha256("test");
            string h2 = EncodeHelpers.Sha256("test");
            Assert.AreEqual(h1, h2);
            Assert.AreEqual(16, h1.Length);
        }

        [TestMethod]
        public void Sha256_DifferentInput_ReturnsDifferentHash()
        {
            string h1 = EncodeHelpers.Sha256("test1");
            string h2 = EncodeHelpers.Sha256("test2");
            Assert.AreNotEqual(h1, h2);
        }

        #endregion

        #region BuildEncoderSpecificArgs

        [TestMethod]
        public void BuildEncoderSpecificArgs_LibAom_IncludesAllParts()
        {
            var cfg = new PresetConfig { Encoder = "libaom-av1", MetricMode = "ssim" };
            string result = EncodeHelpers.BuildEncoderSpecificArgs(cfg, 4, "-tile-columns 2 -tile-rows 0", "-row-mt 1");
            Assert.Contains("-cpu-used 4", result);
            Assert.Contains("-row-mt 1", result);
            Assert.Contains("-tile-columns 2", result);
        }

        [TestMethod]
        public void BuildEncoderSpecificArgs_Hardware_OnlyNonEmptyParts()
        {
            var cfg = new PresetConfig { Encoder = "av1_nvenc" };
            string result = EncodeHelpers.BuildEncoderSpecificArgs(cfg, 0, "", "");
            Assert.AreEqual("", result);
        }

        [TestMethod]
        public void BuildEncoderSpecificArgs_Lossless_NoTune()
        {
            var cfg = new PresetConfig { Encoder = "libaom-av1", Lossless = true };
            string result = EncodeHelpers.BuildEncoderSpecificArgs(cfg, 0, "", "-row-mt 1");
            Assert.DoesNotContain("tune", result);
        }

        #endregion
    }
}
