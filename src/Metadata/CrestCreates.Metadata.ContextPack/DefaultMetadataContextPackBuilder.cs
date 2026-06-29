using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
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

        // 3. Create descriptor source (centralizes topology + inventory lookup)
        var source = new MetadataContextDescriptorSource(topology, descriptors);

        // 4. Resolve focus nodes with asymmetric mismatch policy
        var focusRefs = snapshotRequest.FocusDescriptors;
        var foundFocusRefs = new List<DescriptorRef>();

        foreach (var focusRef in focusRefs)
        {
            var resolved = source.Resolve(focusRef);

            // Ambiguity check takes priority over all other resolution states
            if (resolved.IsAmbiguousUnpinned)
            {
                diagnostics.Add(new MetadataContextPackDiagnostic
                {
                    Severity = SeverityLevel.Warning,
                    Code = MetadataContextPackDiagnosticCodes.AmbiguousDescriptorRef,
                    Message = $"Focus descriptor ref '{focusRef.FullId}' matches multiple versions. Specify an exact version.",
                    Subject = focusRef
                });
                continue;
            }

            if (resolved.TopologyNode is null && resolved.Descriptor is null)
            {
                // Neither topology nor inventory — not found
                diagnostics.Add(new MetadataContextPackDiagnostic
                {
                    Severity = SeverityLevel.Warning,
                    Code = MetadataContextPackDiagnosticCodes.FocusNotFound,
                    Message = $"Focus descriptor '{focusRef.FullId}' not found in topology or descriptor inventory.",
                    Subject = focusRef
                });
                continue;
            }

            if (resolved.TopologyNode is not null && resolved.Descriptor is null)
            {
                // Topology has the node but inventory has no descriptor
                diagnostics.Add(new MetadataContextPackDiagnostic
                {
                    Severity = SeverityLevel.Error,
                    Code = MetadataContextPackDiagnosticCodes.DescriptorMissingForTopologyRef,
                    Message = $"Topology references descriptor '{focusRef.FullId}' but it is absent from descriptor inventory.",
                    Subject = focusRef
                });
                continue;
            }

            if (resolved.TopologyNode is null && resolved.Descriptor is not null)
            {
                // Inventory-only — include if focused, no traversal possible
                diagnostics.Add(new MetadataContextPackDiagnostic
                {
                    Severity = SeverityLevel.Warning,
                    Code = MetadataContextPackDiagnosticCodes.TopologyNodeMissingForDescriptor,
                    Message = $"Descriptor '{focusRef.FullId}' exists in inventory but has no topology node.",
                    Subject = focusRef
                });
                foundFocusRefs.Add(focusRef);
                continue;
            }

            // Fully resolved — keep topology ref for traversal, canonicalize at output time
            foundFocusRefs.Add(resolved.TopologyNode!.Ref);
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
                ResolveDirectDependencies(foundFocusRefs, source, includedRefs, includedEdges);
                traversalDepthReached = 1;
                break;

            case MetadataContextPackScope.DirectDependents:
                ResolveDirectDependents(foundFocusRefs, source, includedRefs, includedEdges);
                traversalDepthReached = 1;
                break;

            case MetadataContextPackScope.ImpactRadius:
                traversalDepthReached = ResolveImpactRadius(foundFocusRefs, source, snapshotRequest.MaxTraversalDepth, includedRefs, includedEdges, diagnostics);
                break;

            case MetadataContextPackScope.RuntimeScenario:
                traversalDepthReached = ResolveRuntimeScenario(foundFocusRefs, source, snapshotRequest, includedRefs, includedEdges);
                break;
        }

        // 6. Apply kind filters (non-focus only)
        var focusSet = new HashSet<DescriptorRef>(foundFocusRefs);
        ApplyKindFilters(includedRefs, focusSet, snapshotRequest, source, diagnostics);

        // 7. Apply count bounds (non-focus only)
        ApplyCountBounds(includedRefs, focusSet, snapshotRequest.MaxDescriptorCount, diagnostics);

        // 8. Build descriptor entries (only when resolved.Descriptor is not null)
        var descriptorEntries = BuildDescriptorEntries(includedRefs, focusSet, source, snapshotRequest, diagnostics);

        // 9. Collect relationship edges (with pack closure invariant against actual descriptor entries)
        var descriptorPresentRefs = new HashSet<DescriptorRef>(descriptorEntries.Select(e => e.Ref));
        var relationshipEntries = CollectRelationshipEntries(descriptorPresentRefs, includedEdges, source);

        // 10. Build summary — canonicalize focus refs to match descriptor entry refs
        var canonicalFocusRefs = foundFocusRefs
            .Select(r => source.Resolve(r).CanonicalRef)
            .ToList();
        var summary = BuildSummary(descriptorEntries, relationshipEntries, canonicalFocusRefs, diagnostics, traversalDepthReached);

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
                Severity = SeverityLevel.Error,
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

    private static void ResolveDirectDependencies(
        List<DescriptorRef> focusRefs, MetadataContextDescriptorSource source,
        HashSet<DescriptorRef> includedRefs, List<DescriptorEdge> includedEdges)
    {
        foreach (var focusRef in focusRefs)
        {
            includedRefs.Add(focusRef);

            foreach (var visit in source.GetDirectedEdges(focusRef, ScenarioTraversalDirection.Dependencies))
            {
                includedEdges.Add(visit.Edge);
                includedRefs.Add(visit.Target);
            }
        }
    }

    private static void ResolveDirectDependents(
        List<DescriptorRef> focusRefs, MetadataContextDescriptorSource source,
        HashSet<DescriptorRef> includedRefs, List<DescriptorEdge> includedEdges)
    {
        foreach (var focusRef in focusRefs)
        {
            includedRefs.Add(focusRef);

            foreach (var visit in source.GetDirectedEdges(focusRef, ScenarioTraversalDirection.Dependents))
            {
                includedEdges.Add(visit.Edge);
                includedRefs.Add(visit.Target);
            }
        }
    }

    private static int ResolveImpactRadius(
        List<DescriptorRef> focusRefs, MetadataContextDescriptorSource source, int maxDepth,
        HashSet<DescriptorRef> includedRefs, List<DescriptorEdge> includedEdges,
        List<MetadataContextPackDiagnostic> diagnostics)
    {
        var visited = new HashSet<DescriptorRef>();
        var frontier = new List<DescriptorRef>();

        // Depth 0: focus nodes
        foreach (var r in focusRefs)
        {
            if (visited.Add(r))
            {
                includedRefs.Add(r);
                frontier.Add(r);
            }
        }

        var depthReached = 0;

        for (int depth = 1; depth <= maxDepth; depth++)
        {
            var nextFrontier = new List<DescriptorRef>();
            foreach (var currentRef in frontier)
            {
                foreach (var visit in source.GetDirectedEdges(currentRef, ScenarioTraversalDirection.Both))
                {
                    includedEdges.Add(visit.Edge);
                    if (visited.Add(visit.Target))
                    {
                        includedRefs.Add(visit.Target);
                        nextFrontier.Add(visit.Target);
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
            foreach (var visit in source.GetDirectedEdges(frontierRef, ScenarioTraversalDirection.Both))
            {
                if (!visited.Contains(visit.Target))
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
                Severity = SeverityLevel.Info,
                Code = MetadataContextPackDiagnosticCodes.TruncatedByDepth,
                Message = $"Traversal truncated at depth {maxDepth}. Additional nodes exist beyond this depth."
            });
        }

        return depthReached;
    }

    private static int ResolveRuntimeScenario(
        List<DescriptorRef> focusRefs, MetadataContextDescriptorSource source,
        MetadataContextPackRequest request,
        HashSet<DescriptorRef> includedRefs, List<DescriptorEdge> includedEdges)
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
            var discoveredThisStep = new HashSet<DescriptorRef>();

            for (int depth = 1; depth <= step.MaxDepth; depth++)
            {
                var nextBoundary = new HashSet<DescriptorRef>();

                foreach (var currentRef in boundary)
                {
                    foreach (var visit in source.GetDirectedEdges(currentRef, step.Direction))
                    {
                        if (visit.Edge.Kind != step.FollowKind) continue;
                        if (step.Role is not null && visit.Edge.Role != step.Role) continue;

                        var targetNode = source.Resolve(visit.Target).TopologyNode;
                        if (targetNode is null) continue;

                        if (step.TargetKind.HasValue && targetNode.Kind != step.TargetKind.Value) continue;

                        includedEdges.Add(visit.Edge);

                        if (stepVisited.Add(visit.Target))
                        {
                            includedRefs.Add(visit.Target);
                            nextBoundary.Add(visit.Target);
                            discoveredThisStep.Add(visit.Target);
                        }
                    }
                }

                boundary = nextBoundary;
                if (nextBoundary.Count > 0) maxDepthReached = depth;
            }

            // Boundary for next step = only nodes discovered in this step, not the starting boundary
            boundary = discoveredThisStep;
        }

        return maxDepthReached;
    }

    private static void ApplyKindFilters(
        HashSet<DescriptorRef> includedRefs, HashSet<DescriptorRef> focusSet,
        MetadataContextPackRequest request, MetadataContextDescriptorSource source,
        List<MetadataContextPackDiagnostic> diagnostics)
    {
        // Check if any focus descriptor would be filtered out
        foreach (var focusRef in focusSet)
        {
            var resolved = source.Resolve(focusRef);
            var kind = resolved.Descriptor?.Kind ?? resolved.TopologyNode?.Kind;
            if (kind is null) continue;

            var wouldBeExcluded = false;

            if (request.IncludeKinds is not null && !request.IncludeKinds.Contains(kind.Value))
                wouldBeExcluded = true;
            if (request.ExcludeKinds is not null && request.ExcludeKinds.Contains(kind.Value))
                wouldBeExcluded = true;

            if (wouldBeExcluded)
            {
                diagnostics.Add(new MetadataContextPackDiagnostic
                {
                    Severity = SeverityLevel.Warning,
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

            var resolved = source.Resolve(ref_);
            var kind = resolved.Descriptor?.Kind ?? resolved.TopologyNode?.Kind;
            if (kind is null) continue;

            if (request.IncludeKinds is not null && !request.IncludeKinds.Contains(kind.Value))
            {
                toRemove.Add(ref_);
                continue;
            }
            if (request.ExcludeKinds is not null && request.ExcludeKinds.Contains(kind.Value))
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
                Severity = SeverityLevel.Info,
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
            Severity = SeverityLevel.Info,
            Code = MetadataContextPackDiagnosticCodes.TruncatedByCount,
            Message = $"Result truncated to {maxDescriptorCount} descriptors.",
            Path = $"MaxDescriptorCount={maxDescriptorCount}"
        });
    }

    private static List<MetadataContextPackRelationshipEntry> CollectRelationshipEntries(
        HashSet<DescriptorRef> includedRefs, List<DescriptorEdge> includedEdges,
        MetadataContextDescriptorSource source)
    {
        var entries = new List<MetadataContextPackRelationshipEntry>();
        var seen = new HashSet<(DescriptorRef, DescriptorRef, RelationshipKind)>();

        foreach (var edge in includedEdges)
        {
            // Pack closure invariant: every relationship endpoint must exist in the descriptor set
            // includedRefs contains canonical refs from descriptor entries
            var fromResolved = source.Resolve(edge.From);
            var toResolved = source.Resolve(edge.To);

            var fromCanonical = fromResolved.CanonicalRef;
            var toCanonical = toResolved.CanonicalRef;

            if (!includedRefs.Contains(fromCanonical) || !includedRefs.Contains(toCanonical)) continue;

            var key = (fromCanonical, toCanonical, edge.Kind);
            if (!seen.Add(key)) continue;

            entries.Add(new MetadataContextPackRelationshipEntry
            {
                From = fromCanonical,
                To = toCanonical,
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
        MetadataContextDescriptorSource source,
        MetadataContextPackRequest request,
        List<MetadataContextPackDiagnostic> diagnostics)
    {
        var entries = new List<MetadataContextPackDescriptorEntry>();

        foreach (var ref_ in includedRefs)
        {
            var resolved = source.Resolve(ref_);

            // DescriptorEntry is built only when resolved.Descriptor is not null.
            // TopologyNode must not be used to fabricate descriptor entries.
            if (resolved.Descriptor is null)
            {
                if (!focusSet.Contains(ref_))
                {
                    // Ambiguity check takes priority
                    if (resolved.IsAmbiguousUnpinned)
                    {
                        diagnostics.Add(new MetadataContextPackDiagnostic
                        {
                            Severity = SeverityLevel.Warning,
                            Code = MetadataContextPackDiagnosticCodes.AmbiguousDescriptorRef,
                            Message = $"Descriptor ref '{ref_.FullId}' matches multiple versions. Specify an exact version.",
                            Subject = ref_
                        });
                    }
                    else
                    {
                        diagnostics.Add(new MetadataContextPackDiagnostic
                        {
                            Severity = SeverityLevel.Error,
                            Code = MetadataContextPackDiagnosticCodes.DescriptorMissingForTopologyRef,
                            Message = $"Topology references descriptor '{ref_.FullId}' but it is absent from descriptor inventory.",
                            Subject = ref_
                        });
                    }
                }
                continue;
            }

            var descriptor = resolved.Descriptor;

            DescriptorStableHashes? hashes = null;
            if (request.IncludeStableHashes)
            {
                if (_hashBuilder is not null)
                {
                    hashes = _hashBuilder.Build(descriptor);
                }
                else
                {
                    // Only emit once
                    if (!diagnostics.Any(d => d.Code == MetadataContextPackDiagnosticCodes.HashBuilderMissing))
                    {
                        diagnostics.Add(new MetadataContextPackDiagnostic
                        {
                            Severity = SeverityLevel.Warning,
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
                    State = descriptor.State,
                    RequiresReview = descriptor.State == DescriptorState.Draft
                };
            }

            entries.Add(new MetadataContextPackDescriptorEntry
            {
                Ref = resolved.CanonicalRef,
                Kind = descriptor.Kind,
                Name = descriptor.Name,
                State = descriptor.State,
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
            .ThenBy(d => d.Code.Value ?? "", StringComparer.Ordinal)
            .ThenBy(d => d.Subject?.Namespace ?? "", StringComparer.Ordinal)
            .ThenBy(d => d.Subject?.Id ?? "", StringComparer.Ordinal)
            .ThenBy(d => d.Subject?.Version ?? -1)
            .ToList();
    }
}
