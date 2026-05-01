using System;

namespace CrestCreates.Domain.Exceptions;

public class CrestPreconditionRequiredException : Exception
{
    public string EntityType { get; }
    public object? EntityId { get; }

    public CrestPreconditionRequiredException(string entityType, object? entityId)
        : base($"Precondition required: DELETE on {entityType} (Id={entityId}) requires If-Match header with current ConcurrencyStamp.")
    {
        EntityType = entityType;
        EntityId = entityId;
    }
}
