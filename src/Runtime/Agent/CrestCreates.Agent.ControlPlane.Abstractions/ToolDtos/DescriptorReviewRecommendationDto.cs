namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record DescriptorReviewRecommendationDto
{
    public required string RecommendationId { get; init; }
    public required string ReasonCode { get; init; }
    public required string Message { get; init; }
    public required DescriptorReviewRecommendationKind Kind { get; init; }
    public required bool IsActionable { get; init; }
    public IReadOnlyList<string> RelatedItemIds { get; init; } = [];
}
