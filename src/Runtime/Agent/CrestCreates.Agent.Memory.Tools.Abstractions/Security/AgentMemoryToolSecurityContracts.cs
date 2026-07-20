using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Tools;

public sealed record AgentMemoryToolPrincipal
{
    public required string TenantId { get; init; }
    public required string UserId { get; init; }
    public required string AgentId { get; init; }
    public required string ExecutionId { get; init; }
}

public enum AgentMemoryResourceKind
{
    Unknown = 0,
    Context = 1,
    Candidate = 2,
    Memory = 3,
    ConversationHistory = 4,
    TaskHistory = 5
}

public enum AgentMemoryHistorySourceKind
{
    Unknown = 0,
    Conversation = 1,
    Task = 2
}

public enum AgentMemorySecurityArtifactState
{
    Unknown = 0,
    Active = 1,
    Revoked = 2,
    Expired = 3
}

public sealed record AgentMemoryResourceHandle
{
    public required string HandleId { get; init; }
    public required AgentMemoryResourceKind ResourceKind { get; init; }
    public required string ResourceId { get; init; }
    public required AgentMemoryToolPrincipal Principal { get; init; }
    public required string ScopeFingerprint { get; init; }
    public IReadOnlyList<DescriptorRef> RequiredDescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
    public bool IsUnscoped { get; init; }
    public required string IssuingInvocationId { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public AgentMemorySecurityArtifactState State { get; init; } = AgentMemorySecurityArtifactState.Active;
}

public sealed record AgentMemoryResourceHandleIssueResult
{
    public required IReadOnlyList<AgentMemoryResourceHandle> Handles { get; init; }
    public bool ReusedExisting { get; init; }
}

public sealed record AgentMemorySourceGrant
{
    public required string GrantId { get; init; }
    public required AgentContextSourceRef SourceRef { get; init; }
    public required AgentMemoryToolPrincipal Principal { get; init; }
    public required string ScopeFingerprint { get; init; }
    public IReadOnlyList<DescriptorRef> RequiredDescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
    public bool IsUnscoped { get; init; }
    public required string IssuingInvocationId { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public AgentMemorySecurityArtifactState State { get; init; } = AgentMemorySecurityArtifactState.Active;
}

public sealed record AgentMemoryGrantIssueResult
{
    public required IReadOnlyList<AgentMemorySourceGrant> Grants { get; init; }
    public bool ReusedExisting { get; init; }
}

public sealed record AgentMemoryToolAccessScope
{
    public IReadOnlyList<DescriptorRef> VisibleDescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
    public bool AllowUnscopedMemory { get; init; }
    public int MaxVisibleDescriptorRefs { get; init; } = 64;
    public int MaxRecallCount { get; init; } = 32;
    public int MaxRecallCharacters { get; init; } = 32_000;
    public int MaxExpansionCharacters { get; init; } = 16_000;
    public int MaxCompressedBlockCount { get; init; } = 64;
    public int MaxCompressedBlockCharacters { get; init; } = 8_000;
    public int MaxCandidateCount { get; init; } = 64;
    public int MaxCandidateCharacters { get; init; } = 8_000;
    public int MaxSourceRefsPerArtifact { get; init; } = 64;
    public int MaxGrantsPerResource { get; init; } = 64;
    public int MaxGrantsPerInvocation { get; init; } = 256;
    public int MaxResourceHandlesPerInvocation { get; init; } = 128;
    public int MaxActiveResourceHandlesPerResource { get; init; } = 64;
    public int MaxAuditFacts { get; init; } = 32;
    public int MaxTagsPerResource { get; init; } = 32;
    public TimeSpan ExpansionGrantLifetime { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan ResourceHandleLifetime { get; init; } = TimeSpan.FromMinutes(30);
}

public interface IAgentMemoryToolAccessScopeProvider
{
    ValueTask<AgentMemoryToolAccessScope> ResolveAsync(
        AgentMemoryToolPrincipal principal,
        CancellationToken cancellationToken = default);
}

public interface IAgentMemoryHistoryAccessAuthorizer
{
    ValueTask<bool> IsAuthorizedAsync(
        AgentMemoryToolPrincipal principal,
        AgentMemoryToolAccessScope scope,
        AgentMemoryHistorySourceKind sourceKind,
        string sourceId,
        CancellationToken cancellationToken = default);
}

public interface IAgentMemoryResourceHandleStore
{
    ValueTask<AgentMemoryResourceHandleIssueResult> TryIssueBatchAsync(
        AgentMemorySecurityArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryResourceHandle> handles,
        int maxActiveHandlesPerResource,
        CancellationToken cancellationToken = default);
    ValueTask<AgentMemoryResourceHandleIssueResult> TryIssueBatchAsync(
        AgentMemorySecurityArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryResourceHandle> handles,
        int maxActiveHandlesPerResource,
        int maxActiveHandlesPerInvocation,
        CancellationToken cancellationToken);
    ValueTask<AgentMemoryResourceHandle?> GetAsync(string handleId, CancellationToken cancellationToken = default);
    ValueTask RevokeAsync(string handleId, CancellationToken cancellationToken = default);
}

public interface IAgentMemorySourceGrantStore
{
    ValueTask<AgentMemoryGrantIssueResult> TryIssueBatchAsync(
        AgentMemorySecurityArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemorySourceGrant> grants,
        int maxActiveGrantsPerResource,
        CancellationToken cancellationToken = default);
    ValueTask<AgentMemoryGrantIssueResult> TryIssueBatchAsync(
        AgentMemorySecurityArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemorySourceGrant> grants,
        int maxActiveGrantsPerResource,
        int maxActiveGrantsPerInvocation,
        CancellationToken cancellationToken);
    ValueTask<AgentMemorySourceGrant?> GetAsync(string grantId, CancellationToken cancellationToken = default);
    ValueTask RevokeAsync(string grantId, CancellationToken cancellationToken = default);
}

public interface IAgentMemoryHistoryResourceHandleIssuer
{
    ValueTask<string> IssueAsync(
        AgentMemoryHostArtifactBatchKey hostBatchKey,
        AgentMemoryToolPrincipal principal,
        AgentMemoryHistorySourceKind sourceKind,
        string sourceId,
        CancellationToken cancellationToken = default);
}
