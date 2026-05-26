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
}
