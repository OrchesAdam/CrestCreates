using System;

namespace CrestCreates.Scheduling.Services;

public interface IBackgroundJobRetryPolicy
{
    bool ShouldRetry(int attemptCount, Exception exception);
    TimeSpan GetDelay(int attemptCount);
    int MaxRetries { get; }
}
