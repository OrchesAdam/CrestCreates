namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptor
{
    /// <summary>
    /// Registry domain. Examples: "event", "capability", "workflow"
    /// </summary>
    string Namespace { get; }

    string Id { get; }
    string Name { get; }

    /// <summary>
    /// Global identity. Computed: {Namespace}.{Id}
    /// </summary>
    string FullId => $"{Namespace}.{Id}";

    // ── Legacy members (kept for backward compatibility) ──
    DescriptorKind Kind { get; }
    DescriptorState State { get; }
    string ContractHash { get; }
    string DefinitionHash { get; }
    string? SupersededById { get; }
}
