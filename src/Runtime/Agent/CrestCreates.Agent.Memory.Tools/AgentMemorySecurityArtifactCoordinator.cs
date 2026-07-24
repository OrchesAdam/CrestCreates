using System.Security.Cryptography;
using System.Text.Json;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Tools;

internal sealed class AgentMemorySecurityArtifactCoordinator : IAgentMemorySecurityArtifactCoordinator
{
    private readonly IAgentMemoryResourceHandleStore _handles;
    private readonly IAgentMemorySourceGrantStore _grants;

    public AgentMemorySecurityArtifactCoordinator(
        IAgentMemoryResourceHandleStore handles,
        IAgentMemorySourceGrantStore grants)
    {
        _handles = handles;
        _grants = grants;
    }

    public async ValueTask<AgentMemoryPreparedSecurityArtifacts> PrepareForAgentToolAsync(
        AgentToolInvocationBindingSnapshot binding,
        AgentMemoryToolPrincipal principal,
        AgentMemoryToolAccessScope scope,
        string artifactPurpose,
        int preparationOrdinal,
        IReadOnlyList<AgentMemoryResourceHandle> handles,
        IReadOnlyList<AgentMemorySourceGrant> grants,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(scope);
        ValidateAgentToolRequest(binding, principal, scope, handles, grants);
        var planHash = AgentMemoryArtifactPlanProjector.Compute(principal, scope, artifactPurpose, handles, grants);
        var originHash = CreateOriginBindingHash(binding);
        var handleResult = (AgentMemoryResourceHandleIssueResult?)null;
        var grantResult = (AgentMemoryGrantIssueResult?)null;
        try
        {
            if (handles.Count > 0)
                handleResult = await _handles.TryIssueBatchAsync(
                    new AgentMemorySecurityArtifactBatchKey
                    {
                        OriginKind = AgentMemorySecurityArtifactBatchOriginKind.AgentToolInvocation,
                        OriginBindingHash = originHash,
                        ArtifactPurpose = $"{artifactPurpose}-handles",
                        PreparationOrdinal = preparationOrdinal,
                        ArtifactPlanHash = planHash
                    }, handles, scope.MaxActiveResourceHandlesPerResource,
                    scope.MaxResourceHandlesPerInvocation, cancellationToken).ConfigureAwait(false);
            if (grants.Count > 0)
                grantResult = await _grants.TryIssueBatchAsync(
                    new AgentMemorySecurityArtifactBatchKey
                    {
                        OriginKind = AgentMemorySecurityArtifactBatchOriginKind.AgentToolInvocation,
                        OriginBindingHash = originHash,
                        ArtifactPurpose = $"{artifactPurpose}-grants",
                        PreparationOrdinal = preparationOrdinal,
                        ArtifactPlanHash = planHash
                    }, grants, scope.MaxGrantsPerResource,
                    scope.MaxGrantsPerInvocation, cancellationToken).ConfigureAwait(false);
            return new AgentMemoryPreparedSecurityArtifacts { Handles = handleResult, Grants = grantResult };
        }
        catch
        {
            await RevokeCreatedAsync(new AgentMemoryPreparedSecurityArtifacts { Handles = handleResult, Grants = grantResult }, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask RevokeCreatedAsync(
        AgentMemoryPreparedSecurityArtifacts prepared,
        CancellationToken cancellationToken = default)
    {
        if (prepared.Handles is { ReusedExisting: false })
            foreach (var handle in prepared.Handles.Handles)
                await _handles.RevokeAsync(handle.HandleId, cancellationToken).ConfigureAwait(false);
        if (prepared.Grants is { ReusedExisting: false })
            foreach (var grant in prepared.Grants.Grants)
                await _grants.RevokeAsync(grant.GrantId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<AgentMemoryPreparedSecurityArtifacts> PrepareForHostAsync(
        AgentMemoryHostArtifactBatchKey hostBatchKey,
        AgentMemoryToolPrincipal principal,
        AgentMemoryToolAccessScope scope,
        AgentMemoryHistorySourceKind sourceKind,
        string sourceId,
        IReadOnlyList<AgentMemoryResourceHandle> handles,
        IReadOnlyList<AgentMemorySourceGrant> grants,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostBatchKey.HostOperationId)
            || string.IsNullOrWhiteSpace(hostBatchKey.ArtifactPurpose)
            || sourceKind is not (AgentMemoryHistorySourceKind.Conversation or AgentMemoryHistorySourceKind.Task)
            || string.IsNullOrWhiteSpace(sourceId))
            throw new InvalidOperationException("A complete trusted Host artifact request is required.");
        if (handles.Count == 0)
            throw new InvalidOperationException("A Host artifact preparation requires at least one resource handle.");
        ValidateHostFingerprint(hostBatchKey.OperationFingerprint);
        ValidateHostRequest(hostBatchKey, principal, scope, sourceKind, sourceId, handles, grants);
        var batch = AgentMemoryHostArtifactBatchProjector.Create(
            hostBatchKey, principal, scope, sourceKind, sourceId, handles, grants);
        AgentMemoryResourceHandleIssueResult? handleResult = null;
        AgentMemoryGrantIssueResult? grantResult = null;
        try
        {
            handleResult = await _handles.TryIssueBatchAsync(
                batch, handles, scope.MaxActiveResourceHandlesPerResource,
                scope.MaxResourceHandlesPerInvocation, cancellationToken).ConfigureAwait(false);
            if (grants.Count > 0)
                grantResult = await _grants.TryIssueBatchAsync(
                    batch with { ArtifactPurpose = $"{batch.ArtifactPurpose}-grants" }, grants,
                    scope.MaxGrantsPerResource, scope.MaxGrantsPerInvocation, cancellationToken).ConfigureAwait(false);
            return new AgentMemoryPreparedSecurityArtifacts { Handles = handleResult, Grants = grantResult };
        }
        catch
        {
            await RevokeCreatedAsync(new AgentMemoryPreparedSecurityArtifacts { Handles = handleResult, Grants = grantResult }, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static CanonicalHash CreateOriginBindingHash(AgentToolInvocationBindingSnapshot binding)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("shape", "agent-tool-origin-binding-v3");
            writer.WriteString("tenantId", binding.LogicalKey.TenantId);
            writer.WriteString("userId", binding.LogicalKey.UserId);
            writer.WriteString("agentId", binding.LogicalKey.AgentId);
            writer.WriteString("executionId", binding.LogicalKey.ExecutionId);
            writer.WriteString("invocationId", binding.LogicalKey.InvocationId);
            writer.WriteString("invocationFingerprint", binding.InvocationFingerprint);
            writer.WriteEndObject();
            writer.Flush();
        }
        return new CanonicalHash
        {
            Value = Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant(),
            Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = "agent-memory-security-artifact-origin-binding", Scope = "TenantVisible",
            Purpose = "SourceBinding", ContractVersion = "memory-security-artifact-v2",
            CanonicalShapeVersion = "agent-tool-origin-binding-v3"
        };
    }

    private static void ValidateAgentToolRequest(
        AgentToolInvocationBindingSnapshot binding,
        AgentMemoryToolPrincipal principal,
        AgentMemoryToolAccessScope scope,
        IReadOnlyList<AgentMemoryResourceHandle> handles,
        IReadOnlyList<AgentMemorySourceGrant> grants)
    {
        var key = binding.LogicalKey;
        if (!string.Equals(key.TenantId, principal.TenantId, StringComparison.Ordinal)
            || !string.Equals(key.UserId, principal.UserId, StringComparison.Ordinal)
            || !string.Equals(key.AgentId, principal.AgentId, StringComparison.Ordinal)
            || !string.Equals(key.ExecutionId, principal.ExecutionId, StringComparison.Ordinal))
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.TenantMismatch, "Invocation binding and artifact principal do not match.");

        var scopeFingerprint = AgentMemoryToolScopeFingerprint.Compute(scope, principal);
        if (handles.Any(handle => handle.Principal != principal
            || !string.Equals(handle.ScopeFingerprint, scopeFingerprint, StringComparison.Ordinal)
            || !string.Equals(handle.IssuingInvocationId, key.InvocationId, StringComparison.Ordinal)
            || handle.ExpiresAt <= handle.IssuedAt
            || handle.RequiredDescriptorRefs.Any(item => item.Version is not > 0)))
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.StateConflict, "Handle preparation is inconsistent with the trusted invocation scope.");
        if (grants.Any(grant => grant.Principal != principal
            || !string.Equals(grant.ScopeFingerprint, scopeFingerprint, StringComparison.Ordinal)
            || !string.Equals(grant.IssuingInvocationId, key.InvocationId, StringComparison.Ordinal)
            || !string.Equals(grant.SourceRef.TenantId, principal.TenantId, StringComparison.Ordinal)
            || grant.ExpiresAt <= grant.IssuedAt
            || grant.RequiredDescriptorRefs.Any(item => item.Version is not > 0)
            || grant.SourceRef.DescriptorRefs.Any(item => item.Version is not > 0)))
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.StateConflict, "Grant preparation is inconsistent with the trusted invocation scope.");
    }

    private static void ValidateHostFingerprint(CanonicalHash fingerprint)
    {
        if (fingerprint is null
            || fingerprint.Value.Length != 64
            || !fingerprint.Value.All(Uri.IsHexDigit)
            || !string.Equals(fingerprint.Algorithm, "SHA-256", StringComparison.Ordinal)
            || !string.Equals(fingerprint.AlgorithmVersion, "sha256-canonical-json-v1", StringComparison.Ordinal)
            || !string.Equals(fingerprint.ArtifactKind, "agent-memory-host-operation", StringComparison.Ordinal)
            || !string.Equals(fingerprint.Scope, "TenantVisible", StringComparison.Ordinal)
            || !string.Equals(fingerprint.Purpose, "HostOperation", StringComparison.Ordinal)
            || !string.Equals(fingerprint.ContractVersion, "memory-security-artifact-v2", StringComparison.Ordinal)
            || !string.Equals(fingerprint.CanonicalShapeVersion, "agent-memory-host-operation-v1", StringComparison.Ordinal))
            throw new InvalidOperationException("Host operation fingerprint does not match the canonical hash profile.");
    }

    private static void ValidateHostRequest(
        AgentMemoryHostArtifactBatchKey hostBatchKey,
        AgentMemoryToolPrincipal principal,
        AgentMemoryToolAccessScope scope,
        AgentMemoryHistorySourceKind sourceKind,
        string sourceId,
        IReadOnlyList<AgentMemoryResourceHandle> handles,
        IReadOnlyList<AgentMemorySourceGrant> grants)
    {
        var expectedKind = sourceKind switch
        {
            AgentMemoryHistorySourceKind.Conversation => AgentMemoryResourceKind.ConversationHistory,
            AgentMemoryHistorySourceKind.Task => AgentMemoryResourceKind.TaskHistory,
            _ => throw new InvalidOperationException("Host history source kind is unsupported.")
        };
        var scopeFingerprint = AgentMemoryToolScopeFingerprint.Compute(scope, principal);
        if (handles.Any(handle => handle.ResourceKind != expectedKind
            || !string.Equals(handle.ResourceId, sourceId, StringComparison.Ordinal)
            || handle.Principal != principal
            || !string.Equals(handle.ScopeFingerprint, scopeFingerprint, StringComparison.Ordinal)
            || !string.Equals(handle.IssuingInvocationId, hostBatchKey.HostOperationId, StringComparison.Ordinal)
            || handle.RequiredDescriptorRefs.Count != 0
            || handle.IsUnscoped
            || handle.ExpiresAt <= handle.IssuedAt))
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.StateConflict, "Host handle preparation is inconsistent with the trusted history request.");

        var expectedSourceKind = sourceKind switch
        {
            AgentMemoryHistorySourceKind.Conversation => AgentSourceKind.ConversationTurn,
            AgentMemoryHistorySourceKind.Task => AgentSourceKind.TaskRecord,
            _ => throw new InvalidOperationException("Host history source kind is unsupported.")
        };
        if (grants.Any(grant => grant.Principal != principal
            || !string.Equals(grant.SourceRef.TenantId, principal.TenantId, StringComparison.Ordinal)
            || grant.SourceRef.SourceKind != expectedSourceKind
            || !string.Equals(grant.SourceRef.SourceId, sourceId, StringComparison.Ordinal)
            || !string.Equals(grant.IssuingInvocationId, hostBatchKey.HostOperationId, StringComparison.Ordinal)
            || !string.Equals(grant.ScopeFingerprint, scopeFingerprint, StringComparison.Ordinal)
            || grant.ExpiresAt <= grant.IssuedAt
            || grant.RequiredDescriptorRefs.Any(item => item.Version is not > 0)
            || grant.SourceRef.DescriptorRefs.Any(item => item.Version is not > 0)
            || grant.RequiredDescriptorRefs.Any(item => !scope.VisibleDescriptorRefs.Contains(item))
            || grant.SourceRef.DescriptorRefs.Any(item => !grant.RequiredDescriptorRefs.Contains(item))
            || grant.IsUnscoped != (grant.RequiredDescriptorRefs.Count == 0)
            || (grant.IsUnscoped && !scope.AllowUnscopedMemory)))
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.StateConflict, "Host grant preparation is inconsistent with the trusted history request.");
    }
}
