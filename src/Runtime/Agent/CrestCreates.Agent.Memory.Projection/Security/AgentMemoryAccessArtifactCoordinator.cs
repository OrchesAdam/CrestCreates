using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using Microsoft.Extensions.Options;

namespace CrestCreates.Agent.Memory.Projection.Security;

/// <summary>
/// Projection-neutral artifact coordinator. Single preparation boundary for
/// plan hashing, dual-origin binding, and handle/grant issuance.
/// Self-compensates on partial failure. Enforces full invariant validation
/// for all origins and additional checks for TrustedHostOperation.
/// </summary>
internal sealed class AgentMemoryAccessArtifactCoordinator : IAgentMemoryAccessArtifactCoordinator
{
    private readonly IAgentMemoryAccessHandleStore _handleStore;
    private readonly IAgentMemoryAccessGrantStore _grantStore;
    private readonly IAgentMemoryAccessArtifactBatchStore _batchStore;
    private readonly IAgentMemoryArtifactLifetimePolicy _lifetimePolicy;
    private readonly TimeProvider _timeProvider;
    private readonly AgentMemoryProjectionSecurityOptions _options;
    private readonly ConcurrentDictionary<string, TrackedCompensation> _compensationTracking = new(StringComparer.Ordinal);

    public AgentMemoryAccessArtifactCoordinator(
        IAgentMemoryAccessHandleStore handleStore,
        IAgentMemoryAccessGrantStore grantStore,
        IAgentMemoryAccessArtifactBatchStore batchStore,
        IAgentMemoryArtifactLifetimePolicy lifetimePolicy,
        TimeProvider timeProvider,
        IOptions<AgentMemoryProjectionSecurityOptions> options)
    {
        _handleStore = handleStore;
        _grantStore = grantStore;
        _batchStore = batchStore;
        _lifetimePolicy = lifetimePolicy;
        _timeProvider = timeProvider;
        _options = options.Value;
    }

