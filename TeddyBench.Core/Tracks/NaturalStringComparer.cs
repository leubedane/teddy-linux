using System;
using System.Collections.Generic;
using System.Globalization;

namespace TeddyBench.Core.Tracks
{
    /// <summary>
    /// Compares strings with digit runs read as numbers ("Folge 2" before "Folge 10"); text is compared
    /// case-insensitively. Strings that differ only in case or leading zeros still compare non-equal.
    /// </summary>
    public sealed class NaturalStringComparer : IComparer<string>
    {
        public static readonly NaturalStringComparer Instance = new NaturalStringComparer();

        private static readonly CompareInfo Text = CultureInfo.InvariantCulture.CompareInfo;

        public int Compare(string x, string y)
        {
            if (ReferenceEquals(x, y))
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

            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                bool xDigit = char.IsDigit(x[i]);
                bool yDigit = char.IsDigit(y[j]);
                int xEnd = Run(x, i, xDigit);
                int yEnd = Run(y, j, yDigit);

                int result = xDigit && yDigit
                    ? CompareNumbers(x, i, xEnd, y, j, yEnd)
                    : Text.Compare(x, i, xEnd - i, y, j, yEnd - j, CompareOptions.IgnoreCase);
                if (result != 0)
                {
                    return result;
                }
                i = xEnd;
                j = yEnd;
            }

            if (i < x.Length || j < y.Length)
            {
                return i < x.Length ? 1 : -1;
            }
            /* equal in natural order: fall back to a strict order so sorting is deterministic */
            return string.CompareOrdinal(x, y);
        }

        private static int Run(string s, int start, bool digits)
        {
            int end = start;
            while (end < s.Length && char.IsDigit(s[end]) == digits)
            {
                end++;
            }
            return end;
        }

        /// <summary>Compares two digit runs by value without parsing (no overflow).</summary>
        private static int CompareNumbers(string x, int xStart, int xEnd, string y, int yStart, int yEnd)
        {
            while (xStart < xEnd - 1 && x[xStart] == '0')
            {
                xStart++;
            }
            while (yStart < yEnd - 1 && y[yStart] == '0')
            {
                yStart++;
            }

            int lengths = (xEnd - xStart).CompareTo(yEnd - yStart);
            if (lengths != 0)
            {
                return lengths;
            }
            return Math.Sign(string.CompareOrdinal(x, xStart, y, yStart, xEnd - xStart));
        }
    }
}
