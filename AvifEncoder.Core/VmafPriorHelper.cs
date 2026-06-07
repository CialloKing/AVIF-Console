using System;
using System.Collections.Generic;

namespace AvifEncoder
{
    /// <summary>
    /// 多指标先验分布表：根据目标质量指标返回建议的 CRF 中位数及搜索范围。
    /// 数据基于 400 张真实图片的 CRF 0-63 全扫描统计，使用分段线性插值，外推采用局部斜率。
    ///
    /// 支持指标：vmaf / ssim / psnr / msssim / xpsnr / ssimu2 / butter3
    /// GMSD 因离散度过高（σ=14-22）且在高 CRF 饱和，不提供先验表。
    /// </summary>
    public static class VmafPriorHelper
    {
        private readonly struct Entry
        {
            public readonly double Target;
            public readonly int Median;
            public readonly int Lo;
            public readonly int Hi;
            public readonly double StdDev;

            public Entry(double target, int median, int lo, int hi, double stdDev)
            {
                Target = target;
                Median = median;
                Lo = lo;
                Hi = hi;
                StdDev = stdDev;
            }
        }

        // (Target, Median, P10, P90, StdDev_of_optimal_CRF)
        private static readonly Dictionary<string, List<Entry>> Tables = new()
        {
            ["vmaf"] = new()
            {
                new(90, 39, 35, 45, 4.17),
                new(91, 37, 34, 44, 4.24),
                new(92, 35, 32, 41, 4.28),
                new(93, 32, 29, 39, 4.41),
                new(94, 30, 26, 36, 4.55),
                new(95, 26, 23, 32, 4.67),
                new(96, 20, 16, 27, 5.06),
            },
            ["ssim"] = new()
            {
                new(0.990, 46, 38, 60,  8.36),
                new(0.992, 44, 35, 59,  8.76),
                new(0.994, 41, 32, 57,  9.26),
                new(0.995, 39, 30, 55,  9.54),
                new(0.996, 37, 27, 53,  9.86),
                new(0.997, 34, 24, 51, 10.28),
                new(0.998, 29, 18, 46, 10.69),
            },
            ["psnr"] = new()
            {
                new(42, 26, 15, 40, 8.87),
                new(44, 19, 11, 34, 8.72),
                new(46, 13,  8, 28, 8.00),
                new(48,  9,  6, 22, 7.02),
                new(50,  6,  4, 16, 5.88),
                new(52,  4,  2, 11, 4.99),
                new(54,  2,  2,  7, 4.07),
            },
            ["msssim"] = new()
            {
                new(0.990, 40, 29, 55,  9.45),
                new(0.992, 37, 26, 53,  9.87),
                new(0.994, 33, 21, 50, 10.36),
                new(0.995, 31, 18, 48, 10.58),
                new(0.996, 28, 15, 44, 10.79),
                new(0.997, 24, 12, 40, 10.93),
                new(0.998, 17,  9, 35, 10.79),
            },
            ["xpsnr"] = new()
            {
                new(42, 45, 34, 59,  9.88),
                new(44, 40, 29, 56, 10.29),
                new(46, 34, 23, 51, 10.58),
                new(48, 29, 17, 46, 10.65),
                new(50, 23, 12, 41, 10.55),
                new(52, 16,  9, 35, 10.11),
                new(54, 11,  6, 30,  9.30),
            },
            ["ssimu2"] = new()
            {
                new(70, 33, 27, 41, 6.89),
                new(75, 29, 23, 37, 7.05),
                new(80, 23, 15, 31, 7.17),
                new(82, 19, 12, 27, 7.22),
                new(85, 13,  6, 22, 7.01),
                new(87,  9,  0, 16, 6.52),
                new(90,  2,  0,  8, 3.84),
            },
            ["butter3"] = new()
            {
                new(0.6,  0,  0,  7,  4.68),
                new(0.7,  1,  0, 11,  5.91),
                new(0.8,  7,  0, 16,  7.61),
                new(0.9, 14,  0, 21,  9.05),
                new(1.0, 20,  0, 26, 10.10),
                new(1.2, 28,  5, 31,  9.54),
                new(1.5, 34, 28, 37,  8.68),
            },
        };

        // ═══════════════════════════════════════
        // 公开 API
        // ═══════════════════════════════════════

        /// <summary>判断指定指标是否有先验表</summary>
        public static bool HasTable(string? metricMode)
        {
            return ResolveTable(metricMode) != null;
        }

        /// <summary>按指标和目标值（原生尺度）返回 (median, lo, hi)，lo/hi 为 80% 置信区间</summary>
        public static (int median, int lo, int hi) GetPrior(string metricMode, double target)
        {
            var table = ResolveTable(metricMode);
            if (table == null)
            {
                throw new ArgumentException($"指标 '{metricMode}' 没有先验表", nameof(metricMode));
            }

            return InterpolateMedianLoHi(table, target);
        }

