using System.Collections.Immutable;

namespace CrestCreates.CodeGenerator.CapabilityEndpointGenerator;

/// <summary>
/// Normalized representation of a [CapabilityEndpointSpec] attribute on a class,
/// extracted during source generation.
/// </summary>
internal sealed class CapabilityEndpointSpecRecord
{
    // --- Constructor arguments ---
    public string CapabilityId { get; init; } = string.Empty;
    public int HttpMethodValue { get; init; }
    public string RoutePattern { get; init; } = string.Empty;

    // --- Named arguments ---
    public int CapabilityVersion { get; init; }
    public string? EndpointId { get; init; }
    public int EndpointVersion { get; init; }
    public int AuthorizationModeValue { get; init; }
    public int SuccessStatusCode { get; init; }
    public string? OperationId { get; init; }
    public string? GroupName { get; init; }
    public ImmutableArray<string> Tags { get; init; } = ImmutableArray<string>.Empty;
    public string? Summary { get; init; }
    public string? Description { get; init; }
    public bool Deprecated { get; init; }

    // --- Container class info ---
    public string SpecClassName { get; init; } = string.Empty;
    public string ContainerClassName { get; init; } = string.Empty;
    public string ContainerNamespace { get; init; } = string.Empty;
    public bool IsNested { get; init; }

    // --- Input bindings on the same class ---
    public ImmutableArray<CapabilityEndpointInputRecord> Inputs { get; init; }
        = ImmutableArray<CapabilityEndpointInputRecord>.Empty;
}

/// <summary>
/// Normalized representation of a [CapabilityEndpointInput] attribute.
/// </summary>
internal sealed class CapabilityEndpointInputRecord
{
    /// <summary>
    /// Fully qualified type name from the INamedTypeSymbol.
    /// </summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary>
    /// Type expression for typeof() and generic type arguments.
    /// - Nullable value types (int?): uses Nullable&lt;int&gt; form
    /// - Nullable reference types (BookDto?): strips ? suffix
    /// - Non-nullable types: same as TypeName
    /// </summary>
    public string TypeOfExpression { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
    public int SourceValue { get; init; } = 3; // Body = 3
    public bool Required { get; init; } = true;
    public string? CapabilityInputPath { get; init; }

    public string? TargetProperty { get; init; }

    /// <summary>
    /// True when the type is a C# enum. Used by the binding emitter to decide
    /// whether to generate Enum.Parse or return null for unsupported types.
    /// </summary>
    public bool IsEnum { get; init; } = false;
}

/// <summary>
/// Group of endpoint specs belonging to the same container class.
/// </summary>
internal sealed class ContainerEndpointGroup
{
    public string ContainerClassName { get; init; } = string.Empty;
    public string ContainerNamespace { get; init; } = string.Empty;
    public bool IsNested { get; init; }

    /// <summary>
    /// De-duplicated specs for this container, keyed by (CapabilityId, Version).
    /// </summary>
    public ImmutableArray<CapabilityEndpointSpecRecord> Specs { get; init; }
        = ImmutableArray<CapabilityEndpointSpecRecord>.Empty;
}
