namespace CrestCreates.Agent.DraftContracts.Specs;

/// <summary>
/// Marks a property as an EditableScalar — directly editable primitive, enum, string, or value-type metadata.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class AgentDraftFieldAttribute : Attribute;
