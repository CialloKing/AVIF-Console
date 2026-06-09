namespace AvifEncoder
{
    /// <summary>
    /// 动态并发限制器：支持运行时增减并发数。
    /// 扩缩容通过调整 _currentMax + Release 中吞噬超额槽位实现逐步收敛。
    /// </summary>
    public class DynamicConcurrencyLimiter : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private int _currentMax;
        private readonly object _lock = new();

        public int CurrentMax { get { lock (_lock) return _currentMax; } }

        public DynamicConcurrencyLimiter(int initialMax)
        {
            _currentMax = initialMax;
            _semaphore = new SemaphoreSlim(initialMax, int.MaxValue);
        }

        public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken token = default)
            => await _semaphore.WaitAsync(timeout, token);

        public async Task WaitAsync(CancellationToken token = default)
            => await _semaphore.WaitAsync(token);

        /// <summary>
        /// 释放槽位。如果当前并发数已超目标上限（缩容后），吞噬本次释放不增加可用计数，
        /// 等待运行中的任务逐步完成，并发数自然收敛到目标值。
        /// </summary>
        public void Release()
        {
            // ★ 缩容后 Semaphore 内部计数可能超标，吞噬多余的 Release 直到降到 _currentMax 以下
            if (_semaphore.CurrentCount < _currentMax)
                _semaphore.Release();
        }

        /// <summary>动态设置为指定最大并发数。扩容立即生效；缩容通过 Release 吞噬逐步收敛。</summary>
        public int SetMax(int newMax)
        {
            lock (_lock)
            {
                newMax = Math.Max(1, newMax);
                int diff = newMax - _currentMax;
                if (diff > 0)
                {
                    _semaphore.Release(diff);
                }
                else if (diff < 0)
                {
                    // 缩容：先回收空闲槽位，剩余通过 Release 吞噬逐步收敛
                    for (int i = 0; i < -diff; i++)
                    {
                        if (!_semaphore.Wait(0)) break;
                    }
                }
                _currentMax = newMax;
                return _currentMax;
            }
        }

        public void Dispose() => _semaphore?.Dispose();
    }
}
