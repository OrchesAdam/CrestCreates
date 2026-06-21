namespace CrestCreates.Agent.DraftContracts.Specs;

/// <summary>
/// Marks a property as Unsupported — not part of the editable contract.
/// The generator fails closed and blocks accidental exposure.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class AgentDraftUnsupportedAttribute : Attribute
{
    public required string Reason { get; init; }
}
