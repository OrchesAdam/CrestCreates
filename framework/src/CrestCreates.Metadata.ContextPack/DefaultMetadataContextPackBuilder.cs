using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.ContextPack.Abstractions;

namespace CrestCreates.Metadata.ContextPack;

public sealed class DefaultMetadataContextPackBuilder : IMetadataContextPackBuilder
{
    private readonly IDescriptorStableHashBuilder? _hashBuilder;

    public DefaultMetadataContextPackBuilder(IDescriptorStableHashBuilder? hashBuilder = null)
    {
        _hashBuilder = hashBuilder;
    }

    public MetadataContextPack Build(
        MetadataContextPackRequest request,
        DescriptorTopologySnapshot topology,
        IReadOnlyList<IDescriptor> descriptors)
    {
        var diagnostics = new List<MetadataContextPackDiagnostic>();

        // 1. Validate request
        ValidateRequest(request, diagnostics);

        // 2. Snapshot request collections defensively
        var snapshotRequest = SnapshotRequest(request);

        // 3. Build descriptor index
        var descriptorIndex = BuildDescriptorIndex(descriptors);

        // 4. Resolve focus nodes
        var focusRefs = snapshotRequest.FocusDescriptors;
        var foundFocusRefs = new List<DescriptorRef>();
        var missingFocusRefs = new List<DescriptorRef>();

        foreach (var focusRef in focusRefs)
        {
            if (topology.Contains(focusRef))
            {
                foundFocusRefs.Add(topology.FindNode(focusRef)!.Ref);
            }
            else
            {
                missingFocusRefs.Add(focusRef);
                diagnostics.Add(new MetadataContextPackDiagnostic
                {
                    Severity = MetadataContextPackDiagnosticSeverity.Warning,
                    Code = MetadataContextPackDiagnosticCodes.FocusNotFound,
                    Message = $"Focus descriptor '{focusRef.FullId}' not found in topology.",
                    Subject = focusRef
                });
            }
        }

        // 5. Scope-driven traversal
        var includedRefs = new HashSet<DescriptorRef>();
        var includedEdges = new List<DescriptorEdge>();
        int traversalDepthReached = 0;

        switch (snapshotRequest.Scope)
        {
            case MetadataContextPackScope.FocusOnly:
                foreach (var r in foundFocusRefs) includedRefs.Add(r);
                traversalDepthReached = 0;
                break;

            case MetadataContextPackScope.DirectDependencies:
                ResolveDirectDependencies(foundFocusRefs, topology, includedRefs, includedEdges);
                traversalDepthReached = 1;
                break;

            case MetadataContextPackScope.DirectDependents:
                ResolveDirectDependents(foundFocusRefs, topology, includedRefs, includedEdges);
                traversalDepthReached = 1;
                break;

            case MetadataContextPackScope.ImpactRadius:
                traversalDepthReached = ResolveImpactRadius(foundFocusRefs, topology, snapshotRequest.MaxTraversalDepth, includedRefs, includedEdges, diagnostics);
                break;

            case MetadataContextPackScope.RuntimeScenario:
                traversalDepthReached = ResolveRuntimeScenario(foundFocusRefs, topology, snapshotRequest, includedRefs, includedEdges, diagnostics);
                break;
        }

        // 6. Apply kind filters (non-focus only)
        var focusSet = new HashSet<DescriptorRef>(foundFocusRefs);
        ApplyKindFilters(includedRefs, focusSet, snapshotRequest, topology, diagnostics);

        // 7. Apply count bounds (non-focus only)
        ApplyCountBounds(includedRefs, focusSet, snapshotRequest.MaxDescriptorCount, diagnostics);

        // 8. Collect relationship edges
        var relationshipEntries = CollectRelationshipEntries(includedRefs, includedEdges, topology);

        // 9. Build descriptor entries
        var descriptorEntries = BuildDescriptorEntries(includedRefs, focusSet, descriptorIndex, topology, snapshotRequest, diagnostics);

        // 10. Build summary
        var summary = BuildSummary(descriptorEntries, relationshipEntries, foundFocusRefs, diagnostics, traversalDepthReached);

        // 11. Sort output deterministically
        var sortedDescriptors = SortDescriptors(descriptorEntries);
        var sortedRelationships = SortRelationships(relationshipEntries);
        var sortedDiagnostics = SortDiagnostics(diagnostics);

        return new MetadataContextPack
        {
            Request = snapshotRequest,
            Descriptors = sortedDescriptors,
            Relationships = sortedRelationships,
            Summary = summary,
            Diagnostics = sortedDiagnostics
        };
    }

