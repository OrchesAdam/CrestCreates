using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;

using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// Immutable snapshot of a resolved descriptor resource.
/// </summary>
internal sealed record DescriptorResourceSnapshot(IDescriptor Descriptor, DescriptorRef Ref);

/// <summary>
/// Immutable snapshot of a resolved draft resource.
/// </summary>
internal sealed record DraftResourceSnapshot(Draft Draft);

/// <summary>
/// Status of a resource resolution attempt.
/// </summary>
internal enum ResourceResolutionStatus
{
    /// <summary>Resource was found and snapshot is available.</summary>
    Resolved,
    /// <summary>Resource was not found within the invocation tenant.</summary>
    NotFound,
    /// <summary>Multiple candidates found for an unpinned reference — caller must specify a version.</summary>
    Ambiguous
}

/// <summary>
/// Result of a resource resolution attempt carrying either a snapshot or a status.
/// </summary>
internal sealed record ResourceResolution<T>(ResourceResolutionStatus Status, T? Snapshot)
    where T : class
{
    /// <summary>Creates a successful resolution with the given snapshot.</summary>
    public static ResourceResolution<T> Found(T snapshot) => new(ResourceResolutionStatus.Resolved, snapshot);

    /// <summary>Creates a not-found resolution.</summary>
    public static ResourceResolution<T> Missing() => new(ResourceResolutionStatus.NotFound, null);

    /// <summary>Creates an ambiguous resolution (multiple candidates for an unpinned ref).</summary>
    public static ResourceResolution<T> AmbiguousResult() => new(ResourceResolutionStatus.Ambiguous, null);
}

/// <summary>
/// Tenant-safe resource resolver that produces immutable snapshots for authorization and execution.
/// Resolves each resource once; the snapshot is reused for both kind visibility evaluation
/// and business execution, preventing TOCTOU inconsistencies.
/// Never crosses tenant boundaries — all lookups are scoped to the invocation tenant.
///
/// <para>Visibility-aware resolution:</para>
/// When a visibility scope is provided, the resolver uses a two-pass strategy:
/// <list type="number">
///   <item>Resolve against the full catalog to obtain the authoritative snapshot</item>
///   <item>For ambiguous unpinned refs, resolve against visible descriptors only to
///         prevent leaking the existence of denied descriptor versions</item>
/// </list>
/// This ensures that a denied kind resolves to the snapshot (so the caller can return
/// Denied per spec §6.2), while ambiguous refs don't reveal denied version counts.
/// </summary>
internal sealed class AgentControlPlaneResourceResolver
{
    private readonly IDescriptorDraftStore _draftStore;
    private readonly IDescriptorCatalog _descriptorCatalog;

    public AgentControlPlaneResourceResolver(
        IDescriptorDraftStore draftStore,
        IDescriptorCatalog descriptorCatalog)
    {
        _draftStore = draftStore;
        _descriptorCatalog = descriptorCatalog;
    }

    /// <summary>
    /// Resolves a draft by tenant and ID, producing an immutable snapshot.
    /// Returns <see cref="ResourceResolutionStatus.NotFound"/> if the draft does not exist
    /// within the invocation tenant.
    /// </summary>
    public async Task<ResourceResolution<DraftResourceSnapshot>> ResolveDraftAsync(
        string tenantId, string draftId, CancellationToken ct)
    {
        var draft = await _draftStore.GetAsync(tenantId, draftId, ct);
        if (draft is null)
            return ResourceResolution<DraftResourceSnapshot>.Missing();

        return ResourceResolution<DraftResourceSnapshot>.Found(new DraftResourceSnapshot(draft));
    }

    /// <summary>
    /// Resolves a descriptor by reference against the full catalog.
    /// Use <see cref="ResolveDescriptor(DescriptorRef, AgentDescriptorVisibilityScope)"/> instead
    /// when a visibility scope is available, to prevent leaking denied descriptor existence
    /// through ambiguous responses.
    /// </summary>
    public ResourceResolution<DescriptorResourceSnapshot> ResolveDescriptor(DescriptorRef descriptorRef)
    {
        var allDescriptors = _descriptorCatalog.GetAll().ToList();
        return ResolveFromList(descriptorRef, allDescriptors);
    }

    /// <summary>
    /// Resolves a descriptor by reference with visibility-aware ambiguity handling.
    ///
    /// <para>Strategy:</para>
    /// <list type="number">
    ///   <item>For version-pinned refs: resolve against the full catalog so that
    ///         a denied kind is resolved to a snapshot (caller returns Denied per spec §6.2)</item>
    ///   <item>For unpinned refs that are unambiguous in the full catalog: same as pinned</item>
    ///   <item>For unpinned refs that are ambiguous in the full catalog: resolve against
    ///         visible descriptors only. If only one visible match, resolve it. If multiple
    ///         visible matches, return Ambiguous. If no visible matches, return NotFound
    ///         (don't reveal that denied versions exist).</item>
    /// </list>
    /// </summary>
    public ResourceResolution<DescriptorResourceSnapshot> ResolveDescriptor(
        DescriptorRef descriptorRef,
        AgentDescriptorVisibilityScope scope)
    {
        var allDescriptors = _descriptorCatalog.GetAll().ToList();
        return ResolveDescriptor(descriptorRef, scope, allDescriptors);
    }

