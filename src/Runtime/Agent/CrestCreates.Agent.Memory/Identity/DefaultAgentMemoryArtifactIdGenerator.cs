using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Memory.Identity;

/// <summary>
/// First-party opaque artifact identity allocator. Identities intentionally carry
/// no tenant, source, provider, ordinal, or content material.
/// </summary>
public sealed class DefaultAgentMemoryArtifactIdGenerator : IAgentMemoryArtifactIdGenerator
{
    public string CreateContextId() => Create("ctx");
    public string CreateBlockId() => Create("block");
    public string CreateCandidateId() => Create("candidate");
    public string CreateMemoryId() => Create("memory");

    private static string Create(string kind) => $"{kind}_{Guid.NewGuid():N}";
}