    private static void ValidateRequest(MetadataContextPackRequest request, List<MetadataContextPackDiagnostic> diagnostics)
    {
        if (request.Scope == MetadataContextPackScope.RuntimeScenario && request.ScenarioRecipe is null)
        {
            diagnostics.Add(new MetadataContextPackDiagnostic
            {
                Severity = MetadataContextPackDiagnosticSeverity.Error,
                Code = MetadataContextPackDiagnosticCodes.RecipeMissing,
                Message = "RuntimeScenario scope requires a ScenarioRecipe."
            });
        }
    }

    private static MetadataContextPackRequest SnapshotRequest(MetadataContextPackRequest request)
    {
        return request with
        {
            FocusDescriptors = request.FocusDescriptors.ToArray(),
            IncludeKinds = request.IncludeKinds?.ToArray(),
            ExcludeKinds = request.ExcludeKinds?.ToArray(),
            ScenarioRecipe = request.ScenarioRecipe is null ? null :
                request.ScenarioRecipe with { Steps = request.ScenarioRecipe.Steps.ToArray() }
        };
    }

    private static Dictionary<DescriptorRef, IDescriptor> BuildDescriptorIndex(IReadOnlyList<IDescriptor> descriptors)
    {
        var index = new Dictionary<DescriptorRef, IDescriptor>();
        foreach (var d in descriptors)
        {
            // Index by exact ref including version from IVersionedDescriptor
            var version = d is IVersionedDescriptor vd ? vd.Version : (int?)null;
            var exactKey = new DescriptorRef(d.Namespace, d.Id, version);
            index[exactKey] = d;

            // Also store by unpinned key as fallback, but don't overwrite an existing entry
            var unpinnedKey = new DescriptorRef(d.Namespace, d.Id, null);
            if (!index.ContainsKey(unpinnedKey))
                index[unpinnedKey] = d;
        }
        return index;
    }

    private static IDescriptor? FindDescriptor(
        DescriptorRef ref_, Dictionary<DescriptorRef, IDescriptor> descriptorIndex)
    {
        // Try exact match first (version-aware)
        if (ref_.Version.HasValue && descriptorIndex.TryGetValue(ref_, out var exact))
            return exact;

        // Fallback to unpinned lookup
        var unpinnedKey = new DescriptorRef(ref_.Namespace, ref_.Id, null);
        if (descriptorIndex.TryGetValue(unpinnedKey, out var unpinned))
            return unpinned;

        return null;
    }

    private static void ResolveDirectDependencies(
        List<DescriptorRef> focusRefs, DescriptorTopologySnapshot topology,
        HashSet<DescriptorRef> includedRefs, List<DescriptorEdge> includedEdges)
    {
        foreach (var focusRef in focusRefs)
        {
            includedRefs.Add(focusRef);
            var deps = topology.GetDirectDependencies(focusRef);
            foreach (var dep in deps)
            {
                includedRefs.Add(dep.Ref);
            }

            var focusNode = topology.FindNode(focusRef);
            if (focusNode is not null)
            {
                foreach (var edgeIdx in focusNode.OutgoingEdgeIndices)
                {
                    includedEdges.Add(topology.Edges[edgeIdx]);
                }
            }
        }
    }

    private static void ResolveDirectDependents(
        List<DescriptorRef> focusRefs, DescriptorTopologySnapshot topology,
        HashSet<DescriptorRef> includedRefs, List<DescriptorEdge> includedEdges)
    {
        foreach (var focusRef in focusRefs)
        {
            includedRefs.Add(focusRef);
            var dependents = topology.GetDirectDependents(focusRef);
            foreach (var dep in dependents)
            {
                includedRefs.Add(dep.Ref);
            }

            var focusNode = topology.FindNode(focusRef);
            if (focusNode is not null)
            {
                foreach (var edgeIdx in focusNode.IncomingEdgeIndices)
                {
                    includedEdges.Add(topology.Edges[edgeIdx]);
                }
            }
        }
    }

