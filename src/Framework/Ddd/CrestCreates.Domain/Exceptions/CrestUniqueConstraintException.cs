using System;

namespace CrestCreates.Domain.Exceptions;

/// <summary>
/// Signals that a write lost a race to a unique constraint. Callers must re-read the persisted
/// row and converge; this exception never represents a successful write by itself.
/// </summary>
public sealed class CrestUniqueConstraintException : Exception
{
    public CrestUniqueConstraintException(string entityType, Exception innerException)
        : base($"Unique constraint violated for {entityType}.", innerException)
    {
        EntityType = entityType;
    }

    public string EntityType { get; }
}
