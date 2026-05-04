namespace CrestCreates.Domain.Shared.Exceptions;

public class CrestEntityNotFoundException : CrestException
{
    public CrestEntityNotFoundException(string entityType, object? entityId = null)
        : base(
            "Crest.Entity.NotFound",
            404,
            "Entity not found.",
            entityId is null ? entityType : $"{entityType} (Id={entityId})")
    {
        EntityType = entityType;
        EntityId = entityId;
    }

    public string EntityType { get; }

    public object? EntityId { get; }
}
