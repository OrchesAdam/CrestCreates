using CrestCreates.Domain.Shared.Exceptions;

namespace CrestCreates.Domain.Exceptions;

public class CrestPreconditionRequiredException : CrestException
{
    public CrestPreconditionRequiredException(string entityType, object? entityId)
        : base(
            CrestErrorCodes.ConcurrencyPreconditionRequired,
            428,
            "Precondition required.",
            $"DELETE on {entityType} (Id={entityId}) requires If-Match header with current ConcurrencyStamp.")
    {
        EntityType = entityType;
        EntityId = entityId;
    }

    public string EntityType { get; }

    public object? EntityId { get; }
}
