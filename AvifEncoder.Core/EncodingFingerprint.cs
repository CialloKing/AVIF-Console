using System.Text.Json;

namespace AvifEncoder
{
    /// <summary>
    /// 编码参数指纹 — 统一缓存 Key 的数据结构。
    /// 新增影响编码/指标结果的参数时，编译器强制要求更新此结构，杜绝遗漏。
    /// Version 字段用于 Key 格式变更时自动失效所有旧缓存，避免新旧 Key 误匹配。
    /// </summary>
    public readonly record struct EncodingFingerprint
    {
        /// <summary>缓存 Key 格式版本。格式变更时递增此值，自动失效所有旧缓存。</summary>
        public const int CurrentVersion = 2;

        public int Version { get; init; }
        public string NormalizedPath { get; init; }
        public int Crf { get; init; }
        public string PixFmt { get; init; }
        public int TileCols { get; init; }
        public int CpuUsed { get; init; }
        public bool IsTrueLossless { get; init; }
        public string AomParams { get; init; }
        public bool IsJpeg { get; init; }
        public int BitDepth { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public string RowMt { get; init; }
        public string Encoder { get; init; }
        public string? EncoderCustomParams { get; init; }
        public int Denoise { get; init; }
        public bool ArNrUseMaxFrames { get; init; }
        public string? RgbMode { get; init; }

        /// <summary>
        /// 指标模式（仅指标缓存使用，编码缓存留空）。
        /// 非空时参与 Key 计算，使同一个编码参数在不同 metric 下的指标缓存互相隔离。
        /// </summary>
        public string? MetricMode { get; init; }

        /// <summary>
        /// 生成稳定的缓存 Key（SHA256 前 16 位 hex）。
        /// JSON 序列化自动处理特殊字符，字段缺失在编译期即可发现。
        /// </summary>
        public string ToCacheKey()
        {
            return EncodeHelpers.Sha256(JsonSerializer.Serialize(this));
        }

        // ── 工厂方法：从原始参数构建指纹 ──

        public static EncodingFingerprint ForEncode(
            string normalizedPath, int crf, string pixFmt,
            int tileCols, int cpuUsed, bool isTrueLossless,
            string aomParams, bool isJpeg, int bitDepth,
            int width, int height, string rowMt,
            string encoder, string? encoderCustomParams,
            int denoise, bool arnrUseMaxFrames, string? rgbMode)
        {
            return new EncodingFingerprint
            {
                Version = CurrentVersion,
                NormalizedPath = normalizedPath,
                Crf = crf,
                PixFmt = pixFmt,
                TileCols = tileCols,
                CpuUsed = cpuUsed,
                IsTrueLossless = isTrueLossless,
                AomParams = aomParams,
                IsJpeg = isJpeg,
                BitDepth = bitDepth,
                Width = width,
                Height = height,
                RowMt = rowMt,
                Encoder = encoder,
                EncoderCustomParams = encoderCustomParams,
                Denoise = denoise,
                ArNrUseMaxFrames = arnrUseMaxFrames,
                RgbMode = rgbMode
            };
        }

        public static EncodingFingerprint ForMetrics(
            string normalizedPath, int crf, string pixFmt,
            int tileCols, int cpuUsed, bool isTrueLossless,
            string aomParams, bool isJpeg, int bitDepth,
            int width, int height, string rowMt, string metricMode,
            string encoder, string? encoderCustomParams,
            int denoise, bool arnrUseMaxFrames, string? rgbMode)
        {
            return new EncodingFingerprint
            {
                Version = CurrentVersion,
                NormalizedPath = normalizedPath,
                Crf = crf,
                PixFmt = pixFmt,
                TileCols = tileCols,
                CpuUsed = cpuUsed,
                IsTrueLossless = isTrueLossless,
                AomParams = aomParams,
                IsJpeg = isJpeg,
                BitDepth = bitDepth,
                Width = width,
                Height = height,
                RowMt = rowMt,
                MetricMode = metricMode,
                Encoder = encoder,
                EncoderCustomParams = encoderCustomParams,
                Denoise = denoise,
                ArNrUseMaxFrames = arnrUseMaxFrames,
                RgbMode = rgbMode
            };
        }
    }
}
