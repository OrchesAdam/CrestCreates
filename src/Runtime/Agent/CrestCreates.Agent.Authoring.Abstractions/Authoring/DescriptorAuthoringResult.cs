using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Authoring;

public sealed record DescriptorAuthoringResult : ISnapshotable<DescriptorAuthoringResult>
{
    public required DescriptorAuthoringStatus Status { get; init; }
    public required DescriptorAuthoringPlan Plan { get; init; }
    public required DescriptorDraftSet DraftSet { get; init; }
    public AgentPromptInputEvidenceSummary? PromptInputEvidence { get; init; }
    public AgentPromptOutputEvidenceSummary? PromptOutputEvidence { get; init; }
    public IReadOnlyList<DescriptorAuthoringDiagnostic> Diagnostics { get; init; } = Array.Empty<DescriptorAuthoringDiagnostic>();

    public DescriptorAuthoringResult Snapshot() => this with
    {
        Plan = Plan.Snapshot(),
        DraftSet = DraftSet.Snapshot(),
        PromptInputEvidence = PromptInputEvidence is null ? null : PromptInputEvidence with
        {
            Diagnostics = PromptInputEvidence.Diagnostics.ToArray()
        },
        PromptOutputEvidence = PromptOutputEvidence is null ? null : PromptOutputEvidence with
        {
            Diagnostics = PromptOutputEvidence.Diagnostics.ToArray()
        },
        Diagnostics = Diagnostics.Select(d => d.Snapshot()).ToArray()
    };
}
