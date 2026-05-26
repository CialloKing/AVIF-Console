using System;
using System.IO;

namespace AvifEncoder
{
    public static class EncodeHelpers
    {
        /// <summary>libaom 独享 row-mt，其他编码器返回空</summary>
        public static string GetRowMtArg(PresetConfig cfg)
        {
            var encoder = Av1EncoderFactory.Get(cfg.Encoder);
            if (!encoder.SupportsRowMt)
                return "";
            return cfg.SerialEncode ? "-row-mt 0" : "-row-mt 1";
        }

        /// <summary>计算最大合法 tile-columns 值（log2）</summary>
        public static int GetMaxLegalTileCols(int imageWidth, int minTileWidth = 256)
        {
            if (imageWidth < minTileWidth) return 0;
            int maxTiles = imageWidth / minTileWidth;
            if (maxTiles < 1) return 0;
            return (int)Math.Floor(Math.Log2(maxTiles));
        }

        /// <summary>计算最小合法 tile-columns 值</summary>
        public static int GetMinLegalTileCols(int imageWidth)
        {
            if (imageWidth <= 0)
            {
                return 0;
            }
            int cols = 0;
            int tileW = imageWidth;
            while (tileW > 4096)
            {
                cols++;
                tileW = (int)Math.Ceiling(imageWidth / (double)(1 << cols));
            }
            return cols;
        }

        /// <summary>构建 tile 分片参数字符串</summary>
        public static string TilePart(int tileCols, bool isTrueLossless)
        {
            return isTrueLossless
                ? "-tile-columns 0 -tile-rows 0"
                : $"-tile-columns {tileCols} -tile-rows 0";
        }

        /// <summary>CRF 值钳制</summary>
        public static int ClampCrf(int value) => Math.Clamp(value, 0, 63);

        /// <summary>编码器特定参数构建（使用 IAv1Encoder 接口）</summary>
        public static string BuildEncoderSpecificArgs(PresetConfig cfg, int cpuUsed, string tilePart, string rowMt)
        {
            var encoder = Av1EncoderFactory.Get(cfg.Encoder);
            string speedArg = encoder.BuildSpeedArg(cpuUsed);
            string tunePart = "";

            if (!cfg.Lossless)
            {
                string tune = encoder.BuildTuneArg(cfg.MetricMode);
                if (tune.Length > 0)
                {
                    if (encoder is SvtAv1Encoder)
                    {
                        tunePart = $"-svtav1-params \"tune={tune}:keyint=1:avif=1:film-grain=0:enable-qm=1:qm-min=0:qm-max=8\"";
                    }
                    else if (encoder is Rav1eEncoder)
                    {
                        tunePart = $"--tune {tune}";
                    }
                    else if (encoder is LibAomEncoder)
                    {
                        tunePart = $"-aom-params {tune}";
                    }
                }
                else if (encoder is SvtAv1Encoder)
                {
                    tunePart = "-svtav1-params \"tune=3:keyint=1:avif=1:film-grain=0:enable-qm=1:qm-min=0:qm-max=8\"";
                }
            }

            if (tunePart.Length > 0)
                return $"{speedArg} {tunePart} {tilePart} {rowMt}";
            return $"{speedArg} {tilePart} {rowMt}";
        }

        /// <summary>是否为 JPEG 文件</summary>
        public static bool IsJpeg(string path)
        {
            return Path.GetExtension(path).ToLower() is ".jpg" or ".jpeg";
        }

        /// <summary>规范化路径用于缓存键</summary>
        public static string GetNormalizedPathForCache(string input)
        {
            try
            {
                string full = Path.GetFullPath(input).Trim();
                full = EnsureLongPath(full);
                return OperatingSystem.IsWindows() ? full.ToLowerInvariant() : full;
            }
            catch
            {
                return $"__fallback__{Path.GetFileName(input).ToLowerInvariant()}";
            }
        }

        /// <summary>Windows 长路径前缀转换</summary>
        public static string EnsureLongPath(string path)
        {
            if (OperatingSystem.IsWindows() && path.Length >= 260 && !path.StartsWith(@"\\?\"))
                return @"\\?\" + path;
            return path;
        }

        /// <summary>外部工具不接受 \\?\ 前缀，需要剥离</summary>
        public static string NormalizePathForExternalTool(string path)
        {
            if (OperatingSystem.IsWindows() && path.StartsWith(@"\\?\"))
                return path[4..];
            return path;
        }

        /// <summary>CSV 字段转义</summary>
        public static string CsvEscape(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
                return $"\"{field.Replace("\"", "\"\"")}\"";
            return field;
        }

        /// <summary>文件大小格式化</summary>
        public static string FormatSize(long b)
        {
            return b switch
            {
                >= 1_048_576 => $"{b / 1_048_576.0:F2} MB",
                >= 1024 => $"{b / 1024.0:F2} KB",
                _ => $"{b} B"
            };
        }

        /// <summary>SHA256 哈希并截取前 16 位 hex 字符串</summary>
        public static string Sha256(string text)
        {
            byte[] hash = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(text));
            return Convert.ToHexString(hash)[..16];
        }

        /// <summary>时间跨度格式化</summary>
        public static string FormatTimeSpan(TimeSpan t)
        {
            return t switch
            {
                { TotalHours: >= 1 } => $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s",
                { TotalMinutes: >= 1 } => $"{(int)t.TotalMinutes}m {t.Seconds}s",
                _ => $"{t.TotalSeconds:F4}s"
            };
        }
    }
}
