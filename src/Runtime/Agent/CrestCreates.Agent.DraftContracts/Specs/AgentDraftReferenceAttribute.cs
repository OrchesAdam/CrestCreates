namespace CrestCreates.Agent.DraftContracts.Specs;

/// <summary>
/// Marks a property as an EditableReference — directly editable descriptor reference or typed reference.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class AgentDraftReferenceAttribute : Attribute;
