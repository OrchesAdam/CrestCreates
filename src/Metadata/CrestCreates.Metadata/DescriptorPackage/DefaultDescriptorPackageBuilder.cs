using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using Package = CrestCreates.Metadata.Abstractions.DescriptorPackage.DescriptorPackage;

namespace CrestCreates.Metadata.DescriptorPackage;

public sealed class DefaultDescriptorPackageBuilder : IDescriptorPackageBuilder
{
    private readonly IDescriptorStableHashBuilder _hashBuilder;
    private readonly IDescriptorPackageCanonicalHashComputer? _packageHashComputer;

    public DefaultDescriptorPackageBuilder(
        IDescriptorStableHashBuilder hashBuilder,
        IDescriptorPackageCanonicalHashComputer? packageHashComputer = null)
    {
        _hashBuilder = hashBuilder;
        _packageHashComputer = packageHashComputer;
    }

    public Package Build(DescriptorPackageBuildRequest request)
    {
        var createdAt = request.CreatedAt ?? DateTimeOffset.UtcNow;

        // Build manifest entries from descriptors (sorted)
        var entries = BuildManifestEntries(request.Descriptors);

        // Build evidence from supplied reports
        var evidence = BuildEvidence(request);

        // Build relationship entries from topology
        var relationships = BuildRelationshipEntries(request.TopologySnapshot);

        // Build manifest structure first (without hashes) — needed as input to canonical hash
        var manifest = new DescriptorManifest
        {
            FormatVersion = request.Options.FormatVersion,
            PackageId = request.PackageId,
            PackageVersion = request.PackageVersion,
            Name = request.Name,
            CreatedAt = createdAt,
            CreatedBy = request.CreatedBy,
            Source = request.Source,
            DescriptorCount = entries.Count,
            DescriptorEntries = entries
        };

        // Compute atomic hash set through the canonical hash computer
        var envelopeMetadata = new DescriptorPackageEvidenceEnvelopeMetadata
        {
            PackageId = request.PackageId,
            PackageVersion = request.PackageVersion,
            CreatedAt = createdAt,
            CreatedBy = request.CreatedBy,
            Source = request.Source
        };

        if (_packageHashComputer == null)
            throw new InvalidOperationException(
                "IDescriptorPackageCanonicalHashComputer is required. " +
                "Call AddDescriptorPackaging() during DI registration.");

        var hashSet = _packageHashComputer.ComputeHashSet(manifest, evidence, envelopeMetadata);

        // Build evidence envelope
        var evidenceEnvelope = new DescriptorPackageEvidenceEnvelope
        {
            PackageId = request.PackageId,
            PackageVersion = request.PackageVersion,
            CreatedAt = createdAt,
            CreatedBy = request.CreatedBy,
            Source = request.Source,
            PackageManifestHash = hashSet.PackageManifestHash,
            PackageEvidenceHash = hashSet.PackageEvidenceHash
        };

        // Build snapshot (uses package manifest hash for deterministic SnapshotId)
        var snapshotEntries = entries.Select(e => new SnapshotEntry
        {
            Ref = e.Ref,
            DescriptorName = e.Name,
            Kind = e.Kind,
            State = e.State,
            ContractHash = e.ContractHash,
            DefinitionHash = e.DefinitionHash,
            SupersededById = e.SupersededById
        }).ToList();

        var snapshot = new DescriptorSnapshot
        {
            SnapshotId = $"snapshot_{hashSet.PackageManifestHash.Value[..16]}",
            PackageId = request.PackageId,
            PackageVersion = request.PackageVersion,
            CreatedAt = createdAt,
            Descriptors = snapshotEntries,
            Relationships = relationships
        };

        // Run self-consistency diagnostics
        var diagnostics = RunDiagnostics(request, entries, evidence, hashSet.PackageManifestHash.Value);

        return new Package
        {
            Manifest = manifest,
            Snapshot = snapshot,
            Evidence = evidence,
            Diagnostics = diagnostics,
            Hashes = hashSet,
            EvidenceEnvelope = evidenceEnvelope
        };
    }

