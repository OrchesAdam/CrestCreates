namespace CrestCreates.EventBus.Abstractions;

public sealed record DeadLetterRetryResult(
    string MessageId,
    bool Success,
    string? ErrorMessage);
