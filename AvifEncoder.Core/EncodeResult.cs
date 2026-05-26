using System;

namespace AvifEncoder
{
    public class EncodeResult
    {
        public int Index { get; set; }
        public string FileName { get; set; } = "";
        public string OriginalFileName { get; set; } = "";
        public long OriginalSize { get; set; }
        public long OutputSize { get; set; }
        public int UsedCRF { get; set; }
        public double FinalSSIM { get; set; }
        public double CompressionRatio => OriginalSize == 0 ? 0 : Math.Round(1.0 - (double)OutputSize / OriginalSize, 4);
        public TimeSpan EncodeTime { get; set; }
        public TimeSpan SearchTime { get; set; }
        public TimeSpan TotalTime { get; set; }
        public int Retries { get; set; }
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; } = "";
        public bool Skipped { get; set; } = false;
        public string? PixelFormat { get; set; }

        public string? SourcePixelFormat { get; set; }
        public string? Mode { get; set; }
        public bool IsSafeMode { get; set; }
        public string? AomParamsUsed { get; set; }      // 保留原有字段（可选）
        public bool CacheReused { get; set; }

        // ★ 新增：最终成功的 ffmpeg 命令字符串（便于完整审计）
        public string? CommandLine { get; set; }

        public string? AdvancedMetricsCacheKey { get; set; }




        // ★ 新增多指标字段
        public double? FinalVMAF { get; set; }
        public double? FinalPSNR_Y { get; set; }
        public double? FinalMSSSIM { get; set; }
        public double? FinalMixScore { get; set; }

        // ---- 新增：XPSNR 分数 ----
        public double? FinalXPSNR_Y { get; set; }
        public double? FinalXPSNR_U { get; set; }
        public double? FinalXPSNR_V { get; set; }
        public double? FinalWXPSNR { get; set; }          // 加权综合

        // ---- 新增高级指标字段 ----
        public double? FinalSSIMULACRA2 { get; set; }
        public double? FinalButteraugli_Raw { get; set; }
        public double? FinalButteraugli_3norm { get; set; }
        public double? FinalGMSD { get; set; }


        public int SearchEvaluations { get; set; }   // ★ 新增：搜索阶段实际成功评估CRF点的次数

        public string InputPath { get; set; } = "";   // 原始输入文件完整路径，用于重试

    }
}
