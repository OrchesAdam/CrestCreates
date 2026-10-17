using System;

namespace CrestCreates.Domain.Shared.Attributes;

/// <summary>
/// Declares the formal CRUD surface of one entity root: the annotated class is the (partial)
/// application service; the entity type is supplied explicitly. The assembly that declares this
/// service owns the generated DTO/service/mapping/projection artifacts.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class GenerateCrudServiceAttribute : Attribute
{
    public GenerateCrudServiceAttribute(Type entityType)
    {
        EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
    }

    /// <summary>The entity root the CRUD surface operates on; must be accessible and non-open-generic.</summary>
    public Type EntityType { get; }

    /// <summary>HTTP route prefix; defaults follow the existing convention when unset.</summary>
    public string? RoutePrefix { get; set; }

    /// <summary>Namespace of the generated contracts; defaults to the service namespace plus ".Contracts".</summary>
    public string? DtoNamespace { get; set; }

    /// <summary>Explicit capability identifier prefix; unset keeps the compatibility projection naming rule.</summary>
    public string? CapabilityIdPrefix { get; set; }

    /// <summary>Entity properties excluded from the default wire DTOs.</summary>
    public string[] ExcludeProperties { get; set; } = Array.Empty<string>();

    /// <summary>Entity properties allowed in JSON filters; unset means the default rule.</summary>
    public string[] FilterableProperties { get; set; } = Array.Empty<string>();

    /// <summary>Entity properties allowed in sorts; unset means the default rule.</summary>
    public string[] SortableProperties { get; set; } = Array.Empty<string>();
}
