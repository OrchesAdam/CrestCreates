using System;

namespace CrestCreates.Scheduling.Services;

public class JobRetryOptions
{
    /// <summary>Retries after the initial attempt (attempt 1). Zero disables retry.</summary>
    public int MaxRetries { get; init; } = 0;
    public TimeSpan? InitialDelay { get; init; }
    public TimeSpan? MaxDelay { get; init; }
    public double BackoffMultiplier { get; init; } = 2.0;

    public void Validate()
    {
        if (MaxRetries < 0 || MaxRetries == int.MaxValue || !double.IsFinite(BackoffMultiplier) || BackoffMultiplier < 1
            || InitialDelay is { } initial && initial <= TimeSpan.Zero
            || MaxDelay is { } max && max <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(MaxRetries), "Retry count, delays and multiplier must be finite and positive (count may be zero).");
    }

    public TimeSpan GetDelay(int attemptNumber)
    {
        Validate();
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptNumber, 1);
        var initial = InitialDelay ?? TimeSpan.FromSeconds(1);
        var cap = Math.Min((MaxDelay ?? TimeSpan.FromMinutes(1)).TotalMilliseconds, TimeSpan.FromDays(1).TotalMilliseconds);
        return TimeSpan.FromMilliseconds(Math.Min(cap, initial.TotalMilliseconds * Math.Pow(BackoffMultiplier, attemptNumber - 1)));
    }
}
