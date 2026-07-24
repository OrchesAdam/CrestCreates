using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions.Security;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.ReadCore;

/// <summary>
/// Closes the planned-artifact → confirmed-artifact boundary before any
/// credential is projected to a caller.
/// </summary>
internal static class AgentMemoryPreparedArtifactContractVerifier
{
    public static IReadOnlyDictionary<string, AgentMemoryAccessResourceHandle> VerifyHandles(
        IReadOnlyDictionary<string, AgentMemoryAccessResourceHandle> plan,
        IReadOnlyList<AgentMemoryAccessResourceHandle>? confirmed)
        => VerifyExactSet(
            plan,
            confirmed,
            static handle => handle.ResourceId,
            "handle-contract",
            "handle",
            "ResourceId",
            ValidateHandle);

    public static IReadOnlyDictionary<AgentMemorySourceKey, AgentMemoryAccessSourceGrant> VerifyGrants(
        IReadOnlyDictionary<AgentMemorySourceKey, AgentMemoryAccessSourceGrant> plan,
        IReadOnlyList<AgentMemoryAccessSourceGrant>? confirmed)
        => VerifyExactSet(
            plan,
            confirmed,
            static grant => new AgentMemorySourceKey(
                grant.SourceRef.TenantId,
                grant.SourceRef.SourceKind,
                grant.SourceRef.SourceId,
                grant.SourceRef.RangeStart,
                grant.SourceRef.RangeEnd),
            "grant-contract",
            "grant",
            "SourceKey",
            ValidateGrant);

    private static IReadOnlyDictionary<TKey, TArtifact> VerifyExactSet<TKey, TArtifact>(
        IReadOnlyDictionary<TKey, TArtifact> plan,
        IReadOnlyList<TArtifact>? confirmed,
        Func<TArtifact, TKey> keySelector,
        string contractCode,
        string artifactName,
        string keyName,
        Action<TArtifact, TArtifact, TKey> validate)
        where TKey : notnull
        where TArtifact : class
    {
        var confirmedByKey = new Dictionary<TKey, TArtifact>(plan.Count, GetComparer(plan));

        foreach (var artifact in confirmed ?? Array.Empty<TArtifact>())
        {
            if (artifact is null)
            {
                throw new AgentMemoryReadCoreException(
                    contractCode,
                    $"Coordinator returned a null confirmed {artifactName}");
            }

            TKey key;
            try
            {
                key = keySelector(artifact);
            }
            catch (Exception exception) when (exception is NullReferenceException or ArgumentException)
            {
                throw new AgentMemoryReadCoreException(
                    contractCode,
                    $"Coordinator returned a malformed confirmed {artifactName}");
            }

            if (!confirmedByKey.TryAdd(key, artifact))
            {
                throw new AgentMemoryReadCoreException(
                    contractCode,
                    $"Coordinator returned duplicate confirmed {artifactName} for {keyName} {key}");
            }

            if (!plan.TryGetValue(key, out var planned))
            {
                throw new AgentMemoryReadCoreException(
                    contractCode,
                    $"Coordinator returned unexpected confirmed {artifactName} for {keyName} {key}");
            }

            validate(planned, artifact, key);
        }

        foreach (var requestedKey in plan.Keys)
        {
            if (!confirmedByKey.ContainsKey(requestedKey))
            {
                throw new AgentMemoryReadCoreException(
                    contractCode,
                    $"Coordinator did not confirm {artifactName} for {keyName} {requestedKey}");
            }
        }

        return confirmedByKey;
    }

    private static IEqualityComparer<TKey> GetComparer<TKey, TArtifact>(
        IReadOnlyDictionary<TKey, TArtifact> plan)
        where TKey : notnull
        => plan is Dictionary<TKey, TArtifact> dictionary
            ? dictionary.Comparer
            : EqualityComparer<TKey>.Default;

    private static void ValidateHandle(
        AgentMemoryAccessResourceHandle planned,
        AgentMemoryAccessResourceHandle confirmed,
        string resourceId)
    {
        if (string.IsNullOrWhiteSpace(confirmed.HandleId))
        {
            throw new AgentMemoryReadCoreException(
                "handle-contract",
                $"Confirmed handle for ResourceId {resourceId} has an empty HandleId");
        }

        if (confirmed.Principal != planned.Principal)
        {
            throw new AgentMemoryReadCoreException(
                "handle-contract",
                $"Confirmed handle for ResourceId {resourceId} has mismatched Principal");
        }

        if (confirmed.ScopeFingerprint != planned.ScopeFingerprint)
        {
            throw new AgentMemoryReadCoreException(
                "handle-contract",
                $"Confirmed handle for ResourceId {resourceId} has mismatched ScopeFingerprint");
        }

        if (confirmed.ResourceKind != planned.ResourceKind)
        {
            throw new AgentMemoryReadCoreException(
                "handle-contract",
                $"Confirmed handle for ResourceId {resourceId} has mismatched ResourceKind");
        }

        if (confirmed.IsUnscoped != planned.IsUnscoped
            || !CanonicalRefSetEquals(confirmed.RequiredDescriptorRefs, planned.RequiredDescriptorRefs))
        {
            throw new AgentMemoryReadCoreException(
                "handle-contract",
                $"Confirmed handle for ResourceId {resourceId} has mismatched descriptor binding");
        }
    }

    private static void ValidateGrant(
        AgentMemoryAccessSourceGrant planned,
        AgentMemoryAccessSourceGrant confirmed,
        AgentMemorySourceKey sourceKey)
    {
        if (string.IsNullOrWhiteSpace(confirmed.GrantId))
        {
            throw new AgentMemoryReadCoreException(
                "grant-contract",
                $"Confirmed grant for SourceKey {sourceKey} has an empty GrantId");
        }

        if (confirmed.Principal != planned.Principal)
        {
            throw new AgentMemoryReadCoreException(
                "grant-contract",
                $"Confirmed grant for SourceKey {sourceKey} has mismatched Principal");
        }

        if (confirmed.ScopeFingerprint != planned.ScopeFingerprint)
        {
            throw new AgentMemoryReadCoreException(
                "grant-contract",
                $"Confirmed grant for SourceKey {sourceKey} has mismatched ScopeFingerprint");
        }

        if (confirmed.IsUnscoped != planned.IsUnscoped
            || !CanonicalRefSetEquals(confirmed.RequiredDescriptorRefs, planned.RequiredDescriptorRefs)
            || !CanonicalRefSetEquals(
                confirmed.SourceRef.DescriptorRefs ?? Array.Empty<DescriptorRef>(),
                planned.SourceRef.DescriptorRefs ?? Array.Empty<DescriptorRef>()))
        {
            throw new AgentMemoryReadCoreException(
                "grant-contract",
                $"Confirmed grant for SourceKey {sourceKey} has mismatched descriptor binding");
        }
    }

    private static bool CanonicalRefSetEquals(
        IReadOnlyList<DescriptorRef>? left,
        IReadOnlyList<DescriptorRef>? right)
    {
        left ??= Array.Empty<DescriptorRef>();
        right ??= Array.Empty<DescriptorRef>();

        if (left.Count != right.Count)
            return false;

        return new HashSet<DescriptorRef>(left).SetEquals(right);
    }
}
