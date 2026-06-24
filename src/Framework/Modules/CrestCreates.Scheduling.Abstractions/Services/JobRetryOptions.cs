using System;

namespace CrestCreates.Scheduling.Services;

public class JobRetryOptions
{
    public int MaxRetries { get; init; } = 0;
    public TimeSpan? InitialDelay { get; init; }
    public TimeSpan? MaxDelay { get; init; }
    public double BackoffMultiplier { get; init; } = 2.0;
}
