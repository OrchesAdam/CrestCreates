using System;

namespace CrestCreates.Scheduling.Services;

public class NoRetryPolicy : IBackgroundJobRetryPolicy
{
    public int MaxRetries => 0;

    public bool ShouldRetry(int attemptCount, Exception exception) => false;

    public TimeSpan GetDelay(int attemptCount) => TimeSpan.Zero;
}
