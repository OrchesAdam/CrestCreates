using System;

namespace CrestCreates.Scheduling.Services;

public class FixedDelayRetryPolicy : IBackgroundJobRetryPolicy
{
    private readonly int _maxRetries;
    private readonly TimeSpan _delay;

    public FixedDelayRetryPolicy(int maxRetries = 3, TimeSpan? delay = null)
    {
        _maxRetries = maxRetries;
        _delay = delay ?? TimeSpan.FromSeconds(30);
    }

    public int MaxRetries => _maxRetries;

    public bool ShouldRetry(int attemptCount, Exception exception)
    {
        return attemptCount < _maxRetries;
    }

    public TimeSpan GetDelay(int attemptCount)
    {
        return _delay;
    }
}
