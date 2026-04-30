using System;

namespace CrestCreates.Domain.Exceptions;

public class CrestConcurrencyException : Exception
{
    public string EntityType { get; }
    public object? EntityId { get; }

    public CrestConcurrencyException(string entityType, object? entityId)
        : base($"Concurrency conflict: {entityType} (Id={entityId}) has been modified by another user.")
    {
        EntityType = entityType;
        EntityId = entityId;
    }
}
