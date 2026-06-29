using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata.DescriptorImpact;

public sealed class DescriptorImpactAnalyzer : IDescriptorImpactAnalyzer
{
    // ── Advisory edge predicate (§4.4) ──
    internal static bool IsAdvisory(DescriptorEdge edge)
    {
        if (edge.IsRuntimeBinding) return false;
        return edge.Strength == RelationshipStrength.Weak
            && (edge.Kind == RelationshipKind.References
                || edge.Kind == RelationshipKind.DependsOn
                || edge.Role == RelationshipRoles.SupersededBy
                || edge.Role == RelationshipRoles.SubWorkflowStep);
    }

    // ── Base severity from table (§4.1) ──
    internal static DescriptorImpactSeverity BaseSeverity(
        DescriptorChangeKind changeKind,
        bool isStrongPath,
        bool isRuntimePath)
    {
        if (changeKind == DescriptorChangeKind.Removed)
            return isStrongPath
                ? (isRuntimePath ? DescriptorImpactSeverity.Critical : DescriptorImpactSeverity.High)
                : DescriptorImpactSeverity.Medium;

        if (changeKind == DescriptorChangeKind.Deprecated)
            return isStrongPath
                ? (isRuntimePath ? DescriptorImpactSeverity.High : DescriptorImpactSeverity.Medium)
                : DescriptorImpactSeverity.Low;

        if (changeKind is DescriptorChangeKind.Updated or DescriptorChangeKind.ContractHashChanged)
            return isStrongPath
                ? (isRuntimePath ? DescriptorImpactSeverity.High : DescriptorImpactSeverity.Medium)
                : DescriptorImpactSeverity.Low;

        if (changeKind == DescriptorChangeKind.StateChanged)
            return isStrongPath
                ? (isRuntimePath ? DescriptorImpactSeverity.Medium : DescriptorImpactSeverity.Low)
                : DescriptorImpactSeverity.Info;

        // Activated or Added
        return DescriptorImpactSeverity.Info;
    }

    // ── Attenuation (§4.3, Modifier 1) ──
    internal static DescriptorImpactSeverity Attenuate(DescriptorImpactSeverity severity)
    {
        return severity switch
        {
            DescriptorImpactSeverity.Critical => DescriptorImpactSeverity.High,
            DescriptorImpactSeverity.High => DescriptorImpactSeverity.Medium,
            DescriptorImpactSeverity.Medium => DescriptorImpactSeverity.Low,
            DescriptorImpactSeverity.Low => DescriptorImpactSeverity.Info,
            _ => severity
        };
    }

    // ── Runtime binding boost (§4.3, Modifier 2): per-terminal-segment, cap High ──
    internal static DescriptorImpactSeverity RuntimeBoost(DescriptorImpactSeverity severity)
    {
        return severity switch
        {
            DescriptorImpactSeverity.Critical => DescriptorImpactSeverity.High, // cap
            DescriptorImpactSeverity.High => DescriptorImpactSeverity.High,
            DescriptorImpactSeverity.Medium => DescriptorImpactSeverity.High,
            DescriptorImpactSeverity.Low => DescriptorImpactSeverity.Medium,
            DescriptorImpactSeverity.Info => DescriptorImpactSeverity.Low,
            _ => severity
        };
    }

    // ── Full severity pipeline ──
    internal static DescriptorImpactSeverity ComputePathSeverity(
        DescriptorChangeKind changeKind,
        DescriptorImpactPathSegment terminalSegment,
        int depth)
    {
        var isStrong = terminalSegment.Strength == RelationshipStrength.Strong;
        var isRuntime = terminalSegment.IsRuntimeBinding;
        var severity = BaseSeverity(changeKind, isStrong, isRuntime);
        if (depth >= 2) severity = Attenuate(severity);
        // Boost when terminal is runtime AND base was NOT from Strong Runtime column
        // (which already accounts for runtime binding)
        if (terminalSegment.IsRuntimeBinding && !(isStrong && isRuntime))
            severity = RuntimeBoost(severity);
        return severity;
    }

    // ── Runtime area from descriptor kind (§6.3) ──
    internal static DescriptorImpactRuntimeArea AreaFromKind(DescriptorKind kind) => kind switch
    {
        DescriptorKind.Schema => DescriptorImpactRuntimeArea.Schema,
        DescriptorKind.Form => DescriptorImpactRuntimeArea.Form,
        DescriptorKind.Capability => DescriptorImpactRuntimeArea.Capability,
        DescriptorKind.Event => DescriptorImpactRuntimeArea.Event,
        DescriptorKind.Workflow => DescriptorImpactRuntimeArea.Workflow,
        DescriptorKind.HumanTask => DescriptorImpactRuntimeArea.HumanTask,
        _ => DescriptorImpactRuntimeArea.Metadata
    };