    private IReadOnlyList<DescriptorManifestEntry> BuildManifestEntries(
        IReadOnlyList<IDescriptor> descriptors)
    {
        return descriptors
            .Select(d =>
            {
                var hashes = _hashBuilder.Build(d);
                return new DescriptorManifestEntry
                {
                    Ref = new DescriptorRef(d.Namespace, d.Id,
                        (d as IVersionedDescriptor)?.Version),
                    Kind = d.Kind,
                    Name = d.Name,
                    State = d.State,
                    ContractHash = hashes.ContractHash.Value,
                    DefinitionHash = hashes.DefinitionHash.Value,
                    SupersededById = d.SupersededById
                };
            })
            .OrderBy(e => e.Ref.Namespace)
            .ThenBy(e => e.Ref.Id)
            .ThenBy(e => e.Ref.Version ?? 0)
            .ThenBy(e => e.Kind)
            .ThenBy(e => e.Name)
            .ToList();
    }

    private static DescriptorPackageEvidence BuildEvidence(
        DescriptorPackageBuildRequest request)
    {
        int topologyNodeCount = 0;
        int topologyEdgeCount = 0;
        bool hasTopologyErrors = false;
        var topologyDiagnosticCounts = new List<EvidenceFindingCount>();
        DescriptorImpactSeverity maxImpactSeverity = DescriptorImpactSeverity.None;
        int affectedDescriptorCount = 0;
        int impactPathCount = 0;
        var impactDiagnosticCounts = new List<EvidenceFindingCount>();
        DescriptorCompatibilityLevel maxCompatibilityLevel = DescriptorCompatibilityLevel.Compatible;
        int breakingFindingCount = 0;
        int securitySensitiveFindingCount = 0;
        int unsupportedFindingCount = 0;
        DescriptorLifecycleDecisionKind maxLifecycleDecision = DescriptorLifecycleDecisionKind.Allowed;
        bool requiresReview = false;
        bool isBlocked = false;
        int packageFindingCount = 0;
        var normalizedFindings = new List<EvidenceFinding>();

        if (request.TopologySnapshot != null)
        {
            topologyNodeCount = request.TopologySnapshot.NodeCount;
            topologyEdgeCount = request.TopologySnapshot.EdgeCount;
            hasTopologyErrors = request.TopologySnapshot.Diagnostics.All
                .Any(d => d.Severity == DiagnosticSeverity.Error);
            topologyDiagnosticCounts = request.TopologySnapshot.Diagnostics.All
                .GroupBy(d => new { d.Severity, d.Code })
                .Select(g => new EvidenceFindingCount
                {
                    Severity = g.Key.Severity.ToString(),
                    Code = g.Key.Code,
                    Count = g.Count()
                })
                .ToList();

            foreach (var d in request.TopologySnapshot.Diagnostics.All)
            {
                normalizedFindings.Add(new EvidenceFinding
                {
                    Source = "topology",
                    Code = d.Code,
                    Severity = d.Severity.ToString(),
                    Message = d.Message
                });
            }
        }

        if (request.ImpactReport != null)
        {
            maxImpactSeverity = request.ImpactReport.MaxSeverity;
            affectedDescriptorCount = request.ImpactReport.AffectedDescriptors.Count;
            impactPathCount = request.ImpactReport.Paths.Count;
            impactDiagnosticCounts = request.ImpactReport.Diagnostics
                .GroupBy(d => new { d.Severity, d.Code })
                .Select(g => new EvidenceFindingCount
                {
                    Severity = g.Key.Severity.ToString(),
                    Code = g.Key.Code,
                    Count = g.Count()
                })
                .ToList();

            foreach (var d in request.ImpactReport.Diagnostics)
            {
                normalizedFindings.Add(new EvidenceFinding
                {
                    Source = "impact",
                    Code = d.Code,
                    Severity = d.Severity.ToString(),
                    Subject = d.Subject,
                    Message = d.Message,
                    RelatedRefs = d.RelatedRefs ?? Array.Empty<DescriptorRef>()
                });
            }
        }

        if (request.CompatibilityReport != null)
        {
            maxCompatibilityLevel = request.CompatibilityReport.MaxLevel;
            breakingFindingCount = request.CompatibilityReport.Findings
                .Count(f => f.Level == DescriptorCompatibilityLevel.Breaking);
            securitySensitiveFindingCount = request.CompatibilityReport.Findings
                .Count(f => f.Level == DescriptorCompatibilityLevel.SecuritySensitive);
            unsupportedFindingCount = request.CompatibilityReport.Findings
                .Count(f => f.Level == DescriptorCompatibilityLevel.Unsupported);

            foreach (var f in request.CompatibilityReport.Findings)
            {
                normalizedFindings.Add(new EvidenceFinding
                {
                    Source = "compatibility",
                    Code = f.RuleId,
                    Severity = f.Level.ToString(),
                    Subject = f.Subject,
                    Message = f.Message
                });
            }
        }

        if (request.GovernanceReport != null)
        {
            maxLifecycleDecision = request.GovernanceReport.MaxDecision;
            requiresReview = request.GovernanceReport.RequiresReview;
            isBlocked = request.GovernanceReport.IsBlocked;
            packageFindingCount = request.GovernanceReport.PackageFindings.Count;

            foreach (var f in request.GovernanceReport.PackageFindings)
            {
                normalizedFindings.Add(new EvidenceFinding
                {
                    Source = "lifecycle",
                    Code = f.Code,
                    Severity = f.Severity.ToString(),
                    Subject = f.Subject,
                    Message = f.Message,
                    RelatedRefs = f.RelatedRefs
                });
            }
        }

        return new DescriptorPackageEvidence
        {
            TopologyNodeCount = topologyNodeCount,
            TopologyEdgeCount = topologyEdgeCount,
            HasTopologyErrors = hasTopologyErrors,
            TopologyDiagnosticCounts = topologyDiagnosticCounts,
            MaxImpactSeverity = maxImpactSeverity,
            AffectedDescriptorCount = affectedDescriptorCount,
            ImpactPathCount = impactPathCount,
            ImpactDiagnosticCounts = impactDiagnosticCounts,
            MaxCompatibilityLevel = maxCompatibilityLevel,
            BreakingFindingCount = breakingFindingCount,
            SecuritySensitiveFindingCount = securitySensitiveFindingCount,
            UnsupportedFindingCount = unsupportedFindingCount,
            MaxLifecycleDecision = maxLifecycleDecision,
            RequiresReview = requiresReview,
            IsBlocked = isBlocked,
            PackageFindingCount = packageFindingCount,
            NormalizedFindings = normalizedFindings
        };
    }

