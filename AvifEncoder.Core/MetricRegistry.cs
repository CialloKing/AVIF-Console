using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AvifEncoder
{
    /// <summary>
    /// 指标计算注册项，新增指标只需在 MetricRegistry 的构造函数中添加一行。
    /// </summary>
    public sealed class MetricDef
    {
        /// <summary>指标标识（如 "vmaf", "ssim", "psnr", "xpsnr"...）</summary>
        public string Key { get; }

        /// <summary>GUI 显示名称</summary>
        public string DisplayName { get; }

        /// <summary>是否为"越小越好"型指标（butter3, gmsd 为 true）</summary>
        public bool LowerIsBetter { get; }

        /// <summary>是否属于高级指标（需要外部工具或自定义实现）</summary>
        public bool IsAdvanced { get; }

        /// <summary>从 QualityMetrics 提取原生分值</summary>
        public Func<QualityMetrics, double> GetScore { get; }

        public MetricDef(string key, string displayName, bool lowerIsBetter, bool isAdvanced,
            Func<QualityMetrics, double> getScore)
        {
            Key = key;
            DisplayName = displayName;
            LowerIsBetter = lowerIsBetter;
            IsAdvanced = isAdvanced;
            GetScore = getScore;
        }
    }

    /// <summary>全局指标注册表</summary>
    public static class MetricRegistry
    {
        /// <summary>指标名 → 指标定义</summary>
        private static readonly Dictionary<string, MetricDef> _metrics = new(StringComparer.OrdinalIgnoreCase);

        static MetricRegistry()
        {
            // 常用基础指标
            Register(new MetricDef("vmaf", "VMAF", false, false, m => m.VMAF));
            Register(new MetricDef("ssim", "SSIM", false, false, m => m.SSIM));
            Register(new MetricDef("psnr", "PSNR-Y", false, false, m => m.PSNR_Y));
            Register(new MetricDef("msssim", "MS-SSIM", false, false, m => m.MS_SSIM));
            // XPSNR 系列
            Register(new MetricDef("xpsnr", "XPSNR (W)", false, false, m => m.W_XPSNR ?? double.NaN));
            // 高级指标
            Register(new MetricDef("ssimu2", "SSIMULACRA2", false, true, m => m.SSIMULACRA2 ?? double.NaN));
            Register(new MetricDef("butter3", "Butteraugli 3norm", true, true, m => m.Butteraugli_3norm ?? double.NaN));
            Register(new MetricDef("gmsd", "GMSD", true, true, m => m.GMSD ?? double.NaN));
            // CAMBI/ADM 暂未在此 ffmpeg 构建中可用，择机恢复
            // Register(new MetricDef("cambi",   "CAMBI",             true,  false, m => m.CAMBI ?? double.NaN));
            // Register(new MetricDef("adm",     "ADM",               true,  false, m => m.ADM ?? double.NaN));
            // XPSNR 子通道（较少使用）
            Register(new MetricDef("xpsnr_y", "XPSNR-Y", false, false, m => m.XPSNR_Y ?? double.NaN));
            Register(new MetricDef("xpsnr_u", "XPSNR-U", false, false, m => m.XPSNR_U ?? double.NaN));
            Register(new MetricDef("xpsnr_v", "XPSNR-V", false, false, m => m.XPSNR_V ?? double.NaN));
            Register(new MetricDef("xpsnr_w", "XPSNR (W)", false, false, m => m.W_XPSNR ?? double.NaN));
            // 末尾 — 综合评分，依赖多项基础指标，放在最后
            Register(new MetricDef("mix", "MixScore", false, false, m => double.IsNaN(m.VMAF) ? double.NaN : ComputeMixScore(m)));
        }

        private static void Register(MetricDef def)
        {
            _metrics[def.Key] = def;
        }

        /// <summary>所有已注册的指标键</summary>
        public static IEnumerable<string> AllKeys => _metrics.Keys;

        /// <summary>获取指标定义，不存在返回 null</summary>
        public static MetricDef? Get(string key)
        {
            _metrics.TryGetValue(key ?? "", out var def);
            return def;
        }

        /// <summary>是否为高级指标</summary>
        public static bool IsAdvanced(string? key)
        {
            return Get(key ?? "")?.IsAdvanced ?? false;
        }

        /// <summary>是否越小越好</summary>
        public static bool IsLowerBetter(string? key)
        {
            return Get(key ?? "")?.LowerIsBetter ?? false;
        }

        /// <summary>从 QualityMetrics 提取原生分值，失败返回 -1</summary>
        public static double GetScore(QualityMetrics m, string key)
        {
            var def = Get(key);
            if (def == null) return double.IsNaN(m.SSIM) ? -1 : m.SSIM;  // 未知模式回退到 SSIM
            double score = def.GetScore(m);
            return double.IsNaN(score) ? -1 : score;
        }

        internal static double ComputeMixScore(QualityMetrics m)
        {
            if (double.IsNaN(m.VMAF) || double.IsNaN(m.PSNR_Y) ||
                double.IsNaN(m.SSIM) || double.IsNaN(m.MS_SSIM))
                return double.NaN;
            double vmafNorm = m.VMAF / 100.0;
            double psnrNorm = Math.Clamp((m.PSNR_Y - 30) / 20.0, 0, 1);
            if (m.W_XPSNR.HasValue && !double.IsNaN(m.W_XPSNR.Value))
            {
                double xpsnrNorm = Math.Clamp((m.W_XPSNR.Value - 40) / 20.0, 0, 1);
                return 0.50 * vmafNorm + 0.05 * m.SSIM + 0.08 * m.MS_SSIM + 0.05 * psnrNorm + 0.32 * xpsnrNorm;
            }
            return 0.80 * vmafNorm + 0.05 * m.SSIM + 0.10 * m.MS_SSIM + 0.05 * psnrNorm;
        }
    }
}
