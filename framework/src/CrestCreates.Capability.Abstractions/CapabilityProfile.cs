using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Capability.Abstractions;

public sealed class CapabilityProfile
{
    public VersionedDescriptorRef<CapabilityDescriptor> Capability { get; init; }
    public string Scope { get; init; } = string.Empty;
    public TimeSpan? Timeout { get; init; }
    public string? RetryPolicy { get; init; }
    public bool? RequireApproval { get; init; }
    public int? RateLimit { get; init; }
}