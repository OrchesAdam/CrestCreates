using System;

namespace CrestCreates.EventBus.Abstractions;

public enum DeadLetterStatus
{
    Pending,
    Retrying,
    Retried,
    Archived
}

public sealed record DeadLetterMessage(
    string MessageId,
    string EventType,
    byte[] Payload,
    string ErrorMessage,
    DateTime FailedAt,
    int RetryCount,
    int MaxRetries,
    DeadLetterStatus Status
);