    public async ValueTask<AgentMemoryAccessPreparedArtifacts> PrepareAsync(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemoryAccessScope scope,
        string artifactPurpose,
        int preparationOrdinal,
        IReadOnlyList<AgentMemoryAccessResourceHandle> handles,
        IReadOnlyList<AgentMemoryAccessSourceGrant> grants,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(scope);

        if (principal.CallerKind == AgentMemoryCallerKind.Unknown)
            throw new InvalidOperationException("Principal CallerKind must not be Unknown.");
        if (origin.Kind == AgentMemoryArtifactOriginKind.Unknown)
            throw new InvalidOperationException("Origin Kind must not be Unknown.");

        // Full invariant validation
        ValidateArtifactConsistency(principal, origin, scope, handles, grants);

        // Periodic compensation token cleanup
        SweepExpiredCompensationTokens();

        var batchKey = BuildBatchKey(principal, origin, scope, artifactPurpose, preparationOrdinal, handles, grants);
        var scopeFingerprint = AgentMemoryScopeFingerprint.Compute(scope);

        // Prepare batch entries for idempotency check
        var handleArtifacts = handles.Select(h => new AgentMemoryAccessPreparedArtifact
        {
            Kind = AgentMemorySecurityArtifactKind.ResourceHandle,
            ResourceKind = h.ResourceKind.ToString(),
            ResourceId = h.ResourceId,
            ArtifactId = h.HandleId,
            Disposition = PreparedArtifactDisposition.CreatedByBatch,
            PlanHash = batchKey.ArtifactPlanHash
        }).ToArray();

        var grantArtifacts = grants.Select(g => new AgentMemoryAccessPreparedArtifact
        {
            Kind = AgentMemorySecurityArtifactKind.SourceGrant,
            ResourceKind = g.SourceRef.SourceKind.ToString(),
            ResourceId = g.SourceRef.SourceId,
            ArtifactId = g.GrantId,
            Disposition = PreparedArtifactDisposition.CreatedByBatch,
            PlanHash = batchKey.ArtifactPlanHash
        }).ToArray();

        var allArtifacts = handleArtifacts.Concat(grantArtifacts).ToArray();

        if (allArtifacts.Length > 0)
        {
            await _batchStore.PrepareAsync(batchKey, allArtifacts, cancellationToken);
        }

        AgentMemoryAccessHandleIssueResult? handleResult = null;
        AgentMemoryAccessGrantIssueResult? grantResult = null;
        bool createdNew = false;

        try
        {
            if (handles.Count > 0)
            {
                handleResult = await _handleStore.TryIssueBatchAsync(
                    batchKey, handles,
                    scope.MaxActiveResourceHandlesPerResource,
                    scope.MaxResourceHandlesPerOperation,
                    cancellationToken);

                if (!handleResult.ReusedExisting) createdNew = true;
            }

            if (grants.Count > 0)
            {
                grantResult = await _grantStore.TryIssueBatchAsync(
                    batchKey, grants,
                    scope.MaxGrantsPerResource,
                    scope.MaxGrantsPerOperation,
                    cancellationToken);

                if (!grantResult.ReusedExisting) createdNew = true;
            }
        }
        catch
        {
            // Self-compensate: revoke newly created handles/grants and batch entries
            if (handleResult is not null && !handleResult.ReusedExisting)
            {
                foreach (var h in handleResult.Handles)
                    await _handleStore.RevokeAsync(h.HandleId, principal.CallerKind, CancellationToken.None);

                await _batchStore.RevokeCreatedAsync(batchKey, handleArtifacts, CancellationToken.None);
            }

            if (grantResult is not null && !grantResult.ReusedExisting)
            {
                foreach (var g in grantResult.Grants)
                    await _grantStore.RevokeAsync(g.GrantId, principal.CallerKind, CancellationToken.None);

                await _batchStore.RevokeCreatedAsync(batchKey, grantArtifacts, CancellationToken.None);
            }

            throw;
        }

        // Build receipt
        var receipt = new AgentMemoryArtifactBatchReceipt
        {
            HandleBatch = handleResult is not null
                ? new AgentMemoryArtifactBatchReceipt.BatchReceipt
                {
                    BatchHash = batchKey.ToCanonicalKey(),
                    Count = handleResult.Handles.Count,
                    ReusedExisting = handleResult.ReusedExisting
                }
                : null,
            GrantBatch = grantResult is not null
                ? new AgentMemoryArtifactBatchReceipt.BatchReceipt
                {
                    BatchHash = batchKey.ToCanonicalKey(),
                    Count = grantResult.Grants.Count,
                    ReusedExisting = grantResult.ReusedExisting
                }
                : null
        };

        // Compensation token only when new artifacts were created
        AgentMemoryArtifactCompensationToken? compensationToken = null;
        if (createdNew)
        {
            var tokenId = Guid.NewGuid().ToString("N");
            var artifactIds = new List<string>();
            if (handleResult is not null && !handleResult.ReusedExisting)
                artifactIds.AddRange(handleResult.Handles.Select(h => h.HandleId));
            if (grantResult is not null && !grantResult.ReusedExisting)
                artifactIds.AddRange(grantResult.Grants.Select(g => g.GrantId));

            _compensationTracking[tokenId] = new TrackedCompensation(
                tokenId, principal.CallerKind, artifactIds, _timeProvider.GetUtcNow());

            compensationToken = new AgentMemoryArtifactCompensationToken { TokenId = tokenId };
        }

        return new AgentMemoryAccessPreparedArtifacts
        {
            Handles = handleResult,
            Grants = grantResult,
            CompensationToken = compensationToken,
            Receipt = receipt
        };
    }

    public async ValueTask RevokeCreatedAsync(
        AgentMemoryArtifactCompensationToken token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (!_compensationTracking.TryGetValue(token.TokenId, out var tracked))
            return; // Already consumed or never existed — one-shot

        // Check for token expiry
        if (_timeProvider.GetUtcNow() - tracked.CreatedAt > _options.CompensationTokenTrackingLifetime)
        {
            _compensationTracking.TryRemove(token.TokenId, out _);
            return; // Token expired
        }

        _compensationTracking.TryRemove(token.TokenId, out _);

        foreach (var artifactId in tracked.ArtifactIds)
        {
            // Best-effort revocation — try both stores
            await _handleStore.RevokeAsync(artifactId, tracked.CallerKind, cancellationToken);
            await _grantStore.RevokeAsync(artifactId, tracked.CallerKind, cancellationToken);
        }
    }

    // ────────────────────────────────────
    //  Invariant validation (P0-3)
    // ────────────────────────────────────