        /// <summary>基于目标值最优 CRF 的标准差计算哨兵探测偏移量。delta = Clamp(round(σ/2), 2, 4)</summary>
        public static int GetSentinelDelta(string metricMode, double target)
        {
            var table = ResolveTable(metricMode);
            if (table == null)
            {
                return 3;   // 安全默认
            }

            double stdDev = InterpolateStdDev(table, target);
            int delta = (int)Math.Round(stdDev / 2.0);
            return Math.Clamp(delta, 2, 4);
        }

        // ═══════════════════════════════════════
        // 向后兼容 API（委托到 VMAF 表）
        // ═══════════════════════════════════════

        /// <summary>[向后兼容] 按 VMAF 目标值返回先验区间</summary>
        public static (int median, int lo, int hi) GetPriorFromVmaf(double targetVmaf)
        {
            return GetPrior("vmaf", targetVmaf);
        }

        /// <summary>[向后兼容] 按 VMAF 目标值返回哨兵偏移量</summary>
        public static int GetSentinelDelta(double targetVmaf)
        {
            return GetSentinelDelta("vmaf", targetVmaf);
        }

        // ═══════════════════════════════════════
        // 内部实现
        // ═══════════════════════════════════════

        private static List<Entry>? ResolveTable(string? metricMode)
        {
            if (string.IsNullOrEmpty(metricMode))
            {
                return null;
            }

            string key = metricMode.ToLowerInvariant();
            // 处理 xpsnr 的子模式（xpsnr-y / xpsnr-w 等）
            if (key.StartsWith("xpsnr"))
            {
                key = "xpsnr";
            }

            return Tables.TryGetValue(key, out var table) ? table : null;
        }

        private static (int median, int lo, int hi) InterpolateMedianLoHi(List<Entry> table, double target)
        {
            int idx = 0;
            while (idx < table.Count && table[idx].Target < target)
            {
                idx++;
            }

            double median, lo, hi;

            if (idx == 0)
            {
                median = ExtrapolateInt(table, target, 0, 1, e => e.Median);
                lo     = ExtrapolateInt(table, target, 0, 1, e => e.Lo);
                hi     = ExtrapolateInt(table, target, 0, 1, e => e.Hi);
            }
            else if (idx == table.Count)
            {
                int last = table.Count - 1;
                median = ExtrapolateInt(table, target, last - 1, last, e => e.Median);
                lo     = ExtrapolateInt(table, target, last - 1, last, e => e.Lo);
                hi     = ExtrapolateInt(table, target, last - 1, last, e => e.Hi);
            }
            else
            {
                var left  = table[idx - 1];
                var right = table[idx];
                double t = (target - left.Target) / (right.Target - left.Target);
                median = left.Median + t * (right.Median - left.Median);
                lo     = left.Lo     + t * (right.Lo     - left.Lo);
                hi     = left.Hi     + t * (right.Hi     - left.Hi);
            }

            int medianInt = EncodeHelpers.ClampCrf((int)Math.Round(median));
            int loInt     = EncodeHelpers.ClampCrf((int)Math.Round(lo));
            int hiInt     = EncodeHelpers.ClampCrf((int)Math.Round(hi));

            if (loInt > medianInt)
            {
                loInt = medianInt - 1;
            }
            if (hiInt < medianInt)
            {
                hiInt = medianInt + 1;
            }

            return (medianInt, loInt, hiInt);
        }

        private static double InterpolateStdDev(List<Entry> table, double target)
        {
            int idx = 0;
            while (idx < table.Count && table[idx].Target < target)
            {
                idx++;
            }

            if (idx == 0)
            {
                return ExtrapolateDouble(table, target, 0, 1, e => e.StdDev);
            }
            else if (idx == table.Count)
            {
                int last = table.Count - 1;
                return ExtrapolateDouble(table, target, last - 1, last, e => e.StdDev);
            }
            else
            {
                var left  = table[idx - 1];
                var right = table[idx];
                double t = (target - left.Target) / (right.Target - left.Target);
                return left.StdDev + t * (right.StdDev - left.StdDev);
            }
        }

        private static double ExtrapolateInt(
            List<Entry> table, double target, int leftIdx, int rightIdx,
            Func<Entry, int> selector)
        {
            var left  = table[leftIdx];
            var right = table[rightIdx];
            double slope = (selector(right) - selector(left)) / (right.Target - left.Target);
            return selector(left) + slope * (target - left.Target);
        }

        private static double ExtrapolateDouble(
            List<Entry> table, double target, int leftIdx, int rightIdx,
            Func<Entry, double> selector)
        {
            var left  = table[leftIdx];
            var right = table[rightIdx];
            double slope = (selector(right) - selector(left)) / (right.Target - left.Target);
            return selector(left) + slope * (target - left.Target);
        }
    }
}
