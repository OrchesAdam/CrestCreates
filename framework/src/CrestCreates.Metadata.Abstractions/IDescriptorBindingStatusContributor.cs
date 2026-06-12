namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Per-module evaluator + descriptor enumerator. Each module (Capability, Form, HumanTask,
/// Workflow, Event) implements one to enumerate and evaluate descriptors of its SupportedKind.
/// Singleton, stateless, receives typed registries via constructor DI.
/// </summary>
public interface IDescriptorBindingStatusContributor
{
    /// <summary>Which DescriptorKind this contributor handles.</summary>
    DescriptorKind SupportedKind { get; }

    /// <summary>Execution order (lower = earlier). Contributors are sorted before evaluation.</summary>
    int Order { get; }

    /// <summary>
    /// Enumerate all descriptors of this kind from the contributor's injected registry.
    /// Returns empty list if the registry has not been built (RegistryState != Built).
    /// Must not trigger registry.Build().
    /// </summary>
    IReadOnlyList<IDescriptor> GetDescriptors();

    /// <summary>Evaluate a single descriptor. Must not mutate state.</summary>
    DescriptorBindingReport Evaluate(IDescriptor descriptor);
}
