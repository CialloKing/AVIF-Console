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
                    // 溢出时退化为按位数比较（长数字 > 短数字）
                    if (xOverflow || yOverflow)
                    {
                        if (nx != ny) return nx.CompareTo(ny);
                        // 位数相同：溢出前已积累的值作为近似比较
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
