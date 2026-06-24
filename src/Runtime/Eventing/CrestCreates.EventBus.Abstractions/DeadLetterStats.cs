namespace CrestCreates.EventBus.Abstractions;

public sealed record DeadLetterStats(
    int TotalCount,
    int PendingCount,
    int RetryingCount,
    int RetriedCount,
    int ArchivedCount);
