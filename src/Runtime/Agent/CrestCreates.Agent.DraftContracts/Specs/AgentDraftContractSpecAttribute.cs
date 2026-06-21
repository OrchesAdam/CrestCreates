namespace CrestCreates.Agent.DraftContracts.Specs;

/// <summary>
/// Marks a class as an Agent Draft Contract Spec source.
/// The generator reads these classes to discover field classifications.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
internal sealed class AgentDraftContractSpecAttribute : Attribute
{
    public required DescriptorKind Kind { get; init; }
}
