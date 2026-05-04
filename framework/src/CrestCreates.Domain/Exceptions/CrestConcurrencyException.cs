using CrestCreates.Domain.Shared.Exceptions;

namespace CrestCreates.Domain.Exceptions;

public class CrestConcurrencyException : CrestException
{
    public CrestConcurrencyException(string entityType, object? entityId)
        : base(
            "Crest.Concurrency.Conflict",
            409,
            "Concurrency conflict.",
            $"{entityType} (Id={entityId}) has been modified by another user.")
    {
        EntityType = entityType;
        EntityId = entityId;
    }

    public string EntityType { get; }

    public object? EntityId { get; }
}