    private static int ResolveImpactRadius(
        List<DescriptorRef> focusRefs, DescriptorTopologySnapshot topology, int maxDepth,
        HashSet<DescriptorRef> includedRefs, List<DescriptorEdge> includedEdges,
        List<MetadataContextPackDiagnostic> diagnostics)
    {
        var visited = new HashSet<DescriptorRef>();
        var frontier = new List<DescriptorRef>();

        // Depth 0: focus nodes
        foreach (var r in focusRefs)
        {
            var node = topology.FindNode(r);
            if (node is not null && visited.Add(node.Ref))
            {
                includedRefs.Add(node.Ref);
                frontier.Add(node.Ref);
            }
        }

        var depthReached = 0;

        for (int depth = 1; depth <= maxDepth; depth++)
        {
            var nextFrontier = new List<DescriptorRef>();
            foreach (var currentRef in frontier)
            {
                var currentNode = topology.FindNode(currentRef);
                if (currentNode is null) continue;

                // Follow outgoing edges
                foreach (var edgeIdx in currentNode.OutgoingEdgeIndices)
                {
                    var edge = topology.Edges[edgeIdx];
                    includedEdges.Add(edge);
                    var target = topology.FindNode(edge.To);
                    if (target is not null && visited.Add(target.Ref))
                    {
                        includedRefs.Add(target.Ref);
                        nextFrontier.Add(target.Ref);
                    }
                }

                // Follow incoming edges
                foreach (var edgeIdx in currentNode.IncomingEdgeIndices)
                {
                    var edge = topology.Edges[edgeIdx];
                    includedEdges.Add(edge);
                    var source = topology.FindNode(edge.From);
                    if (source is not null && visited.Add(source.Ref))
                    {
                        includedRefs.Add(source.Ref);
                        nextFrontier.Add(source.Ref);
                    }
                }
            }

            if (nextFrontier.Count > 0)
            {
                depthReached = depth;
            }

            frontier = nextFrontier;
        }

        // Check if there are actually unvisited neighbors beyond the max-depth frontier
        var hasUnvisitedBeyond = false;
        foreach (var frontierRef in frontier)
        {
            var frontierNode = topology.FindNode(frontierRef);
            if (frontierNode is null) continue;

            // Check outgoing edges
            foreach (var edgeIdx in frontierNode.OutgoingEdgeIndices)
            {
                var edge = topology.Edges[edgeIdx];
                var target = topology.FindNode(edge.To);
                if (target is not null && !visited.Contains(target.Ref))
                {
                    hasUnvisitedBeyond = true;
                    break;
                }
            }

            if (hasUnvisitedBeyond) break;

            // Check incoming edges
            foreach (var edgeIdx in frontierNode.IncomingEdgeIndices)
            {
                var edge = topology.Edges[edgeIdx];
                var source = topology.FindNode(edge.From);
                if (source is not null && !visited.Contains(source.Ref))
                {
                    hasUnvisitedBeyond = true;
                    break;
                }
            }

            if (hasUnvisitedBeyond) break;
        }

        if (hasUnvisitedBeyond)
        {
            diagnostics.Add(new MetadataContextPackDiagnostic
            {
                Severity = MetadataContextPackDiagnosticSeverity.Info,
                Code = MetadataContextPackDiagnosticCodes.TruncatedByDepth,
                Message = $"Traversal truncated at depth {maxDepth}. Additional nodes exist beyond this depth."
            });
        }

        return depthReached;
    }

