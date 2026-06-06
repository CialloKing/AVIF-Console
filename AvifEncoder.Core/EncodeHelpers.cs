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
            string tunePart = cfg.Lossless ? "" : encoder.BuildFullTuneArg(cfg.MetricMode);

            return string.Join(" ", new[] { speedArg, tunePart, tilePart, rowMt }
                .Where(s => s.Length > 0));
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

        /// <summary>Windows 长路径前缀转换，处理相对路径和 UNC 路径。</summary>
        public static string EnsureLongPath(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                if (path.StartsWith(@"\\?\"))
                    return path;
                string full = Path.GetFullPath(path);
                if (full.StartsWith(@"\\") && !full.StartsWith(@"\\?\"))
                    return @"\\?\UNC" + full.Substring(1);
                else
                    return @"\\?\" + full;
            }
            return path;
        }

        /// <summary>为外部工具参数转义文件名中的特殊字符（双引号 + 末尾反斜杠）</summary>
        public static string EscapeArg(string path)
        {
            string escaped = path.Replace("\"", "\\\"");
            // 防止末尾反斜杠与闭合引号组合产生歧义（"C:\test\" → 反斜杠转义闭合引号）
            if (escaped.EndsWith('\\'))
                escaped += "\\";
            return escaped;
        }

        /// <summary>外部工具不接受 \\?\ 前缀，需要剥离。正确处理 UNC 路径 \\?\UNC\... → \\... </summary>
        public static string NormalizePathForExternalTool(string path)
        {
            if (OperatingSystem.IsWindows() && path.StartsWith(@"\\?\"))
            {
                // \\?\UNC\server\share\path → \\server\share\path (UNC)
                if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                    return @"\" + path[7..];  // "\\?\UNC\" 共 8 字符，path[7..] 从末尾 \ 开始截取
                // \\?\C:\path → C:\path (普通路径)
                return path[4..];
            }
            return path;
        }

        /// <summary>CSV 字段转义（处理逗号、引号、换行符、回车符）</summary>
        public static string CsvEscape(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
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
