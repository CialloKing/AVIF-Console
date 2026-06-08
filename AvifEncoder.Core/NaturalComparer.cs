using System.Collections.Generic;

namespace AvifEncoder
{
    internal sealed class NaturalComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == y)
            {
                return 0;
            }
            if (x == null)
            {
                return -1;
            }
            if (y == null)
            {
                return 1;
            }
            int xi = 0, yi = 0;
            while (xi < x.Length && yi < y.Length)
            {
                if (char.IsDigit(x[xi]) && char.IsDigit(y[yi]))
                {
                    // ★ 使用 long 避免溢出（int 最多 10 位，long 支持 18 位）
                    long xn = 0, yn = 0;
                    int nx = 0, ny = 0;
                    bool xOverflow = false, yOverflow = false;
                    while (xi < x.Length && char.IsDigit(x[xi]))
                    {
                        if (!xOverflow)
                        {
                            if (xn > long.MaxValue / 10)
                                xOverflow = true;
                            else
                                xn = xn * 10 + (x[xi] - '0');
                        }
                        nx++;
                        xi++;
                    }
                    while (yi < y.Length && char.IsDigit(y[yi]))
                    {
                        if (!yOverflow)
                        {
                            if (yn > long.MaxValue / 10)
                                yOverflow = true;
                            else
                                yn = yn * 10 + (y[yi] - '0');
                        }
                        ny++;
                        yi++;
                    }
                    // ★ long 最多存 18 位数字（9,223,372,036,854,775,807），超出后 xn/yn 均为截断值。
                    //    位数不同 → 长数字 > 短数字（如 20 位 > 19 位）。
                    //    位数相同且双方溢出 → 必须逐字符比较，不能用截断的 long 值。
                    if (xOverflow || yOverflow)
                    {
                        if (nx != ny) return nx.CompareTo(ny);
                        if (xOverflow && yOverflow)
                        {
                            int xStart = xi - nx, yStart = yi - ny;
                            for (int d = 0; d < nx; d++)
                            {
                                int cmp = x[xStart + d].CompareTo(y[yStart + d]);
                                if (cmp != 0) return cmp;
                            }
                            return 0;
                        }
                    }
                    if (xn != yn)
                    {
                        return xn.CompareTo(yn);
                    }
                }
                else
                {
                    if (char.ToLowerInvariant(x[xi]) != char.ToLowerInvariant(y[yi]))
                    {
                        return char.ToLowerInvariant(x[xi]).CompareTo(char.ToLowerInvariant(y[yi]));
                    }
                    xi++;
                    yi++;
                }
            }
            return (x.Length - xi).CompareTo(y.Length - yi);
        }
    }
}