    private static int ResolveRuntimeScenario(
        List<DescriptorRef> focusRefs, DescriptorTopologySnapshot topology,
        MetadataContextPackRequest request,
        HashSet<DescriptorRef> includedRefs, List<DescriptorEdge> includedEdges,
        List<MetadataContextPackDiagnostic> diagnostics)
    {
        var recipe = request.ScenarioRecipe;
        if (recipe is null) return 0;

        // Add focus refs
        foreach (var r in focusRefs) includedRefs.Add(r);

        var boundary = new HashSet<DescriptorRef>(focusRefs);
        var maxDepthReached = 0;

        foreach (var step in recipe.Steps)
        {
            var stepVisited = new HashSet<DescriptorRef>(boundary);

            for (int depth = 1; depth <= step.MaxDepth; depth++)
            {
                var nextBoundary = new HashSet<DescriptorRef>();

                foreach (var currentRef in boundary)
                {
                    var currentNode = topology.FindNode(currentRef);
                    if (currentNode is null) continue;

                    // Build edge list with direction tracking for correct target resolution
                    List<(int EdgeIndex, bool IsOutgoing)> directedEdges = new();

                    switch (step.Direction)
                    {
                        case ScenarioTraversalDirection.Dependencies:
                            foreach (var idx in currentNode.OutgoingEdgeIndices)
                                directedEdges.Add((idx, true));
                            break;
                        case ScenarioTraversalDirection.Dependents:
                            foreach (var idx in currentNode.IncomingEdgeIndices)
                                directedEdges.Add((idx, false));
                            break;
                        case ScenarioTraversalDirection.Both:
                            foreach (var idx in currentNode.OutgoingEdgeIndices)
                                directedEdges.Add((idx, true));
                            foreach (var idx in currentNode.IncomingEdgeIndices)
                                directedEdges.Add((idx, false));
                            break;
                    }

                    foreach (var (edgeIdx, isOutgoing) in directedEdges)
                    {
                        var edge = topology.Edges[edgeIdx];

                        if (edge.Kind != step.FollowKind) continue;
                        if (step.Role is not null && edge.Role != step.Role) continue;

                        // Outgoing edges target edge.To; incoming edges target edge.From
                        var targetRef = isOutgoing ? edge.To : edge.From;

                        var targetNode = topology.FindNode(targetRef);
                        if (targetNode is null) continue;

                        if (step.TargetKind.HasValue && targetNode.Kind != step.TargetKind.Value) continue;

                        includedEdges.Add(edge);

                        if (stepVisited.Add(targetNode.Ref))
                        {
                            includedRefs.Add(targetNode.Ref);
                            nextBoundary.Add(targetNode.Ref);
                        }
                    }
                }

                boundary = nextBoundary;
                if (nextBoundary.Count > 0) maxDepthReached = depth;
            }

            // Boundary for next step = all discovered nodes from this step
            boundary = new HashSet<DescriptorRef>(stepVisited);
        }

        return maxDepthReached;
    }

    private static void ApplyKindFilters(
        HashSet<DescriptorRef> includedRefs, HashSet<DescriptorRef> focusSet,
        MetadataContextPackRequest request, DescriptorTopologySnapshot topology,
        List<MetadataContextPackDiagnostic> diagnostics)
    {
        // Check if any focus descriptor would be filtered out
        foreach (var focusRef in focusSet)
        {
            var node = topology.FindNode(focusRef);
            if (node is null) continue;

            var kind = node.Kind;
            var wouldBeExcluded = false;

            if (request.IncludeKinds is not null && !request.IncludeKinds.Contains(kind))
                wouldBeExcluded = true;
            if (request.ExcludeKinds is not null && request.ExcludeKinds.Contains(kind))
                wouldBeExcluded = true;

            if (wouldBeExcluded)
            {
                diagnostics.Add(new MetadataContextPackDiagnostic
                {
                    Severity = MetadataContextPackDiagnosticSeverity.Warning,
                    Code = MetadataContextPackDiagnosticCodes.FocusKindFiltered,
                    Message = $"Focus descriptor '{focusRef.FullId}' has kind {kind} that would be filtered. Focus is still included.",
                    Subject = focusRef
                });
            }
        }

        // Apply filters to non-focus refs
        var toRemove = new List<DescriptorRef>();
        foreach (var ref_ in includedRefs)
        {
            if (focusSet.Contains(ref_)) continue;

            var node = topology.FindNode(ref_);
            if (node is null) continue;

            var kind = node.Kind;
            if (request.IncludeKinds is not null && !request.IncludeKinds.Contains(kind))
            {
                toRemove.Add(ref_);
                continue;
            }
            if (request.ExcludeKinds is not null && request.ExcludeKinds.Contains(kind))
            {
                toRemove.Add(ref_);
            }
        }

        foreach (var r in toRemove)
        {
            includedRefs.Remove(r);
        }

        if (toRemove.Count > 0)
        {
            diagnostics.Add(new MetadataContextPackDiagnostic
            {
                Severity = MetadataContextPackDiagnosticSeverity.Info,
                Code = MetadataContextPackDiagnosticCodes.KindExcluded,
                Message = $"{toRemove.Count} descriptor(s) excluded by kind filters."
            });
        }
    }

