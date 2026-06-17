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
        private int _currentCount;  // ★ 手动追踪释放计数，避免 maxCount=int.MaxValue 使吞噬失效
        private readonly object _lock = new();

        public int CurrentMax { get { lock (_lock) return _currentMax; } }

        public DynamicConcurrencyLimiter(int initialMax)
        {
            _currentMax = initialMax;
            _currentCount = initialMax;
            _semaphore = new SemaphoreSlim(initialMax);
        }

        public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken token = default)
        {
            var result = await _semaphore.WaitAsync(timeout, token);
            if (result) { lock (_lock) { _currentCount--; } }
            return result;
        }

        public async Task WaitAsync(CancellationToken token = default)
        {
            await _semaphore.WaitAsync(token);
            lock (_lock) { _currentCount--; }
        }

        /// <summary>
        /// 释放槽位。如果当前并发数已超目标上限（缩容后），吞噬本次释放不增加可用计数，
        /// 等待运行中的任务逐步完成，并发数自然收敛到目标值。
        /// </summary>
        public void Release()
        {
            // ★ 手动追踪：缩容后如已超 _currentMax，吞噬本次释放不增加槽位
            lock (_lock)
            {
                if (_currentCount < _currentMax)
                {
                    _currentCount++;
                    _semaphore.Release();
                }
            }
        }

        /// <summary>动态设置为指定最大并发数。扩容立即生效；缩容通过 Release 吞噬逐步收敛。</summary>
        public int SetMax(int newMax)
        {
            lock (_lock)
            {
                newMax = Math.Max(1, newMax);
                int oldMax = _currentMax;
                _currentMax = newMax;
                int diff = newMax - oldMax;
                if (diff > 0)
                {
                    _semaphore.Release(diff);
                    _currentCount += diff;
                }
                else if (diff < 0)
                {
                    // 缩容：回收空闲槽位 + 重置计数上限
                    for (int i = 0; i < -diff; i++)
                    {
                        if (!_semaphore.Wait(0)) break;
                        _currentCount--;
                    }
                }
                return _currentMax;
            }
        }

        public void Dispose() => _semaphore?.Dispose();
    }
}
