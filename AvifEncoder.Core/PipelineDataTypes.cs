namespace AvifEncoder
{
    internal class EncodingInfo
    {
        public string SourcePixFmt { get; set; } = "";
        public string ActualPixFmt { get; set; } = "";
        public string PixInfo { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsTrulyLossless { get; set; }
        public bool IsLosslessMode { get; set; }
        public int TileCols { get; set; }
        public int BaseCrf { get; set; }
        public bool HasAlpha { get; set; } = false;
    }

    internal class CRFSearchResult
    {
        public int Crf;
        public string ActualPixFmt = "";
        public TimeSpan SearchTime;
        public bool SearchBasedCRF;
        public bool UseSafeModeFinalEncode;
        public int SearchEvalCount;
    }

    internal class FinalEncodeResult
    {
        public bool Success;
        public int Crf;
        public string ActualPixFmt = "";
        public TimeSpan EncodeTime;
        public int Retries;
        public string FailReason = "";
        public bool FromCache;
        public string? ActualAom;
        public string? FinalCommand;
        public bool UseSafeMode;
        public DateTime StartTime;
    }

    /// <summary> 单个像素差异记录（JSON 扩展字段） </summary>
    public struct MismatchSample
    {
        public int X { get; set; }
        public int Y { get; set; }
        public string Channel { get; set; }
        public int RefValue { get; set; }
        public int OutValue { get; set; }
        public int Delta { get; set; }
    }

    /// <summary> 无损验证失败类型 </summary>
    public enum VerificationFailureType
    {
        SizeMismatch,        // 解码后尺寸与原图不同
        AlphaMismatch,       // 仅 Alpha 通道有差异（可能 rounding）
        ChromaMismatch,      // 仅色度通道有差异
        MassiveMismatch,     // 超过 50% 像素不一致
        PixelMismatch        // 其他像素级差异
    }

    /// <summary> 单次无损验证的完整报告（JSON 详细诊断 + CSV 索引共用） </summary>
    public sealed class FailedVerificationInfo
    {
        // ===== CSV 索引字段 =====
        public string SourceFile { get; set; } = "";
        public string FailedOutput { get; set; } = "";
        public string Encoder { get; set; } = "";
        public string EncoderVersion { get; set; } = "";
        public string PixelFormat { get; set; } = "";
        public int BitDepth { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public VerificationFailureType FailureType { get; set; }
        public int MismatchCount { get; set; }
        public int MaxDelta { get; set; }
        public int FirstMismatchX { get; set; }
        public int FirstMismatchY { get; set; }
        public string FirstMismatchChannel { get; set; } = "";
        public int RefValue { get; set; }
        public int OutValue { get; set; }
        public int RMismatches { get; set; }
        public int GMismatches { get; set; }
        public int BMismatches { get; set; }
        public int AMismatches { get; set; }
        public string EncodeCommand { get; set; } = "";
        public string Timestamp { get; set; } = "";

        // ===== JSON 扩展字段（不在 CSV 中） =====
        /// <summary>差异像素占图像总像素的比例</summary>
        public double MismatchRatio { get; set; }
        /// <summary>各通道差异占比（百分比）</summary>
        public double RPct { get; set; }
        public double GPct { get; set; }
        public double BPct { get; set; }
        public double APct { get; set; }
        /// <summary>源文件像素格式</summary>
        public string SourcePixelFormat { get; set; } = "";
        /// <summary>AVIF 文件大小（字节）</summary>
        public long OutputFileSize { get; set; }
        /// <summary>验证耗时（秒）</summary>
        public double VerificationTimeSec { get; set; }
        /// <summary>差异热力图相对路径（为空时未生成）</summary>
        public string? DiffHeatmapPath { get; set; }
        /// <summary>编码耗时（秒）</summary>
        public double EncodeTimeSec { get; set; }
        /// <summary>前 N 个差异像素的精确采样（最多 500 条）</summary>
        public List<MismatchSample> MismatchSamples { get; set; } = [];

        /// <summary> 整理为人类可读摘要 </summary>
        public string ToSummary()
        {
            double totalPixels = Width * (double)Height;
            string pctStr = totalPixels > 0
                ? $"{MismatchRatio:P2}"
                : "N/A";
            string channelBreakdown =
                $"R:{RMismatches}({RPct:F1}%) " +
                $"G:{GMismatches}({GPct:F1}%) " +
                $"B:{BMismatches}({BPct:F1}%) " +
                $"A:{AMismatches}({APct:F1}%)";
            return $"FailureType={FailureType} " +
                   $"Mismatches={MismatchCount} ({pctStr}) " +
                   $"MaxDelta={MaxDelta} " +
                   $"FirstAt=({FirstMismatchX},{FirstMismatchY}) " +
                   $"Channel={FirstMismatchChannel} " +
                   $"Ref=0x{RefValue:X2} Out=0x{OutValue:X2} " +
                   $"Breakdown=[{channelBreakdown}]";
        }
    }
}
