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
                    int xn = 0, yn = 0;
                    while (xi < x.Length && char.IsDigit(x[xi]))
                    {
                        xn = xn * 10 + (x[xi] - '0');
                        xi++;
                    }
                    while (yi < y.Length && char.IsDigit(y[yi]))
                    {
                        yn = yn * 10 + (y[yi] - '0');
                        yi++;
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