    private static void ApplyCountBounds(
        HashSet<DescriptorRef> includedRefs, HashSet<DescriptorRef> focusSet,
        int maxDescriptorCount, List<MetadataContextPackDiagnostic> diagnostics)
    {
        if (includedRefs.Count <= maxDescriptorCount) return;

        // Focus always stays. Remove non-focus descriptors that exceed the limit.
        var nonFocusRefs = includedRefs.Where(r => !focusSet.Contains(r)).ToList();
        var focusCount = focusSet.Count;

        if (focusCount >= maxDescriptorCount)
        {
            // Focus alone exceeds limit — keep all focus, remove all non-focus
            foreach (var r in nonFocusRefs) includedRefs.Remove(r);
        }
        else
        {
            var allowedNonFocus = maxDescriptorCount - focusCount;
            // Remove excess non-focus (sorted deterministically for reproducibility)
            var sortedNonFocus = nonFocusRefs
                .OrderBy(r => r.Namespace, StringComparer.Ordinal)
                .ThenBy(r => r.Id, StringComparer.Ordinal)
                .ThenBy(r => r.Version ?? -1)
                .ToList();

            for (int i = allowedNonFocus; i < sortedNonFocus.Count; i++)
            {
                includedRefs.Remove(sortedNonFocus[i]);
            }
        }

        diagnostics.Add(new MetadataContextPackDiagnostic
        {
            Severity = MetadataContextPackDiagnosticSeverity.Info,
            Code = MetadataContextPackDiagnosticCodes.TruncatedByCount,
            Message = $"Result truncated to {maxDescriptorCount} descriptors.",
            Path = $"MaxDescriptorCount={maxDescriptorCount}"
        });
    }

    private static List<MetadataContextPackRelationshipEntry> CollectRelationshipEntries(
        HashSet<DescriptorRef> includedRefs, List<DescriptorEdge> includedEdges,
        DescriptorTopologySnapshot topology)
    {
        var entries = new List<MetadataContextPackRelationshipEntry>();
        var seen = new HashSet<(DescriptorRef, DescriptorRef, RelationshipKind)>();

        foreach (var edge in includedEdges)
        {
            // Only include edges where both endpoints are in the included set
            var fromResolved = topology.FindNode(edge.From);
            var toResolved = topology.FindNode(edge.To);
            if (fromResolved is null || toResolved is null) continue;
            if (!includedRefs.Contains(fromResolved.Ref) || !includedRefs.Contains(toResolved.Ref)) continue;

            var key = (fromResolved.Ref, toResolved.Ref, edge.Kind);
            if (!seen.Add(key)) continue;

            entries.Add(new MetadataContextPackRelationshipEntry
            {
                From = fromResolved.Ref,
                To = toResolved.Ref,
                Kind = edge.Kind,
                Role = edge.Role,
                SourcePath = edge.SourcePath,
                Strength = edge.Strength,
                IsRuntimeBinding = edge.IsRuntimeBinding
            });
        }

        return entries;
    }

