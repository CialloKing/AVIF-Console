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

    /// <summary> 无损验证失败类型 </summary>
    public enum VerificationFailureType
    {
        SizeMismatch,        // 解码后尺寸与原图不同
        AlphaMismatch,       // 仅 Alpha 通道有差异（可能 rounding）
        ChromaMismatch,      // 仅色度通道有差异
        MassiveMismatch,     // 超过 50% 像素不一致
        PixelMismatch        // 其他像素级差异
    }

    /// <summary> 单次无损验证的完整报告（JSON + CSV 共用） </summary>
    public sealed class FailedVerificationInfo
    {
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

        /// <summary> 整理为人类可读摘要 </summary>
        public string ToSummary()
        {
            string channelBreakdown = $"R:{RMismatches} G:{GMismatches} B:{BMismatches} A:{AMismatches}";
            return $"FailureType={FailureType} " +
                   $"Mismatches={MismatchCount} MaxDelta={MaxDelta} " +
                   $"FirstAt=({FirstMismatchX},{FirstMismatchY}) " +
                   $"Channel={FirstMismatchChannel} " +
                   $"Ref=0x{RefValue:X2} Out=0x{OutValue:X2} " +
                   $"Breakdown=[{channelBreakdown}]";
        }
    }
}
