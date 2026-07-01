using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Model;

public sealed record DescriptorAuthoringProviderProfile : ISnapshotable<DescriptorAuthoringProviderProfile>
{
    public required string ProviderName { get; init; }
    public Uri? Endpoint { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
    public string? CredentialReference { get; init; }

    public DescriptorAuthoringProviderProfile Snapshot() => this;
}
