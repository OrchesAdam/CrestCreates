using System;
using CrestCreates.Event.Abstractions;

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
    string EventName,              // "capability.succeeded" — registry-defined name
    int EventVersion,              // event version number from registry
    string? EventDescriptorId,     // "evt_A3F8C2D1..." — stable descriptor id
    string? CorrelationId,         // distributed tracing correlation id
    string? TenantId,              // multi-tenant DLQ dashboard
    EventScope Scope,
    string PayloadTypeFullName,    // survives assembly version changes
    byte[] Payload,
    string ErrorMessage,
    string? ExceptionType,         // typeof(TimeoutException).FullName
    DateTime OccurredAt,           // when the original event was created
    DateTime FailedAt,             // when the handler failed
    int RetryCount,
    int MaxRetries,
    DeadLetterStatus Status
)
{
    /// <summary>
    /// Computed aggregation key for monitoring systems (Grafana, Prometheus, Elastic).
    /// Example: "capability.succeeded:v2".
    /// Not stored — derived from EventName + EventVersion.
    /// Database indexing uses (EventName, EventVersion) columns, not this property.
    /// </summary>
    public string VersionKey => $"{EventName}:v{EventVersion}";
}
