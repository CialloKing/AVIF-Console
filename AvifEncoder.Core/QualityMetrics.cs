namespace AvifEncoder
{
    /// <summary> 一次 libvmaf 计算得到的全部常用指标 </summary>
    public sealed class QualityMetrics
    {
        public double SSIM { get; set; }
        public double PSNR_Y { get; set; }
        public double MS_SSIM { get; set; }
        public double VMAF { get; set; }

        // ---- 新增：XPSNR 各通道分数 ----
        public double? XPSNR_Y { get; set; }
        public double? XPSNR_U { get; set; }
        public double? XPSNR_V { get; set; }

        /// <summary>加权 XPSNR (Y:U:V = 6:1:1)，未计算时返回 null</summary>
        public double? W_XPSNR { get; set; }

        // ★ 新增高级指标
        public double? SSIMULACRA2 { get; set; }
        public double? Butteraugli_Raw { get; set; }
        public double? Butteraugli_3norm { get; set; }
        public double? GMSD { get; set; }

        // CAMBI/ADM 暂不可用，择机恢复
        // public double? CAMBI { get; set; }
        // public double? ADM { get; set; }
    }
}
