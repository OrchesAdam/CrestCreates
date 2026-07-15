namespace CrestCreates.Capability.Abstractions;

public sealed record CapabilityExecutionIssue(
    string Code,
    string Message,
    string? FieldName = null);
