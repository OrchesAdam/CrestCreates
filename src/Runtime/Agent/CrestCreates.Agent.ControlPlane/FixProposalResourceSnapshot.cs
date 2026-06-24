using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.DescriptorDraft.Abstractions;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Agent.ControlPlane;

internal sealed record FixProposalResourceSnapshot(
    FixProposal Proposal,
    Draft Owner);
