using System;

namespace CrestCreates.Scheduling.Services;

public class ExponentialBackoffRetryPolicy : IBackgroundJobRetryPolicy
{
    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;

    public ExponentialBackoffRetryPolicy(int maxRetries = 3, TimeSpan? baseDelay = null, TimeSpan? maxDelay = null)
    {
        _maxRetries = maxRetries;
        _baseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
        _maxDelay = maxDelay ?? TimeSpan.FromMinutes(5);
    }

    public int MaxRetries => _maxRetries;

    public bool ShouldRetry(int attemptCount, Exception exception)
    {
        return attemptCount < _maxRetries;
    }

    public TimeSpan GetDelay(int attemptCount)
    {
        var delay = TimeSpan.FromTicks(_baseDelay.Ticks * (long)Math.Pow(2, attemptCount));
        return delay > _maxDelay ? _maxDelay : delay;
    }
}
