using System.Collections.Concurrent;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public sealed class InMemoryRateLimitStore : IRateLimitStore
{
    private readonly ConcurrentDictionary<string, SlidingWindow> _windows = new();

    public Task<bool> CheckRateLimitAsync(string key, int maxRequests, TimeSpan window, CancellationToken ct = default)
    {
        var w = _windows.GetOrAdd(key, _ => new SlidingWindow(window));
        return Task.FromResult(w.TryIncrement(maxRequests));
    }

    private sealed class SlidingWindow
    {
        private readonly TimeSpan _window;
        private readonly ConcurrentQueue<DateTimeOffset> _timestamps = new();
        private readonly object _lock = new();

        public SlidingWindow(TimeSpan window) { _window = window; }

        public bool TryIncrement(int maxRequests)
        {
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;
                var cutoff = now - _window;

                while (_timestamps.TryPeek(out var oldest) && oldest < cutoff)
                    _timestamps.TryDequeue(out _);

                if (_timestamps.Count >= maxRequests) return false;

                _timestamps.Enqueue(now);
                return true;
            }
        }
    }
}