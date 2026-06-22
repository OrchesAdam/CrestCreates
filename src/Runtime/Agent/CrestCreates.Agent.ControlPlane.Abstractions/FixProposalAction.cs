using System.Text.Json;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record FixProposalAction
{
    public required FixProposalActionKind Kind { get; init; }
    public required string TargetPath { get; init; }
    public string? TargetDescriptorId { get; init; }
    public JsonElement? CurrentValue { get; init; }
    public JsonElement? ProposedValue { get; init; }
    public required bool IsExecutable { get; init; }
    public required FixProposalActionSafetyLevel SafetyLevel { get; init; }
    public string? Description { get; init; }
}
