using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AvifEncoder
{
    /// <summary>
    /// Runtime-adjustable concurrency limiter. Expansion wakes waiters immediately;
    /// shrinkage lets already-running work drain before new waiters are granted slots.
    /// </summary>
    public class DynamicConcurrencyLimiter : IDisposable
    {
        private readonly object _lock = new();
        private readonly Queue<Waiter> _waiters = new();
        private int _currentMax;
        private int _inUse;
        private bool _disposed;

        public int CurrentMax { get { lock (_lock) return _currentMax; } }

        public DynamicConcurrencyLimiter(int initialMax)
        {
            _currentMax = Math.Max(1, initialMax);
        }

        public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken token = default)
        {
            if (timeout < Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(timeout));
            if (token.IsCancellationRequested)
                return Task.FromCanceled<bool>(token);

            Waiter? waiter = null;
            List<Waiter>? grants = null;
            lock (_lock)
            {
                ThrowIfDisposed();
                if (_waiters.Count == 0 && _inUse < _currentMax)
                {
                    _inUse++;
                    return Task.FromResult(true);
                }

                if (timeout == TimeSpan.Zero)
                    return Task.FromResult(false);

                waiter = new Waiter();
                _waiters.Enqueue(waiter);
                grants = GrantWaitersLocked();
            }

            CompleteGrantedWaiters(grants);
            if (!waiter.IsCompleted)
                waiter.AttachTimeoutAndCancellation(this, timeout, token);
            return waiter.Task;
        }

        public async Task WaitAsync(CancellationToken token = default)
        {
            await WaitAsync(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
        }

        public void Release()
        {
            List<Waiter>? grants = null;
            lock (_lock)
            {
                if (_disposed || _inUse <= 0)
                    return;

                _inUse--;
                grants = GrantWaitersLocked();
            }

            CompleteGrantedWaiters(grants);
        }

        public int SetMax(int newMax)
        {
            int result;
            List<Waiter>? grants = null;
            lock (_lock)
            {
                ThrowIfDisposed();
                _currentMax = Math.Max(1, newMax);
                grants = GrantWaitersLocked();
                result = _currentMax;
            }

            CompleteGrantedWaiters(grants);
            return result;
        }

        public void Dispose()
        {
            List<Waiter> pending = new();
            lock (_lock)
            {
                if (_disposed)
                    return;

                _disposed = true;
                while (_waiters.Count > 0)
                {
                    var waiter = _waiters.Dequeue();
                    if (!waiter.IsCompleted)
                    {
                        waiter.MarkCompleted();
                        pending.Add(waiter);
                    }
                }
            }

            foreach (var waiter in pending)
            {
                waiter.DisposeRegistrations();
                waiter.TrySetException(new ObjectDisposedException(nameof(DynamicConcurrencyLimiter)));
            }
        }

        private List<Waiter>? GrantWaitersLocked()
        {
            List<Waiter>? grants = null;
            while (_inUse < _currentMax && _waiters.Count > 0)
            {
                var waiter = _waiters.Dequeue();
                if (waiter.IsCompleted)
                    continue;

                waiter.MarkCompleted();
                _inUse++;
                (grants ??= new List<Waiter>()).Add(waiter);
            }
            return grants;
        }

        private static void CompleteGrantedWaiters(List<Waiter>? grants)
        {
            if (grants == null)
                return;

            foreach (var waiter in grants)
            {
                waiter.DisposeRegistrations();
                waiter.TrySetResult(true);
            }
        }

        private void CompleteTimedOut(Waiter waiter)
        {
            if (!TryCompletePending(waiter))
                return;

            waiter.DisposeRegistrations();
            waiter.TrySetResult(false);
        }

        private void CompleteCanceled(Waiter waiter, CancellationToken token)
        {
            if (!TryCompletePending(waiter))
                return;

            waiter.DisposeRegistrations();
            waiter.TrySetCanceled(token);
        }

        private bool TryCompletePending(Waiter waiter)
        {
            lock (_lock)
            {
                if (waiter.IsCompleted)
                    return false;

                waiter.MarkCompleted();
                return true;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DynamicConcurrencyLimiter));
        }

        private sealed class Waiter
        {
            private readonly TaskCompletionSource<bool> _tcs =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private CancellationTokenRegistration _cancellationRegistration;
            private Timer? _timeoutTimer;

            public bool IsCompleted { get; private set; }
            public Task<bool> Task => _tcs.Task;

            public void MarkCompleted() => IsCompleted = true;

            public void AttachTimeoutAndCancellation(
                DynamicConcurrencyLimiter owner,
                TimeSpan timeout,
                CancellationToken token)
            {
                if (token.CanBeCanceled)
                {
                    _cancellationRegistration = token.Register(
                        static state =>
                        {
                            var (limiter, waiter, cancellationToken) =
                                ((DynamicConcurrencyLimiter, Waiter, CancellationToken))state!;
                            limiter.CompleteCanceled(waiter, cancellationToken);
                        },
                        (owner, this, token));
                }

                if (timeout != Timeout.InfiniteTimeSpan)
                {
                    _timeoutTimer = new Timer(
                        static state =>
                        {
                            var (limiter, waiter) = ((DynamicConcurrencyLimiter, Waiter))state!;
                            limiter.CompleteTimedOut(waiter);
                        },
                        (owner, this),
                        timeout,
                        Timeout.InfiniteTimeSpan);
                }

                if (IsCompleted)
                    DisposeRegistrations();
            }

            public void DisposeRegistrations()
            {
                try { _timeoutTimer?.Dispose(); } catch { }
                _timeoutTimer = null;
                try { _cancellationRegistration.Dispose(); } catch { }
            }

            public void TrySetResult(bool value) => _tcs.TrySetResult(value);
            public void TrySetCanceled(CancellationToken token) => _tcs.TrySetCanceled(token);
            public void TrySetException(Exception exception) => _tcs.TrySetException(exception);
        }
    }
}
