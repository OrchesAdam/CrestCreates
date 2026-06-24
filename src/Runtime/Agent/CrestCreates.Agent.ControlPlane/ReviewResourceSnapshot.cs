using CrestCreates.DescriptorDraft.Abstractions;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftReviewResult = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraftReviewResult;

namespace CrestCreates.Agent.ControlPlane;

internal sealed record ReviewResourceSnapshot(
    DraftReviewResult Review,
    Draft Owner,
    DateTimeOffset CreatedAt);
