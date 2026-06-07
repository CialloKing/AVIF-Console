namespace AvifEncoder
{
    /// <summary> 一次 libvmaf 计算得到的全部常用指标 </summary>
    public sealed class QualityMetrics
    {
        public double SSIM { get; set; }
        public double PSNR_Y { get; set; }
        public double MS_SSIM { get; set; }
        public double VMAF { get; set; }

        public double? XPSNR_Y { get; set; }
        public double? XPSNR_U { get; set; }
        public double? XPSNR_V { get; set; }
        public double? W_XPSNR { get; set; }

        public double? SSIMULACRA2 { get; set; }
        public double? Butteraugli_Raw { get; set; }
        public double? Butteraugli_3norm { get; set; }
        public double? GMSD { get; set; }
    }
}
