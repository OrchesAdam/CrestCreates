namespace CrestCreates.Agent.ControlPlane.Abstractions;

public enum DescriptorReviewRecommendationKind
{
    RequestActivationHandoff = 1,
    RequestHumanReview = 2,
    ApplyFixProposal = 3,
    ReviseDraft = 4,
    CancelDraft = 5,
    NoAction = 6
}
