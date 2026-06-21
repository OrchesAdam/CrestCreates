namespace CrestCreates.Agent.DraftContracts.Specs;

/// <summary>
/// Modifier: marks a field as required when creating a new draft payload.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class AgentDraftRequiredOnCreateAttribute : Attribute;