    private static IReadOnlyList<DescriptorPackageRelationshipEntry> BuildRelationshipEntries(
        DescriptorTopologySnapshot? topology)
    {
        if (topology == null)
            return Array.Empty<DescriptorPackageRelationshipEntry>();
        return topology.Edges
            .Select(e => new DescriptorPackageRelationshipEntry
            {
                From = e.From, To = e.To, Kind = e.Kind,
                Role = e.Role, SourcePath = e.SourcePath,
                Strength = e.Strength, IsRuntimeBinding = e.IsRuntimeBinding
            })
            .ToList();
    }

    private static IReadOnlyList<DescriptorPackageDiagnostic> RunDiagnostics(
        DescriptorPackageBuildRequest request,
        IReadOnlyList<DescriptorManifestEntry> entries,
        DescriptorPackageEvidence evidence,
        string contentHash)
    {
        var diagnostics = new List<DescriptorPackageDiagnostic>();

        // Check for duplicate descriptor refs
        var seenRefs = new HashSet<DescriptorRef>();
        foreach (var entry in entries)
        {
            if (!seenRefs.Add(entry.Ref))
            {
                diagnostics.Add(new DescriptorPackageDiagnostic
                {
                    Code = DescriptorPackageDiagnosticCodes.DuplicateDescriptorRef,
                    Severity = DescriptorPackageDiagnosticCodes.SeverityError,
                    Message = $"Duplicate descriptor ref: {entry.Ref.Namespace}.{entry.Ref.Id} v{entry.Ref.Version}",
                    Subject = entry.Ref
                });
            }
        }

        // Check topology edges reference package descriptors
        var packageRefs = new HashSet<DescriptorRef>(entries.Select(e => e.Ref));
        if (request.TopologySnapshot != null)
        {
            foreach (var edge in request.TopologySnapshot.Edges)
            {
                if (!packageRefs.Contains(edge.From))
                {
                    diagnostics.Add(new DescriptorPackageDiagnostic
                    {
                        Code = DescriptorPackageDiagnosticCodes.TopologyEdgeOutsidePackage,
                        Severity = DescriptorPackageDiagnosticCodes.SeverityWarning,
                        Message = $"Topology edge 'From' ref outside package: {edge.From.FullId}",
                        Subject = edge.From
                    });
                }
                if (!packageRefs.Contains(edge.To))
                {
                    diagnostics.Add(new DescriptorPackageDiagnostic
                    {
                        Code = DescriptorPackageDiagnosticCodes.TopologyEdgeOutsidePackage,
                        Severity = DescriptorPackageDiagnosticCodes.SeverityWarning,
                        Message = $"Topology edge 'To' ref outside package: {edge.To.FullId}",
                        Subject = edge.To
                    });
                }
            }
        }
        else
        {
            diagnostics.Add(new DescriptorPackageDiagnostic
            {
                Code = DescriptorPackageDiagnosticCodes.TopologyNotProvided,
                Severity = DescriptorPackageDiagnosticCodes.SeverityInfo,
                Message = "No topology snapshot provided; package has no relationship facts."
            });
        }

        if (request.ImpactReport?.ChangeSet != null)
        {
            foreach (var change in request.ImpactReport.ChangeSet.Changes)
            {
                if (!packageRefs.Contains(change.Ref))
                {
                    diagnostics.Add(new DescriptorPackageDiagnostic
                    {
                        Code = DescriptorPackageDiagnosticCodes.ImpactChangeOutsidePackage,
                        Severity = DescriptorPackageDiagnosticCodes.SeverityWarning,
                        Message = $"Impact change ref outside package: {change.Ref.FullId}",
                        Subject = change.Ref
                    });
                }
            }
        }

        if (request.CompatibilityReport != null)
        {
            foreach (var finding in request.CompatibilityReport.Findings)
            {
                if (!packageRefs.Contains(finding.Subject))
                {
                    diagnostics.Add(new DescriptorPackageDiagnostic
                    {
                        Code = DescriptorPackageDiagnosticCodes.CompatibilitySubjectOutsidePackage,
                        Severity = DescriptorPackageDiagnosticCodes.SeverityWarning,
                        Message = $"Compatibility finding subject outside package: {finding.Subject.FullId}",
                        Subject = finding.Subject
                    });
                }
            }
        }

        if (request.GovernanceReport != null)
        {
            foreach (var decision in request.GovernanceReport.Decisions)
            {
                if (!packageRefs.Contains(decision.Transition.Subject))
                {
                    diagnostics.Add(new DescriptorPackageDiagnostic
                    {
                        Code = DescriptorPackageDiagnosticCodes.LifecycleTransitionOutsideInventory,
                        Severity = DescriptorPackageDiagnosticCodes.SeverityWarning,
                        Message = $"Lifecycle transition subject outside package: {decision.Transition.Subject.FullId}",
                        Subject = decision.Transition.Subject
                    });
                }
            }
        }

        foreach (var finding in evidence.NormalizedFindings)
        {
            if (finding.Subject != null && !packageRefs.Contains(finding.Subject.Value))
            {
                diagnostics.Add(new DescriptorPackageDiagnostic
                {
                    Code = DescriptorPackageDiagnosticCodes.EvidenceSubjectOutsideInventory,
                    Severity = DescriptorPackageDiagnosticCodes.SeverityWarning,
                    Message = $"Evidence finding subject outside package: {finding.Subject.Value.FullId}",
                    Subject = finding.Subject
                });
            }
        }

        return diagnostics;
    }
}
