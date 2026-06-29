using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

public sealed record DescriptorLifecycleFinding
{
    public required SeverityLevel Severity { get; init; }
    public required DiagnosticCode Code { get; init; }
    public required string Message { get; init; }
    public DescriptorRef? Subject { get; init; }
    public string? Source { get; init; }
    public IReadOnlyList<DescriptorRef> RelatedRefs { get; init; }
        = Array.Empty<DescriptorRef>();
    public string? SuggestedAction { get; init; }
}
