namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Cross-registry bootstrap validation.
/// Unlike IRegistryValidator{T} (single-registry internal validation),
/// this validates relationships across multiple registries.
/// Phase 6 Graph Engine will extend this interface.
/// </summary>
public interface IBootstrapValidator
{
    int Order { get; }
    ValidationReport Validate();
}