    private static void ValidateArtifactConsistency(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemoryAccessScope scope,
        IReadOnlyList<AgentMemoryAccessResourceHandle> handles,
        IReadOnlyList<AgentMemoryAccessSourceGrant> grants)
    {
        var scopeFingerprint = AgentMemoryScopeFingerprint.Compute(scope);

        // 1. Scope-tenant match
        if (!string.Equals(scope.TenantId, principal.TenantId, StringComparison.Ordinal))
            throw new InvalidOperationException("Scope TenantId does not match principal TenantId.");

        // Handle validations (2-6)
        foreach (var handle in handles)
        {
            // 2. Handle Principal must equal calling principal
            if (handle.Principal != principal)
                throw new InvalidOperationException(
                    $"Handle {handle.HandleId}: Principal does not match the calling principal.");

            // 3. Handle ScopeFingerprint must match computed fingerprint
            if (!string.Equals(handle.ScopeFingerprint, scopeFingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Handle {handle.HandleId}: ScopeFingerprint does not match the computed scope fingerprint.");

            // 4. Handle IssuingOperationId must equal origin.OperationId
            if (!string.Equals(handle.IssuingOperationId, origin.OperationId, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Handle {handle.HandleId}: IssuingOperationId does not match origin OperationId.");

            // 5. Handle ExpiresAt > IssuedAt
            if (handle.ExpiresAt <= handle.IssuedAt)
                throw new InvalidOperationException(
                    $"Handle {handle.HandleId}: ExpiresAt must be after IssuedAt.");

            // 6. RequiredDescriptorRefs — all must have Version > 0
            if (handle.RequiredDescriptorRefs.Any(item => item.Version is not > 0))
                throw new InvalidOperationException(
                    $"Handle {handle.HandleId}: RequiredDescriptorRefs must all have Version > 0.");

            // 7. Handle RequiredDescriptorRefs must be subset of scope.VisibleDescriptorRefs
            if (!IsSubsetOf(handle.RequiredDescriptorRefs, scope.VisibleDescriptorRefs))
                throw new InvalidOperationException(
                    $"Handle {handle.HandleId}: RequiredDescriptorRefs are not a subset of scope VisibleDescriptorRefs.");

            // 8. Handle IsUnscoped consistency — except for History resources which are
            // existence-constrained (bound to ResourceId/Tenant/Principal/ScopeFingerprint)
            // rather than descriptor-constrained. History handles have empty refs + IsUnscoped=false.
            var isHistoryResource = handle.ResourceKind is AgentMemoryResourceKind.ConversationHistory
                or AgentMemoryResourceKind.TaskHistory
                or AgentMemoryResourceKind.TaskEvent;
            if (!isHistoryResource && handle.IsUnscoped != (handle.RequiredDescriptorRefs.Count == 0))
                throw new InvalidOperationException(
                    $"Handle {handle.HandleId}: IsUnscoped flag is inconsistent with RequiredDescriptorRefs count.");
        }

        // Grant validations (9-18)
        foreach (var grant in grants)
        {
            // 9. Grant Principal must equal calling principal
            if (grant.Principal != principal)
                throw new InvalidOperationException(
                    $"Grant {grant.GrantId}: Principal does not match the calling principal.");

            // 10. Grant ScopeFingerprint must match computed fingerprint
            if (!string.Equals(grant.ScopeFingerprint, scopeFingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Grant {grant.GrantId}: ScopeFingerprint does not match the computed scope fingerprint.");

            // 11. Grant IssuingOperationId must equal origin.OperationId
            if (!string.Equals(grant.IssuingOperationId, origin.OperationId, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Grant {grant.GrantId}: IssuingOperationId does not match origin OperationId.");

            // 12. Grant SourceRef.TenantId == principal.TenantId
            if (!string.Equals(grant.SourceRef.TenantId, principal.TenantId, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Grant {grant.GrantId}: SourceRef TenantId does not match principal TenantId.");

            // 13. Grant ExpiresAt > IssuedAt
            if (grant.ExpiresAt <= grant.IssuedAt)
                throw new InvalidOperationException(
                    $"Grant {grant.GrantId}: ExpiresAt must be after IssuedAt.");

            // 14. Grant RequiredDescriptorRefs and SourceRef.DescriptorRefs — all must have Version > 0
            if (grant.RequiredDescriptorRefs.Any(item => item.Version is not > 0))
                throw new InvalidOperationException(
                    $"Grant {grant.GrantId}: RequiredDescriptorRefs must all have Version > 0.");
            if (grant.SourceRef.DescriptorRefs.Any(item => item.Version is not > 0))
                throw new InvalidOperationException(
                    $"Grant {grant.GrantId}: SourceRef.DescriptorRefs must all have Version > 0.");

            // 15. Grant RequiredDescriptorRefs must be subset of scope.VisibleDescriptorRefs
            if (!IsSubsetOf(grant.RequiredDescriptorRefs, scope.VisibleDescriptorRefs))
                throw new InvalidOperationException(
                    $"Grant {grant.GrantId}: RequiredDescriptorRefs are not a subset of scope VisibleDescriptorRefs.");

            // 16. Grant SourceRef.DescriptorRefs must be subset of grant.RequiredDescriptorRefs
            if (!IsSubsetOf(grant.SourceRef.DescriptorRefs, grant.RequiredDescriptorRefs))
                throw new InvalidOperationException(
                    $"Grant {grant.GrantId}: SourceRef.DescriptorRefs are not a subset of RequiredDescriptorRefs.");

            // 17. Grant IsUnscoped == (RequiredDescriptorRefs.Count == 0) — consistency check
            if (grant.IsUnscoped != (grant.RequiredDescriptorRefs.Count == 0))
                throw new InvalidOperationException(
                    $"Grant {grant.GrantId}: IsUnscoped flag is inconsistent with RequiredDescriptorRefs count.");

            // 18. If grant.IsUnscoped && !scope.AllowUnscopedMemory — reject unscoped grant in scoped context
            if (grant.IsUnscoped && !scope.AllowUnscopedMemory)
                throw new InvalidOperationException(
                    $"Grant {grant.GrantId}: Unscoped grant not allowed when scope prohibits unscoped memory.");
        }

        // TrustedHostOperation specific validations (19-25)
        if (origin.Kind == AgentMemoryArtifactOriginKind.TrustedHostOperation)
        {
            ValidateTrustedHostOperation(principal, origin, scopeFingerprint, handles, grants);
        }
    }

    private static void ValidateTrustedHostOperation(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        string scopeFingerprint,
        IReadOnlyList<AgentMemoryAccessResourceHandle> handles,
        IReadOnlyList<AgentMemoryAccessSourceGrant> grants)
    {
        // Derive expected source context from handles
        if (handles.Count == 0)
            throw new InvalidOperationException("TrustedHostOperation requires at least one resource handle.");

        // All handles must share the same ResourceKind
        var resourceKind = handles[0].ResourceKind;
        if (handles.Any(h => h.ResourceKind != resourceKind))
            throw new InvalidOperationException("TrustedHostOperation handles must all have the same ResourceKind.");

        // ResourceKind must be ConversationHistory or TaskHistory
        if (resourceKind is not AgentMemoryResourceKind.ConversationHistory
            and not AgentMemoryResourceKind.TaskHistory)
            throw new InvalidOperationException(
                $"TrustedHostOperation handle ResourceKind must be ConversationHistory or TaskHistory, got {resourceKind}.");

        // All handles must share the same ResourceId
        var resourceId = handles[0].ResourceId;
        if (handles.Any(h => !string.Equals(h.ResourceId, resourceId, StringComparison.Ordinal)))
            throw new InvalidOperationException("TrustedHostOperation handles must all have the same ResourceId.");

        var expectedGrantSourceKind = resourceKind switch
        {
            AgentMemoryResourceKind.ConversationHistory => AgentSourceKind.ConversationTurn,
            AgentMemoryResourceKind.TaskHistory => AgentSourceKind.TaskRecord,
            _ => throw new InvalidOperationException(
                $"Unsupported handle ResourceKind for TrustedHostOperation: {resourceKind}")
        };

        // Handle validations specific to TrustedHostOperation (19-22)
        foreach (var handle in handles)
        {
            // 19. Handle ResourceKind must match expected kind (already verified — single kind for batch)
            // 20. Handle ResourceId must match (already verified)

            // 21. Handle RequiredDescriptorRefs.Count == 0 — host handles are unscoped
            if (handle.RequiredDescriptorRefs.Count != 0)
                throw new InvalidOperationException(
                    $"Handle {handle.HandleId}: TrustedHostOperation handles must have zero RequiredDescriptorRefs (are unscoped).");

            // 22. Handle IsUnscoped == false — host handles are resource-bound (existence-constrained),
            // not unscoped (they have ResourceId/Tenant/Principal/ScopeFingerprint binding).
            if (handle.IsUnscoped)
                throw new InvalidOperationException(
                    $"Handle {handle.HandleId}: TrustedHostOperation handles must have IsUnscoped=false (resource-bound, existence-constrained).");
        }

        // Grant validations specific to TrustedHostOperation (23-25)
        foreach (var grant in grants)
        {
            // 23. Grant SourceRef.SourceKind must match expected kind
            if (grant.SourceRef.SourceKind != expectedGrantSourceKind)
                throw new InvalidOperationException(
                    $"Grant {grant.GrantId}: SourceRef.SourceKind {grant.SourceRef.SourceKind} does not match expected {expectedGrantSourceKind} for handle ResourceKind {resourceKind}.");

            // 24. Grant SourceRef.SourceId must match the common handle ResourceId
            if (!string.Equals(grant.SourceRef.SourceId, resourceId, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Grant {grant.GrantId}: SourceRef.SourceId does not match the common handle ResourceId '{resourceId}'.");
        }

        // 25. Validate host operation fingerprint canonical hash profile
        ValidateHostFingerprint(origin.BindingHash);
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

    // ────────────────────────────────────
    //  Plan hash (P0-6a)
    // ────────────────────────────────────

    private static AgentMemoryAccessArtifactBatchKey BuildBatchKey(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemoryAccessScope scope,
        string artifactPurpose,
        int preparationOrdinal,
        IReadOnlyList<AgentMemoryAccessResourceHandle> handles,
        IReadOnlyList<AgentMemoryAccessSourceGrant> grants)
    {
        var planHash = ComputePlanHash(principal, scope, artifactPurpose, handles, grants);
        return new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = origin.Kind,
            OriginBindingHash = origin.BindingHash,
            ArtifactPurpose = artifactPurpose,
            PreparationOrdinal = preparationOrdinal,
            ArtifactPlanHash = planHash
        };
    }

    private static CanonicalHash ComputePlanHash(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryAccessScope scope,
        string purpose,
        IReadOnlyList<AgentMemoryAccessResourceHandle> handles,
        IReadOnlyList<AgentMemoryAccessSourceGrant> grants)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("shape", "projection-artifact-plan-v2");
            writer.WriteString("purpose", purpose);
            writer.WriteString("tenant", principal.TenantId);
            writer.WriteString("user", principal.UserId);
            writer.WriteString("callerKind", principal.CallerKind.ToString());
            writer.WriteString("callerId", principal.CallerId);
            writer.WriteString("securityContextId", principal.SecurityContextId);
            writer.WriteString("scope", AgentMemoryScopeFingerprint.Compute(scope));

            writer.WriteStartArray("handles");
            foreach (var handle in handles.OrderBy(HandleCanonical, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("kind", handle.ResourceKind.ToString());
                writer.WriteString("resource", handle.ResourceId);
                writer.WriteBoolean("unscoped", handle.IsUnscoped);
                writer.WriteNumber("lifetimeTicks", (handle.ExpiresAt - handle.IssuedAt).Ticks);
                WriteDescriptors(writer, handle.RequiredDescriptorRefs);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("grants");
            foreach (var grant in grants.OrderBy(GrantCanonical, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteBoolean("unscoped", grant.IsUnscoped);
                writer.WriteNumber("lifetimeTicks", (grant.ExpiresAt - grant.IssuedAt).Ticks);
                WriteSourceRef(writer, grant.SourceRef);
                WriteDescriptors(writer, grant.RequiredDescriptorRefs);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
            writer.Flush();
        }

        return new CanonicalHash
        {
            Value = Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant(),
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = "projection-artifact-plan",
            Scope = "TenantVisible",
            Purpose = "ArtifactPlan",
            ContractVersion = "canonical-hash-v1",
            CanonicalShapeVersion = "projection-artifact-plan-v2"
        };
    }

    private static void WriteDescriptors(Utf8JsonWriter writer, IReadOnlyList<DescriptorRef> refs)
    {
        writer.WriteStartArray("descriptors");
        foreach (var item in refs.OrderBy(r => r.Namespace, StringComparer.Ordinal)
                     .ThenBy(r => r.Id, StringComparer.Ordinal)
                     .ThenBy(r => r.Version))
        {
            writer.WriteStartObject();
            writer.WriteString("namespace", item.Namespace);
            writer.WriteString("id", item.Id);
            writer.WriteNumber("version", item.Version ?? -1);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteSourceRef(Utf8JsonWriter writer, AgentContextSourceRef source)
    {
        writer.WriteString("sourceKind", source.SourceKind.ToString());
        writer.WriteString("tenant", source.TenantId);
        writer.WriteString("source", source.SourceId);
        if (source.RangeStart is int start) writer.WriteNumber("rangeStart", start);
        else writer.WriteNull("rangeStart");
        if (source.RangeEnd is int end) writer.WriteNumber("rangeEnd", end);
        else writer.WriteNull("rangeEnd");
        if (source.CorrelationId is not null) writer.WriteString("correlation", source.CorrelationId);
        else writer.WriteNull("correlation");
        if (source.CausationId is not null) writer.WriteString("causation", source.CausationId);
        else writer.WriteNull("causation");
        var hash = source.CanonicalContentHash;
        writer.WriteString("contentHash", hash?.Value ?? string.Empty);
        writer.WriteString("contentHashAlgorithm", hash?.Algorithm ?? string.Empty);
        writer.WriteString("contentHashAlgorithmVersion", hash?.AlgorithmVersion ?? string.Empty);
        writer.WriteString("contentHashArtifactKind", hash?.ArtifactKind ?? string.Empty);
        writer.WriteString("contentHashDescriptorKind", hash?.DescriptorKind ?? string.Empty);
        writer.WriteString("contentHashScope", hash?.Scope ?? string.Empty);
        writer.WriteString("contentHashPurpose", hash?.Purpose ?? string.Empty);
        writer.WriteString("contentHashContractVersion", hash?.ContractVersion ?? string.Empty);
        writer.WriteString("contentHashShapeVersion", hash?.CanonicalShapeVersion ?? string.Empty);

        writer.WriteStartArray("sourceDescriptors");
        foreach (var descriptor in source.DescriptorRefs.OrderBy(
                     r => r.Namespace, StringComparer.Ordinal)
                 .ThenBy(r => r.Id, StringComparer.Ordinal)
                 .ThenBy(r => r.Version))
        {
            writer.WriteStartObject();
            writer.WriteString("namespace", descriptor.Namespace);
            writer.WriteString("id", descriptor.Id);
            writer.WriteNumber("version", descriptor.Version ?? -1);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static string HandleCanonical(AgentMemoryAccessResourceHandle handle)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", handle.ResourceKind.ToString());
            writer.WriteString("resource", handle.ResourceId);
            writer.WriteBoolean("unscoped", handle.IsUnscoped);
            writer.WriteNumber("lifetimeTicks", (handle.ExpiresAt - handle.IssuedAt).Ticks);
            WriteDescriptors(writer, handle.RequiredDescriptorRefs);
            writer.WriteEndObject();
            writer.Flush();
        }
        return Convert.ToHexString(buffer.WrittenSpan);
    }

    private static string GrantCanonical(AgentMemoryAccessSourceGrant grant)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("unscoped", grant.IsUnscoped);
            writer.WriteNumber("lifetimeTicks", (grant.ExpiresAt - grant.IssuedAt).Ticks);
            WriteSourceRef(writer, grant.SourceRef);
            WriteDescriptors(writer, grant.RequiredDescriptorRefs);
            writer.WriteEndObject();
            writer.Flush();
        }
        return Convert.ToHexString(buffer.WrittenSpan);
    }

    // ────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────

    private static bool IsSubsetOf(
        IReadOnlyList<DescriptorRef> subset,
        IReadOnlyList<DescriptorRef> superset)
    {
        return subset.All(item =>
            superset.Any(s =>
                string.Equals(s.Namespace, item.Namespace, StringComparison.Ordinal)
                && string.Equals(s.Id, item.Id, StringComparison.Ordinal)
                && s.Version == item.Version));
    }

    private void SweepExpiredCompensationTokens()
    {
        var now = _timeProvider.GetUtcNow();
        var count = 0;
        const int maxSweepPerCall = 10;

        foreach (var kvp in _compensationTracking)
        {
            if (count >= maxSweepPerCall) break;

            if (now - kvp.Value.CreatedAt > _options.CompensationTokenTrackingLifetime)
            {
                _compensationTracking.TryRemove(kvp.Key, out _);
                count++;
            }
        }
    }

    private sealed record TrackedCompensation(
        string TokenId,
        AgentMemoryCallerKind CallerKind,
        IReadOnlyList<string> ArtifactIds,
        DateTimeOffset CreatedAt);
}