using System;
using System.Collections.Generic;

namespace AvifEncoder
{
    /// <summary>
    /// VMAF 先验分布表：根据目标 VMAF 返回建议的 CRF 中位数及搜索范围。
    /// 数据基于真实图片统计，使用分段线性插值，外推采用局部斜率。
    /// </summary>
    public static class VmafPriorHelper
    {
        private static readonly List<(double TargetVmaf, int Median, int Lo, int Hi)> Table = new()
        {
            (90, 38, 26, 58),
            (91, 36, 24, 57),
            (92, 34, 20, 56),
            (93, 32, 16, 54),
            (94, 29, 13, 52),
            (95, 25,  9, 49),
            (96, 19,  5, 43),
        };

        public static (int median, int lo, int hi) GetPriorFromVmaf(double targetVmaf)
        {
            int idx = 0;
            while (idx < Table.Count && Table[idx].TargetVmaf < targetVmaf)
            {
                idx++;
            }

            double median, lo, hi;

            if (idx == 0)
            {
                median = Extrapolate(targetVmaf, Table[0], Table[1], e => e.Median);
                lo = Extrapolate(targetVmaf, Table[0], Table[1], e => e.Lo);
                hi = Extrapolate(targetVmaf, Table[0], Table[1], e => e.Hi);
            }
            else if (idx == Table.Count)
            {
                var left = Table[^2];
                var right = Table[^1];
                median = Extrapolate(targetVmaf, left, right, e => e.Median);
                lo = Extrapolate(targetVmaf, left, right, e => e.Lo);
                hi = Extrapolate(targetVmaf, left, right, e => e.Hi);
            }
            else
            {
                var left = Table[idx - 1];
                var right = Table[idx];
                double t = (targetVmaf - left.TargetVmaf) / (right.TargetVmaf - left.TargetVmaf);
                median = left.Median + t * (right.Median - left.Median);
                lo = left.Lo + t * (right.Lo - left.Lo);
                hi = left.Hi + t * (right.Hi - left.Hi);
            }

            int medianInt = EncodeHelpers.ClampCrf((int)Math.Round(median));
            int loInt = EncodeHelpers.ClampCrf((int)Math.Round(lo));
            int hiInt = EncodeHelpers.ClampCrf((int)Math.Round(hi));

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

        private static double Extrapolate(double targetVmaf,
            (double TargetVmaf, int Median, int Lo, int Hi) left,
            (double TargetVmaf, int Median, int Lo, int Hi) right,
            Func<(double TargetVmaf, int Median, int Lo, int Hi), int> selector)
        {
            double slope = (selector(right) - selector(left)) / (right.TargetVmaf - left.TargetVmaf);
            double delta = targetVmaf - left.TargetVmaf;
            return selector(left) + slope * delta;
        }
    }
}
