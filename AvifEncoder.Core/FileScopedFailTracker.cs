using System.Collections.Generic;

namespace AvifEncoder
{
    internal sealed class FileScopedFailTracker
    {
        public HashSet<int> KnownBadCrfs { get; } = [];
        public Dictionary<int, int> CrfFailCount { get; } = [];
        public const int HardFailThreshold = 2;
        public const int AvoidRadius = 2;

        public void Reset()
        {
            KnownBadCrfs.Clear();
            CrfFailCount.Clear();
        }

        public bool IsBlacklisted(int crf)
        {
            for (int offset = -AvoidRadius; offset <= AvoidRadius; offset++)
            {
                if (KnownBadCrfs.Contains(crf + offset))
                {
                    return true;
                }
            }
            return false;
        }

        public void RecordFailedAttempt(int crf)
        {
            CrfFailCount.TryGetValue(crf, out int count);
            count++;
            CrfFailCount[crf] = count;
            if (count >= HardFailThreshold)
            {
                KnownBadCrfs.Add(crf);
            }
        }

        public void ClearCrf(int crf) => CrfFailCount.Remove(crf);

        public int FindSafeCrfInInterval(int center, int xMin, int xMax)
        {
            for (int offset = 0; offset <= xMax - xMin; offset++)
            {
                int tryCrf = center + offset;
                if (tryCrf >= xMin && tryCrf <= xMax && !IsBlacklisted(tryCrf))
                {
                    return tryCrf;
                }
                tryCrf = center - offset;
                if (tryCrf >= xMin && tryCrf <= xMax && !IsBlacklisted(tryCrf))
                {
                    return tryCrf;
                }
            }
            return -1;
        }
    }
}