    private List<MetadataContextPackDescriptorEntry> BuildDescriptorEntries(
        HashSet<DescriptorRef> includedRefs, HashSet<DescriptorRef> focusSet,
        Dictionary<DescriptorRef, IDescriptor> descriptorIndex,
        DescriptorTopologySnapshot topology,
        MetadataContextPackRequest request,
        List<MetadataContextPackDiagnostic> diagnostics)
    {
        var entries = new List<MetadataContextPackDescriptorEntry>();

        foreach (var ref_ in includedRefs)
        {
            // Prefer topology node for Kind/Name/State — it is version-aware
            var topologyNode = topology.FindNode(ref_);
            var descriptor = FindDescriptor(ref_, descriptorIndex);

            var kind = topologyNode?.Kind ?? descriptor?.Kind ?? DescriptorKind.Schema;
            var name = topologyNode?.Name ?? descriptor?.Name ?? ref_.Id;
            var state = topologyNode?.State ?? descriptor?.State ?? DescriptorState.Active;

            DescriptorStableHashes? hashes = null;
            if (request.IncludeStableHashes)
            {
                if (_hashBuilder is not null && descriptor is not null)
                {
                    hashes = _hashBuilder.Build(descriptor);
                }
                else if (_hashBuilder is null)
                {
                    // Only emit once
                    if (!diagnostics.Any(d => d.Code == MetadataContextPackDiagnosticCodes.HashBuilderMissing))
                    {
                        diagnostics.Add(new MetadataContextPackDiagnostic
                        {
                            Severity = MetadataContextPackDiagnosticSeverity.Warning,
                            Code = MetadataContextPackDiagnosticCodes.HashBuilderMissing,
                            Message = "IncludeStableHashes is true but no IDescriptorStableHashBuilder is available."
                        });
                    }
                }
            }

            MetadataContextPackGovernanceEntry? governance = null;
            if (request.IncludeGovernanceState)
            {
                governance = new MetadataContextPackGovernanceEntry
                {
                    State = state,
                    RequiresReview = state == DescriptorState.Draft
                };
            }

            entries.Add(new MetadataContextPackDescriptorEntry
            {
                Ref = ref_,
                Kind = kind,
                Name = name,
                State = state,
                Hashes = hashes,
                Governance = governance,
                IsFocus = focusSet.Contains(ref_)
            });
        }

        return entries;
    }

    private static MetadataContextPackSummary BuildSummary(
        List<MetadataContextPackDescriptorEntry> descriptors,
        List<MetadataContextPackRelationshipEntry> relationships,
        List<DescriptorRef> focusRefs,
        List<MetadataContextPackDiagnostic> diagnostics,
        int traversalDepthReached)
    {
        var wasTruncated = diagnostics.Any(d =>
            d.Code == MetadataContextPackDiagnosticCodes.TruncatedByCount ||
            d.Code == MetadataContextPackDiagnosticCodes.TruncatedByDepth);

        return new MetadataContextPackSummary
        {
            TotalDescriptorCount = descriptors.Count,
            DescriptorCountsByKind = descriptors
                .GroupBy(d => d.Kind)
                .ToDictionary(g => g.Key, g => g.Count()),
            TotalRelationshipCount = relationships.Count,
            RelationshipCountsByKind = relationships
                .GroupBy(r => r.Kind)
                .ToDictionary(g => g.Key, g => g.Count()),
            FocusRefs = focusRefs,
            WasTruncated = wasTruncated,
            TruncatedAtCount = wasTruncated && diagnostics.Any(d => d.Code == MetadataContextPackDiagnosticCodes.TruncatedByCount)
                ? descriptors.Count : null,
            TraversalDepthReached = traversalDepthReached
        };
    }

    private static List<MetadataContextPackDescriptorEntry> SortDescriptors(
        List<MetadataContextPackDescriptorEntry> entries)
    {
        return entries
            .OrderByDescending(d => d.IsFocus)
            .ThenBy(d => d.Ref.Namespace, StringComparer.Ordinal)
            .ThenBy(d => d.Ref.Id, StringComparer.Ordinal)
            .ThenBy(d => d.Ref.Version ?? -1)
            .ToList();
    }

    private static List<MetadataContextPackRelationshipEntry> SortRelationships(
        List<MetadataContextPackRelationshipEntry> entries)
    {
        return entries
            .OrderBy(r => r.From.Namespace, StringComparer.Ordinal)
            .ThenBy(r => r.From.Id, StringComparer.Ordinal)
            .ThenBy(r => r.From.Version ?? -1)
            .ThenBy(r => r.To.Namespace, StringComparer.Ordinal)
            .ThenBy(r => r.To.Id, StringComparer.Ordinal)
            .ThenBy(r => r.To.Version ?? -1)
            .ThenBy(r => r.Kind)
            .ToList();
    }

    private static List<MetadataContextPackDiagnostic> SortDiagnostics(
        List<MetadataContextPackDiagnostic> diagnostics)
    {
        return diagnostics
            .OrderByDescending(d => d.Severity)
            .ThenBy(d => d.Code, StringComparer.Ordinal)
            .ThenBy(d => d.Subject?.Namespace ?? "", StringComparer.Ordinal)
            .ThenBy(d => d.Subject?.Id ?? "", StringComparer.Ordinal)
            .ThenBy(d => d.Subject?.Version ?? -1)
            .ToList();
    }
}
