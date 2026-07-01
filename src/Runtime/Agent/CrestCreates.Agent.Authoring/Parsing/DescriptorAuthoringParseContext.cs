using CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.Authoring.Parsing;

public sealed record DescriptorAuthoringParseContext
{
    public required string TenantId { get; init; }
    public required string AuthorId { get; init; }
    public required DescriptorDraftAuthorKind AuthorKind { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string IntentText { get; init; }
    public required string ExpectedPromptInputHash { get; init; }
}
