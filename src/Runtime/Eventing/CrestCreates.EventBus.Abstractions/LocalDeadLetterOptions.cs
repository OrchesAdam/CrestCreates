namespace CrestCreates.EventBus.Abstractions;

public sealed class LocalDeadLetterOptions
{
    public int MaxRetries { get; set; } = 3;
    public int RetryIntervalSeconds { get; set; } = 30;
    public int MaxQueueSize { get; set; } = 10000;
    public int AutoCleanArchivedDays { get; set; } = 7;
}
