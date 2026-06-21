namespace CrestCreates.Agent.DraftContracts.Specs;

/// <summary>
/// Marks a property as Preserve — not emitted into DTOs, not adapter-editable.
/// Preserved fields are tracked only in the generator spec and manifest.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class AgentDraftPreserveAttribute : Attribute
{
    public required string Reason { get; init; }
    public required PreserveCreateStrategy CreateStrategy { get; init; }
}

public enum PreserveCreateStrategy
{
    CreateDefault,
    KnownDomainDefault,
    CreateUnsupported
}