    // ── BFS State (§5.4) ──
    private readonly struct BfsState(
        DescriptorNode currentNode,
        int depth,
        List<DescriptorImpactPathSegment> pathSoFar,
        bool hasRuntimeBindingAlongPath)
    {
        public DescriptorNode CurrentNode => currentNode;
        public int Depth => depth;
        public List<DescriptorImpactPathSegment> PathSoFar => pathSoFar;
        public bool HasRuntimeBindingAlongPath => hasRuntimeBindingAlongPath;
    }

    // ── Analyze (§5.3) ──
    public DescriptorImpactAnalysisReport Analyze(
        DescriptorTopologySnapshot topology,
        DescriptorChangeSet changeSet,
        DescriptorImpactAnalysisOptions? options = null)
    {
        var opts = options ?? new DescriptorImpactAnalysisOptions();

        // ── Build indices (§5.1) ──
        var exactIndex = topology.Nodes.ToDictionary(kv => kv.Key, kv => kv.Value);
        var identityIndex = topology.Nodes.Values
            .GroupBy(n => new DescriptorIdentity(n.Ref.Namespace, n.Ref.Id))
            .ToDictionary(g => g.Key,
                g => g.OrderBy(n => n.Ref.Namespace).ThenBy(n => n.Ref.Id)
                      .ThenBy(n => n.Ref.Version).ToList());

        var impactIncoming = new Dictionary<DescriptorRef, List<DescriptorEdge>>();
        foreach (var edge in topology.Edges)
        {
            if (edge.To.Version != null)
            {
                AddToIndex(impactIncoming, edge.To, edge);
            }
            else
            {
                var idKey = new DescriptorIdentity(edge.To.Namespace, edge.To.Id);
                if (identityIndex.TryGetValue(idKey, out var matching))
                {
                    foreach (var node in matching)
                        AddToIndex(impactIncoming, node.Ref, edge);
                }
            }
        }

        var diagnostics = new List<DescriptorImpactDiagnostic>();
        var allDiscovered = new Dictionary<DescriptorRef, List<(DescriptorImpactPath Path, bool HasRuntime)>>();

        // ── BFS loop over each change ──
        foreach (var change in changeSet.Changes)
        {
            var originNodes = ResolveRef(change.Ref, exactIndex, identityIndex);
            if (originNodes.Count == 0) continue;

            foreach (var origin in originNodes)
            {
                if (!impactIncoming.TryGetValue(origin.Ref, out var incomingEdges))
                    continue;

                var visited = new HashSet<(DescriptorRef OriginRef, DescriptorRef CurrentRef, int EdgeIndex)>();
                var queue = new Queue<BfsState>();

                foreach (var edge in incomingEdges)
                {
                    var key = (change.Ref, origin.Ref, edge.Index);
                    if (!visited.Add(key)) continue;

                    if (!opts.IncludeWeakRelationships && edge.Strength == RelationshipStrength.Weak) continue;
                    if (!opts.IncludeAdvisoryRelationships && IsAdvisory(edge))
                    {
                        diagnostics.Add(new DescriptorImpactDiagnostic(
                            SeverityLevel.Info,
                            new DiagnosticCode("IMPACT_SKIPPED_WEAK_PATH"),
                            $"Advisory edge skipped: {edge.From.Id} -> {edge.To.Id}",
                            edge.From, null));
                        continue;
                    }

                    if (opts.MaxDepth.HasValue && 1 >= opts.MaxDepth.Value)
                    {
                        var consumerNodes = ResolveRef(edge.From, exactIndex, identityIndex);
                        foreach (var cn in consumerNodes)
                        {
                            var seg = CreateSegment(edge);
                            var path = new DescriptorImpactPath
                            {
                                SourceChange = change.Ref,
                                Affected = cn.Ref,
                                Segments = new[] { seg }
                            };
                            RecordDiscovered(allDiscovered, cn.Ref, path, edge.IsRuntimeBinding);
                            diagnostics.Add(new DescriptorImpactDiagnostic(
                                SeverityLevel.Warning,
                                new DiagnosticCode("IMPACT_PATH_TRUNCATED"),
                                $"Impact path truncated at depth limit {opts.MaxDepth}: {cn.Ref.FullId}",
                                cn.Ref, null));
                        }
                        continue;
                    }

                    var segment = CreateSegment(edge);
                    queue.Enqueue(new BfsState(origin, 1,
                        new List<DescriptorImpactPathSegment> { segment },
                        edge.IsRuntimeBinding));
                }

                while (queue.Count > 0)
                {
                    var state = queue.Dequeue();
                    var lastSegment = state.PathSoFar[^1];
                    var consumerNodes = ResolveRef(lastSegment.From, exactIndex, identityIndex);

                    if (consumerNodes.Count == 0)
                    {
                        diagnostics.Add(new DescriptorImpactDiagnostic(
                            SeverityLevel.Warning,
                            new DiagnosticCode("IMPACT_UNRESOLVED_CONSUMER"),
                            $"Unresolved consumer: {lastSegment.From.FullId}",
                            lastSegment.From, null));
                        continue;
                    }

                    if (consumerNodes.Count > 1)
                    {
                        diagnostics.Add(new DescriptorImpactDiagnostic(
                            SeverityLevel.Warning,
                            new DiagnosticCode("IMPACT_AMBIGUOUS_UNPINNED_TARGET"),
                            $"Ambiguous unpinned consumer: {lastSegment.From.FullId} resolves to {consumerNodes.Count} versions",
                            lastSegment.From, null));
                    }

                    foreach (var consumerNode in consumerNodes)
                    {
                        var path = new DescriptorImpactPath
                        {
                            SourceChange = change.Ref,
                            Affected = consumerNode.Ref,
                            Segments = state.PathSoFar.ToArray()
                        };
                        RecordDiscovered(allDiscovered, consumerNode.Ref, path, state.HasRuntimeBindingAlongPath);

                        var nextDepth = state.Depth + 1;
                        if (opts.MaxDepth.HasValue && nextDepth > opts.MaxDepth.Value)
                        {
                            diagnostics.Add(new DescriptorImpactDiagnostic(
                                SeverityLevel.Warning,
                                new DiagnosticCode("IMPACT_PATH_TRUNCATED"),
                                $"Impact path truncated at depth {opts.MaxDepth}: {consumerNode.Ref.FullId}",
                                consumerNode.Ref, null));
                            continue;
                        }

                        if (!impactIncoming.TryGetValue(consumerNode.Ref, out var nextEdges))
                            continue;

                        foreach (var nextEdge in nextEdges)
                        {
                            if (!opts.IncludeWeakRelationships && nextEdge.Strength == RelationshipStrength.Weak) continue;
                            if (!opts.IncludeAdvisoryRelationships && IsAdvisory(nextEdge))
                            {
                                diagnostics.Add(new DescriptorImpactDiagnostic(
                                    SeverityLevel.Info,
                                    new DiagnosticCode("IMPACT_SKIPPED_WEAK_PATH"),
                                    $"Advisory edge skipped: {nextEdge.From.Id} -> {nextEdge.To.Id}",
                                    nextEdge.From, null));
                                continue;
                            }

                            var visitKey = (change.Ref, consumerNode.Ref, nextEdge.Index);
                            if (!visited.Add(visitKey)) continue;

                            var nextSegment = CreateSegment(nextEdge);
                            var newPath = new List<DescriptorImpactPathSegment>(state.PathSoFar) { nextSegment };
                            queue.Enqueue(new BfsState(consumerNode, nextDepth, newPath,
                                state.HasRuntimeBindingAlongPath || nextEdge.IsRuntimeBinding));
                        }
                    }
                }
            }
        }

        // ── Assembly (§5.10) ──
        var affectedDescriptors = new List<AffectedDescriptor>();
        var allPaths = new List<DescriptorImpactPath>();

        foreach (var (consumerRef, pathList) in allDiscovered)
        {
            if (!exactIndex.TryGetValue(consumerRef, out var node)) continue;

            var dedupedPaths = pathList.Select(p => p.Path).ToList();
            allPaths.AddRange(dedupedPaths);

            var maxSev = DescriptorImpactSeverity.None;
            DescriptorImpactPath? topPath = null;
            var hasRuntimeBindingAnyPath = false;

            foreach (var (path, hasRb) in pathList)
            {
                if (hasRb) hasRuntimeBindingAnyPath = true;

                var terminalSeg = path.Segments[^1];
                var originChange = changeSet.Changes.FirstOrDefault(c => c.Ref == path.SourceChange);
                if (originChange is null) continue;

                var sev = ComputePathSeverity(originChange.Kind, terminalSeg, path.Segments.Count);
                if (sev > maxSev)
                {
                    maxSev = sev;
                    topPath = path;
                }
            }

            var areas = new List<DescriptorImpactRuntimeArea> { AreaFromKind(node.Kind) };
            if (hasRuntimeBindingAnyPath)
                areas.Add(DescriptorImpactRuntimeArea.RuntimeBinding);

            var reason = topPath is not null
                ? $"{changeSet.Changes.First(c => c.Ref == topPath.SourceChange).Kind}: " +
                  $"{topPath.SourceChange.FullId} -> {consumerRef.FullId} via " +
                  $"{topPath.Segments[^1].Role ?? topPath.Segments[^1].Kind.ToString()}"
                : null;

            affectedDescriptors.Add(new AffectedDescriptor
            {
                Ref = consumerRef,
                Kind = node.Kind,
                Name = node.Name,
                Severity = maxSev,
                RuntimeAreas = areas,
                Paths = dedupedPaths,
                Reason = reason
            });
        }

        affectedDescriptors.Sort((a, b) =>
        {
            var cmp = b.Severity.CompareTo(a.Severity);
            return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });

        // ── Topology diagnostics (§5.11) ──
        foreach (var topoDiag in topology.Diagnostics.All)
        {
            var subjectOnPath = topoDiag.Subject is not null && allDiscovered.ContainsKey(topoDiag.Subject.Value);
            var relatedOnPath = topoDiag.RelatedRefs?.Any(r => allDiscovered.ContainsKey(r)) == true;
            if (subjectOnPath || relatedOnPath)
            {
                var codeStr = (string)topoDiag.Code;
                var code = codeStr switch
                {
                    "MISSING_TARGET" => new DiagnosticCode("IMPACT_TOPOLOGY_MISSING_TARGET"),
                    "STRONG_CYCLE" => new DiagnosticCode("IMPACT_TOPOLOGY_STRONG_CYCLE"),
                    "UNSUPPORTED_REFERENCE" => new DiagnosticCode("IMPACT_TOPOLOGY_UNSUPPORTED_REFERENCE"),
                    _ => ((DiagnosticCode?)null)
                };
                if (code is not null)
                {
                    diagnostics.Add(new DescriptorImpactDiagnostic(
                        topoDiag.Severity, code.Value, topoDiag.Message,
                        topoDiag.Subject, topoDiag.RelatedRefs));
                }
            }
        }

        diagnostics.Sort((a, b) => string.CompareOrdinal(b.Severity.RequireValue(), a.Severity.RequireValue()));

        var maxSeverity = affectedDescriptors.Count > 0
            ? affectedDescriptors.Max(a => a.Severity)
            : DescriptorImpactSeverity.None;

        return new DescriptorImpactAnalysisReport
        {
            ChangeSet = changeSet,
            AffectedDescriptors = affectedDescriptors,
            Paths = allPaths,
            MaxSeverity = maxSeverity,
            Diagnostics = diagnostics
        };
    }

