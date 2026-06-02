namespace AvifEncoder
{
    /// <summary>
    /// 动态并发限制器：支持运行时增减并发数。内部 SemaphoreSlim(initial, int.MaxValue) 无硬上限。
    /// </summary>
    public class DynamicConcurrencyLimiter : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private int _currentMax;
        private readonly object _lock = new();

        public int CurrentMax
        {
            get { lock (_lock) return _currentMax; }
        }

        public DynamicConcurrencyLimiter(int initialMax)
        {
            _currentMax = initialMax;
            _semaphore = new SemaphoreSlim(initialMax, int.MaxValue);
        }

        public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken token = default)
            => await _semaphore.WaitAsync(timeout, token);

        public async Task WaitAsync(CancellationToken token = default)
            => await _semaphore.WaitAsync(token);

        public void Release() => _semaphore.Release();

        /// <summary>动态设置为指定最大并发数。增减差值，仅回收空闲槽位。</summary>
        public int SetMax(int newMax)
        {
            lock (_lock)
            {
                newMax = Math.Max(1, newMax);
                int diff = newMax - _currentMax;
                if (diff > 0)
                    _semaphore.Release(diff);
                else if (diff < 0)
                {
                    // 回收空闲槽位 — 不强行中断正在执行的任务
                    for (int i = 0; i < -diff; i++)
                    {
                        if (!_semaphore.Wait(0))
                            break;
                    }
                }
                _currentMax = newMax;
                return _currentMax;
            }
        }

        public void Dispose() => _semaphore?.Dispose();
    }
}
