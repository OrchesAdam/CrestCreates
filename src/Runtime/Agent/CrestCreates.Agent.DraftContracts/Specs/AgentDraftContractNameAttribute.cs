namespace CrestCreates.Agent.DraftContracts.Specs;

/// <summary>
/// Modifier: changes the generated contract-facing name for a property.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class AgentDraftContractNameAttribute : Attribute
{
    public required string Name { get; init; }
}