    // ── Helpers ──

    private static void AddToIndex(Dictionary<DescriptorRef, List<DescriptorEdge>> index, DescriptorRef key, DescriptorEdge edge)
    {
        if (!index.TryGetValue(key, out var list))
        {
            list = new List<DescriptorEdge>();
            index[key] = list;
        }
        list.Add(edge);
    }

    private static List<DescriptorNode> ResolveRef(
        DescriptorRef r,
        Dictionary<DescriptorRef, DescriptorNode> exactIndex,
        Dictionary<DescriptorIdentity, List<DescriptorNode>> identityIndex)
    {
        if (exactIndex.TryGetValue(r, out var node))
            return new List<DescriptorNode> { node };

        if (r.Version == null)
        {
            var idKey = new DescriptorIdentity(r.Namespace, r.Id);
            if (identityIndex.TryGetValue(idKey, out var matching))
                return matching;
        }

        return new List<DescriptorNode>();
    }

    private static DescriptorImpactPathSegment CreateSegment(DescriptorEdge edge) =>
        new()
        {
            From = edge.From,
            To = edge.To,
            Kind = edge.Kind,
            Strength = edge.Strength,
            IsRuntimeBinding = edge.IsRuntimeBinding,
            Role = edge.Role,
            SourcePath = edge.SourcePath
        };

    private static void RecordDiscovered(
        Dictionary<DescriptorRef, List<(DescriptorImpactPath Path, bool HasRuntime)>> allDiscovered,
        DescriptorRef consumerRef,
        DescriptorImpactPath path,
        bool hasRuntime)
    {
        if (!allDiscovered.TryGetValue(consumerRef, out var list))
        {
            list = new List<(DescriptorImpactPath, bool)>();
            allDiscovered[consumerRef] = list;
        }
        list.Add((path, hasRuntime));
    }
}
