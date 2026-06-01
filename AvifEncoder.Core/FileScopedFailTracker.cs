using System.Collections.Generic;

namespace AvifEncoder
{
    internal sealed class FileScopedFailTracker
    {
        private readonly HashSet<int> _knownBadCrfs = [];
        private readonly Dictionary<int, int> _crfFailCount = [];
        private readonly object _lock = new();
        public const int HardFailThreshold = 2;
        public const int AvoidRadius = 2;

        public void Reset()
        {
            lock (_lock)
            {
                _knownBadCrfs.Clear();
                _crfFailCount.Clear();
            }
        }

        private bool IsBlacklistedLocked(int crf)
        {
            for (int offset = -AvoidRadius; offset <= AvoidRadius; offset++)
            {
                if (_knownBadCrfs.Contains(crf + offset))
                    return true;
            }
            return false;
        }

        public bool IsBlacklisted(int crf)
        {
            lock (_lock)
            {
                return IsBlacklistedLocked(crf);
            }
        }

        public void RecordFailedAttempt(int crf)
        {
            lock (_lock)
            {
                _crfFailCount.TryGetValue(crf, out int count);
                count++;
                _crfFailCount[crf] = count;
                if (count >= HardFailThreshold)
                    _knownBadCrfs.Add(crf);
            }
        }

        public void ClearCrf(int crf)
        {
            lock (_lock)
            {
                _crfFailCount.Remove(crf);
            }
        }

        public int FindSafeCrfInInterval(int center, int xMin, int xMax)
        {
            lock (_lock)
            {
                for (int offset = 0; offset <= xMax - xMin; offset++)
                {
                    int tryCrf = center + offset;
                    if (tryCrf >= xMin && tryCrf <= xMax && !IsBlacklistedLocked(tryCrf))
                        return tryCrf;

                    tryCrf = center - offset;
                    if (tryCrf >= xMin && tryCrf <= xMax && !IsBlacklistedLocked(tryCrf))
                        return tryCrf;
                }
            }
            return -1;
        }
    }
}