    /// <summary>
    /// Resolves a descriptor using a pre-fetched catalog snapshot.
    /// Use this overload when the caller has already captured the full catalog
    /// (e.g. via <see cref="AgentVisibleDescriptorUniverse.AllTenantDescriptors"/>)
    /// to guarantee a single consistent snapshot for authorization and construction,
    /// eliminating TOCTOU between the universe build and ref resolution.
    /// </summary>
    public ResourceResolution<DescriptorResourceSnapshot> ResolveDescriptor(
        DescriptorRef descriptorRef,
        AgentDescriptorVisibilityScope scope,
        IReadOnlyList<IDescriptor> allDescriptors)
    {
        // For version-pinned refs, resolve against full catalog.
        // The caller will apply DenyIfInvisible on the resolved kind.
        if (descriptorRef.Version.HasValue)
        {
            return ResolveFromList(descriptorRef, allDescriptors);
        }

        // For unpinned refs, try the full catalog first.
        var fullResult = ResolveFromList(descriptorRef, allDescriptors);

        // If unambiguous in the full catalog, return it (caller checks visibility).
        if (fullResult.Status != ResourceResolutionStatus.Ambiguous)
            return fullResult;

        // Ambiguous in the full catalog — resolve against visible descriptors only.
        // This prevents leaking the existence of denied descriptor versions.
        var visibleDescriptors = scope.Filter(allDescriptors, d => d.Kind);
        return ResolveFromList(descriptorRef, visibleDescriptors);
    }

    /// <summary>
    /// Batch-loads drafts for the given tenant and filters to the requested
    /// draft IDs. Returns a dictionary keyed by DraftId.
    /// This is the batch equivalent of <see cref="ResolveDraftAsync"/> for
    /// indirect-owner resolution, avoiding N+1 store reads.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, Draft>> ResolveOwnersAsync(
        string tenantId, IEnumerable<string> draftIds, CancellationToken ct)
    {
        var requested = draftIds.ToHashSet(StringComparer.Ordinal);
        var drafts = await _draftStore.ListAsync(tenantId, null, ct);
        return drafts
            .Where(d => requested.Contains(d.DraftId))
            .ToDictionary(d => d.DraftId, StringComparer.Ordinal);
    }

    // ── Private helpers ──

    /// <summary>
    /// Core resolution logic operating on a pre-filtered descriptor list.
    /// Both the visibility-unaware and visibility-aware overloads delegate here.
    /// </summary>
    private static ResourceResolution<DescriptorResourceSnapshot> ResolveFromList(
        DescriptorRef descriptorRef,
        IReadOnlyList<IDescriptor> descriptors)
    {
        if (descriptorRef.Version.HasValue)
        {
            // Version-pinned: exact match on Namespace + Id + Version
            var descriptor = descriptors.FirstOrDefault(d =>
                d.Namespace == descriptorRef.Namespace &&
                d.Id == descriptorRef.Id &&
                d is IVersionedDescriptor vd &&
                vd.Version == descriptorRef.Version.Value);

            if (descriptor is null)
                return ResourceResolution<DescriptorResourceSnapshot>.Missing();

            var resolvedRef = new DescriptorRef(descriptor.Namespace, descriptor.Id, descriptorRef.Version.Value);
            return ResourceResolution<DescriptorResourceSnapshot>.Found(
                new DescriptorResourceSnapshot(descriptor, resolvedRef));
        }

        // Unpinned: match Namespace + Id, check for ambiguity
        var matches = descriptors
            .Where(d => d.Namespace == descriptorRef.Namespace && d.Id == descriptorRef.Id)
            .ToList();

        if (matches.Count == 0)
            return ResourceResolution<DescriptorResourceSnapshot>.Missing();

        if (matches.Count > 1)
            return ResourceResolution<DescriptorResourceSnapshot>.AmbiguousResult();

        var single = matches[0];
        var refWithVersion = single is IVersionedDescriptor vdesc
            ? new DescriptorRef(single.Namespace, single.Id, vdesc.Version)
            : new DescriptorRef(single.Namespace, single.Id);

        return ResourceResolution<DescriptorResourceSnapshot>.Found(
            new DescriptorResourceSnapshot(single, refWithVersion));
    }
}
