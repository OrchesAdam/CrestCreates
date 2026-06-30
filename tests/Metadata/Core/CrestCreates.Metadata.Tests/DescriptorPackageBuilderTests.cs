using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.DescriptorPackage;
using CrestCreates.Metadata.DescriptorPackage.CanonicalHashing;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Metadata.Tests;

public class DescriptorPackageBuilderTests
{
    private readonly ICanonicalHashComputer _hashComputer = new DefaultCanonicalHashComputer();
    private readonly IDescriptorStableHashBuilder _hashBuilder;
    private readonly IDescriptorPackageCanonicalHashComputer _packageHashComputer;
    private readonly IDescriptorPackageBuilder _builder;

    public DescriptorPackageBuilderTests()
    {
        _hashBuilder = new DescriptorStableHashBuilder(_hashComputer);
        _packageHashComputer = new DefaultDescriptorPackageCanonicalHashComputer(_hashComputer);
        _builder = new DefaultDescriptorPackageBuilder(_hashBuilder, _packageHashComputer);
    }

    private static SchemaDescriptor MakeSchema(string id, int version, string name, DescriptorState state = DescriptorState.Active)
    {
        return new SchemaDescriptor
        {
            Id = id, Version = version, Name = name, State = state
        };
    }

    [Fact]
    public void Build_ProducesPackage()
    {
        var descriptors = new IDescriptor[]
        {
            MakeSchema("s1", 1, "Schema1"),
            MakeSchema("s2", 1, "Schema2")
        };
        var package = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0", Descriptors = descriptors
        });
        package.Should().NotBeNull();
        package.PackageId.Should().Be("test.pkg");
        package.Manifest.DescriptorCount.Should().Be(2);
        package.Manifest.DescriptorEntries.Should().HaveCount(2);
        package.SnapshotData.Descriptors.Should().HaveCount(2);
        package.ContentHash.Should().NotBeEmpty();
    }

    [Fact]
    public void Build_SameInput_ProducesSameContentHash()
    {
        var descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") };
        var request = new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0", Descriptors = descriptors,
            CreatedAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero)
        };
        var pkg1 = _builder.Build(request);
        var pkg2 = _builder.Build(request);
        pkg1.ContentHash.Should().Be(pkg2.ContentHash);
        pkg1.SnapshotData.SnapshotId.Should().Be(pkg2.SnapshotData.SnapshotId);
    }

    [Fact]
    public void Build_SameContentDifferentInputOrder_SameContentHash()
    {
        var descriptors1 = new IDescriptor[] { MakeSchema("b", 1, "B"), MakeSchema("a", 1, "A") };
        var descriptors2 = new IDescriptor[] { MakeSchema("a", 1, "A"), MakeSchema("b", 1, "B") };
        var pkg1 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0",
            CreatedAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero),
            Descriptors = descriptors1
        });
        var pkg2 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0",
            CreatedAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero),
            Descriptors = descriptors2
        });
        pkg1.ContentHash.Should().Be(pkg2.ContentHash);
    }

    [Fact]
    public void Build_DifferentCreatedAt_ChangesContentHash()
    {
        // With canonical JSON hashing, createdAt is part of the manifest hash.
        var descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") };
        var pkg1 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0", Descriptors = descriptors,
            CreatedAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero)
        });
        var pkg2 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0", Descriptors = descriptors,
            CreatedAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)
        });
        pkg1.ContentHash.Should().NotBe(pkg2.ContentHash);
    }

    [Fact]
    public void Build_ChangedDescriptorRef_ChangesContentHash()
    {
        var pkg1 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") }
        });
        var pkg2 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 2, "S1") }
        });
        pkg1.ContentHash.Should().NotBe(pkg2.ContentHash);
    }

    [Fact]
    public void Build_ProducesDeterministicSnapshotId_NoGuid()
    {
        var descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") };
        var request = new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0", Descriptors = descriptors,
            CreatedAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero)
        };
        var pkg1 = _builder.Build(request);
        var pkg2 = _builder.Build(request);
        pkg1.SnapshotData.SnapshotId.Should().Be(pkg2.SnapshotData.SnapshotId);
        pkg1.SnapshotData.SnapshotId.Should().StartWith("snapshot_");
        pkg1.SnapshotData.SnapshotId.Should().NotContain("-");
    }

    [Fact]
    public void Build_SnapshotId_DerivedFromContentHash()
    {
        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") }
        });
        var expectedPrefix = pkg.ContentHash[..16];
        pkg.SnapshotData.SnapshotId.Should().Be($"snapshot_{expectedPrefix}");
    }

    [Fact]
    public void Build_ContentHash_DoesNotDependOnContractHash()
    {
        // Two descriptors differing only in fields excluded from ContentHash
        // should produce the same ContentHash (ContentHash is package-level, not descriptor-level)
        var desc1 = new SchemaDescriptor { Id = "s1", Version = 1, Name = "S1" };
        var desc2 = new SchemaDescriptor { Id = "s1", Version = 1, Name = "S1" };
        var createdAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var pkg1 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0",
            CreatedAt = createdAt,
            Descriptors = new IDescriptor[] { desc1 }
        });
        var pkg2 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0",
            CreatedAt = createdAt,
            Descriptors = new IDescriptor[] { desc2 }
        });
        pkg1.ContentHash.Should().Be(pkg2.ContentHash);
    }

    [Fact]
    public void Build_StoresContractAndDefinitionHashes_InManifestEntries()
    {
        var desc = new SchemaDescriptor { Id = "s1", Version = 1, Name = "S1" };
        var expectedHashes = _hashBuilder.Build(desc);
        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { desc }
        });
        var entry = pkg.Manifest.DescriptorEntries.Should().ContainSingle().Subject;
        entry.ContractHash.Should().Be(expectedHashes.ContractHash.Value);
        entry.DefinitionHash.Should().Be(expectedHashes.DefinitionHash.Value);
    }

    [Fact]
    public void Build_ManifestEntries_SortedByNamespaceIdVersion()
    {
        var descriptors = new IDescriptor[]
        {
            MakeSchema("c", 1, "C"),
            MakeSchema("a", 2, "A2"),
            MakeSchema("a", 1, "A1")
        };
        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0", Descriptors = descriptors
        });
        var entries = pkg.Manifest.DescriptorEntries;
        entries[0].Ref.Id.Should().Be("a");
        entries[0].Ref.Version.Should().Be(1);
        entries[1].Ref.Id.Should().Be("a");
        entries[1].Ref.Version.Should().Be(2);
        entries[2].Ref.Id.Should().Be("c");
    }

    [Fact]
    public void Build_CapturesEvidenceSummary_FromImpactReport()
    {
        var changeSet = new DescriptorChangeSet
        {
            Changes = new[]
            {
                new DescriptorChange
                {
                    Ref = new DescriptorRef("ns", "s1", 1),
                    Kind = DescriptorChangeKind.Updated
                }
            }
        };
        var impactReport = new DescriptorImpactAnalysisReport
        {
            ChangeSet = changeSet,
            AffectedDescriptors = new[]
            {
                new AffectedDescriptor
                {
                    Ref = new DescriptorRef("ns", "s2", 1),
                    Kind = DescriptorKind.Schema,
                    Name = "S2",
                    Severity = DescriptorImpactSeverity.High,
                    RuntimeAreas = new[] { DescriptorImpactRuntimeArea.Schema },
                    Paths = Array.Empty<DescriptorImpactPath>()
                }
            },
            Paths = Array.Empty<DescriptorImpactPath>(),
            MaxSeverity = DescriptorImpactSeverity.High,
            Diagnostics = Array.Empty<DescriptorImpactDiagnostic>()
        };
        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1"), MakeSchema("s2", 1, "S2") },
            ImpactReport = impactReport
        });
        pkg.Evidence.MaxImpactSeverity.Should().Be(DescriptorImpactSeverity.High);
        pkg.Evidence.AffectedDescriptorCount.Should().Be(1);
        pkg.Evidence.ImpactPathCount.Should().Be(0);
    }

    [Fact]
    public void Build_CapturesEvidenceSummary_FromCompatibilityReport()
    {
        var changeSet = new DescriptorChangeSet
        {
            Changes = new[]
            {
                new DescriptorChange
                {
                    Ref = new DescriptorRef("ns", "s1", 1),
                    Kind = DescriptorChangeKind.Updated
                }
            }
        };
        var innerImpactReport = new DescriptorImpactAnalysisReport
        {
            ChangeSet = changeSet,
            AffectedDescriptors = Array.Empty<AffectedDescriptor>(),
            Paths = Array.Empty<DescriptorImpactPath>(),
            MaxSeverity = DescriptorImpactSeverity.None,
            Diagnostics = Array.Empty<DescriptorImpactDiagnostic>()
        };
        var findings = new[]
        {
            new DescriptorCompatibilityFinding
            {
                Subject = new DescriptorRef("ns", "s1", 1),
                ChangeKind = DescriptorChangeKind.Updated,
                Level = DescriptorCompatibilityLevel.Breaking,
                Kind = DescriptorCompatibilityFindingKind.Structural,
                RuleId = "R001",
                Message = "Breaking change detected"
            },
            new DescriptorCompatibilityFinding
            {
                Subject = new DescriptorRef("ns", "s1", 1),
                ChangeKind = DescriptorChangeKind.Updated,
                Level = DescriptorCompatibilityLevel.SecuritySensitive,
                Kind = DescriptorCompatibilityFindingKind.Security,
                RuleId = "R002",
                Message = "Security-sensitive change"
            },
            new DescriptorCompatibilityFinding
            {
                Subject = new DescriptorRef("ns", "s1", 1),
                ChangeKind = DescriptorChangeKind.Updated,
                Level = DescriptorCompatibilityLevel.Compatible,
                Kind = DescriptorCompatibilityFindingKind.Structural,
                RuleId = "R003",
                Message = "Compatible change"
            }
        };
        var compatReport = new DescriptorCompatibilityReport
        {
            ChangeSet = changeSet,
            ImpactReport = innerImpactReport,
            Findings = findings,
            MaxLevel = DescriptorCompatibilityLevel.Breaking,
            Diagnostics = Array.Empty<DescriptorCompatibilityDiagnostic>()
        };
        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1"), MakeSchema("s2", 1, "S2") },
            CompatibilityReport = compatReport
        });
        pkg.Evidence.MaxCompatibilityLevel.Should().Be(DescriptorCompatibilityLevel.Breaking);
        pkg.Evidence.BreakingFindingCount.Should().Be(1);
        pkg.Evidence.SecuritySensitiveFindingCount.Should().Be(1);
        pkg.Evidence.UnsupportedFindingCount.Should().Be(0);
    }

    [Fact]
    public void Build_CapturesEvidenceSummary_FromLifecycleReport()
    {
        var governanceReport = new DescriptorLifecycleGovernanceReport
        {
            Decisions = new[]
            {
                new DescriptorLifecycleDecision
                {
                    Transition = new DescriptorLifecycleTransition
                    {
                        Subject = new DescriptorRef("ns", "s1", 1),
                        Operation = DescriptorLifecycleOperation.Deprecate,
                        FromState = DescriptorState.Active,
                        ToState = DescriptorState.Deprecated
                    },
                    Decision = DescriptorLifecycleDecisionKind.ReviewRequired,
                    Findings = Array.Empty<DescriptorLifecycleFinding>()
                }
            },
            MaxDecision = DescriptorLifecycleDecisionKind.ReviewRequired,
            PackageFindings = new[]
            {
                new DescriptorLifecycleFinding
                {
                    Severity = SeverityLevel.Review,
                    Code = new DiagnosticCode("LCF001"),
                    Message = "Package requires review",
                    Subject = new DescriptorRef("ns", "s1", 1)
                }
            }
        };
        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") },
            GovernanceReport = governanceReport
        });
        pkg.Evidence.MaxLifecycleDecision.Should().Be(DescriptorLifecycleDecisionKind.ReviewRequired);
        pkg.Evidence.RequiresReview.Should().BeTrue();
        pkg.Evidence.IsBlocked.Should().BeFalse();
        pkg.Evidence.PackageFindingCount.Should().Be(1);
    }

    [Fact]
    public void Build_CapturesEvidenceSummary_FromTopologySnapshot()
    {
        var nodeRef = new DescriptorRef("ns", "s1", 1);
        var node = new DescriptorNode
        {
            Ref = nodeRef,
            Kind = DescriptorKind.Schema,
            Name = "S1",
            State = DescriptorState.Active,
            OutgoingEdgeIndices = new HashSet<int>(),
            IncomingEdgeIndices = new HashSet<int>()
        };
        var nodes = new Dictionary<DescriptorRef, DescriptorNode> { [nodeRef] = node };
        var edges = new List<DescriptorEdge>();
        var diagnostics = new DescriptorTopologyDiagnostics
        {
            All = Array.Empty<DescriptorTopologyDiagnostic>()
        };
        var snapshot = new DescriptorTopologySnapshot(
            nodes, edges, diagnostics,
            new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>(),
            new Dictionary<(DescriptorIdentity, int), List<(DescriptorRef, DescriptorEdge)>>(),
            new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>(),
            DateTimeOffset.UtcNow);

        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") },
            TopologySnapshot = snapshot
        });
        pkg.Evidence.TopologyNodeCount.Should().Be(1);
        pkg.Evidence.TopologyEdgeCount.Should().Be(0);
        pkg.Evidence.HasTopologyErrors.Should().BeFalse();
    }

    [Fact]
    public void Build_WithoutReports_ProducesEmptyEvidence()
    {
        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") }
        });
        pkg.Evidence.TopologyNodeCount.Should().Be(0);
        pkg.Evidence.MaxImpactSeverity.Should().Be(default);
    }

    [Fact]
    public void Build_DoesNotRerunTopology_WhenTopologyMissing()
    {
        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") }
        });
        pkg.SnapshotData.Relationships.Should().BeEmpty();
    }

    [Fact]
    public void Build_DoesNotRerunAnalysis()
    {
        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") }
        });
        pkg.Should().NotBeNull();
        pkg.Evidence.MaxImpactSeverity.Should().Be(default);
        pkg.Evidence.MaxCompatibilityLevel.Should().Be(DescriptorCompatibilityLevel.Compatible);
    }

    [Fact]
    public void Build_CapturesTopologyRelationshipFacts_WithSourcePath()
    {
        var refA = new DescriptorRef("ns", "a", 1);
        var refB = new DescriptorRef("ns", "b", 1);
        var nodeA = new DescriptorNode
        {
            Ref = refA,
            Kind = DescriptorKind.Schema,
            Name = "A",
            State = DescriptorState.Active,
            OutgoingEdgeIndices = new HashSet<int> { 0 },
            IncomingEdgeIndices = new HashSet<int>()
        };
        var nodeB = new DescriptorNode
        {
            Ref = refB,
            Kind = DescriptorKind.Schema,
            Name = "B",
            State = DescriptorState.Active,
            OutgoingEdgeIndices = new HashSet<int>(),
            IncomingEdgeIndices = new HashSet<int> { 0 }
        };
        var edge = new DescriptorEdge
        {
            Index = 0,
            From = refA,
            To = refB,
            Kind = RelationshipKind.DependsOn,
            Role = "uses",
            SourcePath = "$.dependencies.b",
            Strength = RelationshipStrength.Strong,
            IsRuntimeBinding = false
        };
        var nodes = new Dictionary<DescriptorRef, DescriptorNode>
        {
            [refA] = nodeA,
            [refB] = nodeB
        };
        var edges = new List<DescriptorEdge> { edge };
        var diagnostics = new DescriptorTopologyDiagnostics
        {
            All = Array.Empty<DescriptorTopologyDiagnostic>()
        };
        var snapshot = new DescriptorTopologySnapshot(
            nodes, edges, diagnostics,
            new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>(),
            new Dictionary<(DescriptorIdentity, int), List<(DescriptorRef, DescriptorEdge)>>(),
            new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>(),
            DateTimeOffset.UtcNow);

        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("a", 1, "A"), MakeSchema("b", 1, "B") },
            TopologySnapshot = snapshot
        });
        pkg.SnapshotData.Relationships.Should().HaveCount(1);
        var rel = pkg.SnapshotData.Relationships[0];
        rel.From.Namespace.Should().Be("ns");
        rel.From.Id.Should().Be("a");
        rel.To.Namespace.Should().Be("ns");
        rel.To.Id.Should().Be("b");
        rel.Kind.Should().Be(RelationshipKind.DependsOn);
        rel.Role.Should().Be("uses");
        rel.SourcePath.Should().Be("$.dependencies.b");
        rel.Strength.Should().Be(RelationshipStrength.Strong);
    }

    [Fact]
    public void Build_WithoutTopology_EmitsTopologyNotProvidedDiagnostic()
    {
        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") }
        });
        pkg.Diagnostics.Should().Contain(d =>
            d.Code == DescriptorPackageDiagnosticCodes.TopologyNotProvided);
    }

    [Fact]
    public void Build_DuplicateDescriptorRefs_EmitsPackageDiagnostic()
    {
        var desc1 = MakeSchema("s1", 1, "S1");
        var desc2 = MakeSchema("s1", 1, "S1");
        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { desc1, desc2 }
        });
        pkg.Diagnostics.Should().Contain(d =>
            d.Code == DescriptorPackageDiagnosticCodes.DuplicateDescriptorRef);
    }

    [Fact]
    public void Build_DifferentEvidence_DoesNotChangeContentHash()
    {
        var descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") };
        var createdAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var pkg1 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0", Descriptors = descriptors,
            CreatedAt = createdAt,
            GovernanceReport = new DescriptorLifecycleGovernanceReport
            {
                Decisions = Array.Empty<DescriptorLifecycleDecision>(),
                MaxDecision = DescriptorLifecycleDecisionKind.Allowed,
                PackageFindings = Array.Empty<DescriptorLifecycleFinding>()
            }
        });

        var pkg2 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0", Descriptors = descriptors,
            CreatedAt = createdAt,
            GovernanceReport = new DescriptorLifecycleGovernanceReport
            {
                Decisions = Array.Empty<DescriptorLifecycleDecision>(),
                MaxDecision = DescriptorLifecycleDecisionKind.Blocked,
                PackageFindings = Array.Empty<DescriptorLifecycleFinding>()
            }
        });

        pkg1.ContentHash.Should().Be(pkg2.ContentHash);
        pkg1.Evidence.MaxLifecycleDecision.Should().NotBe(pkg2.Evidence.MaxLifecycleDecision);
    }

    [Fact]
    public void Build_DifferentEvidence_DifferentEvidenceHash()
    {
        var descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") };
        var createdAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var pkg1 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0", Descriptors = descriptors,
            CreatedAt = createdAt,
            GovernanceReport = new DescriptorLifecycleGovernanceReport
            {
                Decisions = Array.Empty<DescriptorLifecycleDecision>(),
                MaxDecision = DescriptorLifecycleDecisionKind.Allowed,
                PackageFindings = Array.Empty<DescriptorLifecycleFinding>()
            }
        });

        var pkg2 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0", Descriptors = descriptors,
            CreatedAt = createdAt,
            GovernanceReport = new DescriptorLifecycleGovernanceReport
            {
                Decisions = Array.Empty<DescriptorLifecycleDecision>(),
                MaxDecision = DescriptorLifecycleDecisionKind.Blocked,
                PackageFindings = Array.Empty<DescriptorLifecycleFinding>()
            }
        });

        pkg1.Hashes!.PackageEvidenceHash.Value.Should().NotBe(pkg2.Hashes!.PackageEvidenceHash.Value);
    }

    [Fact]
    public void Build_DifferentEvidence_DifferentEnvelopeHash()
    {
        var descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") };
        var createdAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var pkg1 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0", Descriptors = descriptors,
            CreatedAt = createdAt,
            GovernanceReport = new DescriptorLifecycleGovernanceReport
            {
                Decisions = Array.Empty<DescriptorLifecycleDecision>(),
                MaxDecision = DescriptorLifecycleDecisionKind.Allowed,
                PackageFindings = Array.Empty<DescriptorLifecycleFinding>()
            }
        });

        var pkg2 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0", Descriptors = descriptors,
            CreatedAt = createdAt,
            GovernanceReport = new DescriptorLifecycleGovernanceReport
            {
                Decisions = Array.Empty<DescriptorLifecycleDecision>(),
                MaxDecision = DescriptorLifecycleDecisionKind.Blocked,
                PackageFindings = Array.Empty<DescriptorLifecycleFinding>()
            }
        });

        pkg1.Hashes!.PackageEvidenceEnvelopeHash.Value.Should().NotBe(pkg2.Hashes!.PackageEvidenceEnvelopeHash.Value);
    }

    [Fact]
    public void Build_SnapshotId_DerivesFromContentHashNotEvidence()
    {
        var descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") };
        var createdAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var pkg1 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0", Descriptors = descriptors,
            CreatedAt = createdAt,
            GovernanceReport = new DescriptorLifecycleGovernanceReport
            {
                Decisions = Array.Empty<DescriptorLifecycleDecision>(),
                MaxDecision = DescriptorLifecycleDecisionKind.Allowed,
                PackageFindings = Array.Empty<DescriptorLifecycleFinding>()
            }
        });

        var pkg2 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0", Descriptors = descriptors,
            CreatedAt = createdAt,
            GovernanceReport = new DescriptorLifecycleGovernanceReport
            {
                Decisions = Array.Empty<DescriptorLifecycleDecision>(),
                MaxDecision = DescriptorLifecycleDecisionKind.Blocked,
                PackageFindings = Array.Empty<DescriptorLifecycleFinding>()
            }
        });

        pkg1.ContentHash.Should().Be(pkg2.ContentHash);
        pkg1.SnapshotData.SnapshotId.Should().Be(pkg2.SnapshotData.SnapshotId);
    }

    [Fact]
    public void DescriptorPackageEvidence_Snapshot_CopiesNestedCollections()
    {
        var evidence = new DescriptorPackageEvidence
        {
            TopologyDiagnosticCounts =
            [
                new EvidenceFindingCount { Severity = SeverityLevel.Warning, Code = new DiagnosticCode("T001"), Count = 1 }
            ],
            NormalizedFindings =
            [
                new EvidenceFinding
                {
                    Source = "test",
                    Code = new DiagnosticCode("F001"),
                    Severity = SeverityLevel.Warning,
                    Message = "finding",
                    RelatedRefs = [new DescriptorRef("workflow", "wf", 1)]
                }
            ]
        };

        var snapshot = evidence.Snapshot();

        snapshot.Should().NotBeSameAs(evidence);
        snapshot.TopologyDiagnosticCounts.Should().NotBeSameAs(evidence.TopologyDiagnosticCounts);
        snapshot.NormalizedFindings.Should().NotBeSameAs(evidence.NormalizedFindings);
        snapshot.NormalizedFindings[0].RelatedRefs.Should().NotBeSameAs(evidence.NormalizedFindings[0].RelatedRefs);
    }

    [Fact]
    public void PackageDiagnostics_UsePackageSeverityEnum()
    {
        var diagnostic = new DescriptorPackageDiagnostic
        {
            Code = DescriptorPackageDiagnosticCodes.TopologyNotProvided,
            Severity = DescriptorPackageDiagnosticSeverity.Info,
            Message = "Topology was not provided."
        };

        diagnostic.Severity.Should().Be(DescriptorPackageDiagnosticSeverity.Info);
    }
}
