using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using Xunit;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Metadata.Abstractions.Evidence;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.DescriptorPackage.CanonicalHashing;
using FluentAssertions;

namespace CrestCreates.Metadata.Tests.DescriptorPackage.CanonicalHashing;

public sealed class DescriptorPackageCanonicalHashComputerTests
{
    [Fact]
    public void ComputeHashSet_Produces_Correct_Artifact_Metadata()
    {
        var computer = new DefaultDescriptorPackageCanonicalHashComputer(new DefaultCanonicalHashComputer());
        var manifest = CreateManifest();
        var evidence = CreateEvidence();
        var envelopeMetadata = CreateEnvelopeMetadata();

        var hashes = computer.ComputeHashSet(manifest, evidence, envelopeMetadata);

        hashes.PackageManifestHash.ArtifactKind.Should().Be(CanonicalHashArtifactNames.PackageManifest);
        hashes.PackageManifestHash.Purpose.Should().Be(CanonicalHashPurposeNames.Integrity);
        hashes.PackageManifestHash.Scope.Should().Be(CanonicalHashScopeNames.InternalFull);
        hashes.PackageManifestHash.Algorithm.Should().Be("SHA-256");
        hashes.PackageManifestHash.AlgorithmVersion.Should().Be("sha256-canonical-json-v1");
        hashes.PackageManifestHash.CanonicalShapeVersion.Should().Be(DescriptorPackageCanonicalShapeVersions.PackageManifestV1);
        hashes.PackageManifestHash.ContractVersion.Should().Be(CanonicalHashContractVersions.DescriptorHash);

        hashes.PackageEvidenceHash.ArtifactKind.Should().Be(CanonicalHashArtifactNames.PackageEvidence);
        hashes.PackageEvidenceHash.Purpose.Should().Be(CanonicalHashPurposeNames.AuditEvidence);
        hashes.PackageEvidenceHash.CanonicalShapeVersion.Should().Be(DescriptorPackageCanonicalShapeVersions.PackageEvidenceV1);

        hashes.PackageEvidenceEnvelopeHash.ArtifactKind.Should().Be(CanonicalHashArtifactNames.PackageEvidenceEnvelope);
        hashes.PackageEvidenceEnvelopeHash.Purpose.Should().Be(CanonicalHashPurposeNames.AuditEvidence);
        hashes.PackageEvidenceEnvelopeHash.CanonicalShapeVersion.Should().Be(DescriptorPackageCanonicalShapeVersions.PackageEvidenceEnvelopeV1);
    }

    [Fact]
    public void ComputeHashSet_All_Values_Are_NonEmpty()
    {
        var computer = new DefaultDescriptorPackageCanonicalHashComputer(new DefaultCanonicalHashComputer());
        var hashes = computer.ComputeHashSet(CreateManifest(), CreateEvidence(), CreateEnvelopeMetadata());

        hashes.PackageManifestHash.Value.Should().NotBeEmpty();
        hashes.PackageEvidenceHash.Value.Should().NotBeEmpty();
        hashes.PackageEvidenceEnvelopeHash.Value.Should().NotBeEmpty();
    }

    [Fact]
    public void ComputeHashSet_Is_Deterministic()
    {
        var computer = new DefaultDescriptorPackageCanonicalHashComputer(new DefaultCanonicalHashComputer());
        var manifest = CreateManifest();
        var evidence = CreateEvidence();
        var envelopeMetadata = CreateEnvelopeMetadata();

        var first = computer.ComputeHashSet(manifest, evidence, envelopeMetadata);
        var second = computer.ComputeHashSet(manifest, evidence, envelopeMetadata);

        first.PackageManifestHash.Value.Should().Be(second.PackageManifestHash.Value);
        first.PackageEvidenceHash.Value.Should().Be(second.PackageEvidenceHash.Value);
        first.PackageEvidenceEnvelopeHash.Value.Should().Be(second.PackageEvidenceEnvelopeHash.Value);
    }

    [Fact]
    public void ComputeHashSet_DifferentManifest_ProducesDifferentHash()
    {
        var computer = new DefaultDescriptorPackageCanonicalHashComputer(new DefaultCanonicalHashComputer());
        var evidence = CreateEvidence();
        var envelopeMetadata = CreateEnvelopeMetadata();

        var manifest1 = CreateManifest();
        var manifest2 = CreateManifest("different-pkg");

        var hash1 = computer.ComputeHashSet(manifest1, evidence, envelopeMetadata);
        var hash2 = computer.ComputeHashSet(manifest2, evidence, envelopeMetadata);

        hash1.PackageManifestHash.Value.Should().NotBe(hash2.PackageManifestHash.Value);
        // Evidence hash should stay same since evidence didn't change
        hash1.PackageEvidenceHash.Value.Should().Be(hash2.PackageEvidenceHash.Value);
        // Envelope hash should be different since manifest hash changed
        hash1.PackageEvidenceEnvelopeHash.Value.Should().NotBe(hash2.PackageEvidenceEnvelopeHash.Value);
    }

    [Fact]
    public void ComputeHashSet_DifferentEvidence_ProducesDifferentEvidenceHash()
    {
        var computer = new DefaultDescriptorPackageCanonicalHashComputer(new DefaultCanonicalHashComputer());
        var manifest = CreateManifest();
        var envelopeMetadata = CreateEnvelopeMetadata();

        var evidence1 = CreateEvidence();
        var evidence2 = CreateEvidence();
        evidence2 = new DescriptorPackageEvidence
        {
            TopologyNodeCount = 99,
            TopologyEdgeCount = evidence2.TopologyEdgeCount,
            TopologyDiagnosticCounts = evidence2.TopologyDiagnosticCounts,
            HasTopologyErrors = evidence2.HasTopologyErrors,
            MaxImpactSeverity = evidence2.MaxImpactSeverity,
            AffectedDescriptorCount = evidence2.AffectedDescriptorCount,
            ImpactPathCount = evidence2.ImpactPathCount,
            ImpactDiagnosticCounts = evidence2.ImpactDiagnosticCounts,
            MaxCompatibilityLevel = evidence2.MaxCompatibilityLevel,
            BreakingFindingCount = evidence2.BreakingFindingCount,
            SecuritySensitiveFindingCount = evidence2.SecuritySensitiveFindingCount,
            UnsupportedFindingCount = evidence2.UnsupportedFindingCount,
            MaxLifecycleDecision = evidence2.MaxLifecycleDecision,
            RequiresReview = evidence2.RequiresReview,
            IsBlocked = evidence2.IsBlocked,
            PackageFindingCount = evidence2.PackageFindingCount,
            NormalizedFindings = evidence2.NormalizedFindings
        };

        var hash1 = computer.ComputeHashSet(manifest, evidence1, envelopeMetadata);
        var hash2 = computer.ComputeHashSet(manifest, evidence2, envelopeMetadata);

        hash1.PackageManifestHash.Value.Should().Be(hash2.PackageManifestHash.Value);
        hash1.PackageEvidenceHash.Value.Should().NotBe(hash2.PackageEvidenceHash.Value);
        hash1.PackageEvidenceEnvelopeHash.Value.Should().NotBe(hash2.PackageEvidenceEnvelopeHash.Value);
    }

    // ── Manifest sensitivity tests ──────────────────────────────────

    [Fact]
    public void ComputeManifestHash_DifferentPackageId_ProducesDifferentHash()
    {
        var computer = new DefaultDescriptorPackageCanonicalHashComputer(new DefaultCanonicalHashComputer());
        var evidence = CreateEvidence();
        var envelopeMetadata = CreateEnvelopeMetadata();

        var m1 = CreateManifest("pkg-a");
        var m2 = CreateManifest("pkg-b");

        var h1 = computer.ComputeHashSet(m1, evidence, envelopeMetadata);
        var h2 = computer.ComputeHashSet(m2, evidence, envelopeMetadata);

        h1.PackageManifestHash.Value.Should().NotBe(h2.PackageManifestHash.Value);
    }

    [Fact]
    public void ComputeManifestHash_DifferentCreatedAt_ProducesDifferentHash()
    {
        var computer = new DefaultDescriptorPackageCanonicalHashComputer(new DefaultCanonicalHashComputer());
        var evidence = CreateEvidence();
        var envelopeMetadata = CreateEnvelopeMetadata();

        var m1 = CreateManifest();
        var m2 = CreateManifest();
        m2 = new DescriptorManifest
        {
            FormatVersion = m2.FormatVersion,
            PackageId = m2.PackageId,
            PackageVersion = m2.PackageVersion,
            Name = m2.Name,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            CreatedBy = m2.CreatedBy,
            Source = m2.Source,
            DescriptorCount = m2.DescriptorCount,
            DescriptorEntries = m2.DescriptorEntries
        };

        var h1 = computer.ComputeHashSet(m1, evidence, envelopeMetadata);
        var h2 = computer.ComputeHashSet(m2, evidence, envelopeMetadata);

        h1.PackageManifestHash.Value.Should().NotBe(h2.PackageManifestHash.Value);
    }

    [Fact]
    public void ComputeManifestHash_DifferentFormatVersion_ProducesDifferentHash()
    {
        var computer = new DefaultDescriptorPackageCanonicalHashComputer(new DefaultCanonicalHashComputer());
        var evidence = CreateEvidence();
        var envelopeMetadata = CreateEnvelopeMetadata();

        var m1 = CreateManifest();
        var m2 = CreateManifest();
        m2 = new DescriptorManifest
        {
            FormatVersion = "2.0",
            PackageId = m2.PackageId,
            PackageVersion = m2.PackageVersion,
            Name = m2.Name,
            CreatedAt = m2.CreatedAt,
            CreatedBy = m2.CreatedBy,
            Source = m2.Source,
            DescriptorCount = m2.DescriptorCount,
            DescriptorEntries = m2.DescriptorEntries
        };

        var h1 = computer.ComputeHashSet(m1, evidence, envelopeMetadata);
        var h2 = computer.ComputeHashSet(m2, evidence, envelopeMetadata);

        h1.PackageManifestHash.Value.Should().NotBe(h2.PackageManifestHash.Value);
    }

    [Fact]
    public void ComputeManifestHash_AddedEntry_ProducesDifferentHash()
    {
        var computer = new DefaultDescriptorPackageCanonicalHashComputer(new DefaultCanonicalHashComputer());
        var evidence = CreateEvidence();
        var envelopeMetadata = CreateEnvelopeMetadata();

        var m1 = CreateManifest();
        var m2 = CreateManifest();
        m2 = new DescriptorManifest
        {
            FormatVersion = m2.FormatVersion,
            PackageId = m2.PackageId,
            PackageVersion = m2.PackageVersion,
            Name = m2.Name,
            CreatedAt = m2.CreatedAt,
            CreatedBy = m2.CreatedBy,
            Source = m2.Source,
            DescriptorCount = 3,
            DescriptorEntries = m2.DescriptorEntries
                .Append(new DescriptorManifestEntry
                {
                    Ref = new DescriptorRef("capability", "f1", 1),
                    Kind = DescriptorKind.Capability,
                    Name = "NewCapability",
                    State = DescriptorState.Active,
                    ContractHash = "zzz999",
                    DefinitionHash = "yyy888"
                })
                .ToArray()
        };

        var h1 = computer.ComputeHashSet(m1, evidence, envelopeMetadata);
        var h2 = computer.ComputeHashSet(m2, evidence, envelopeMetadata);

        h1.PackageManifestHash.Value.Should().NotBe(h2.PackageManifestHash.Value);
    }

    [Fact]
    public void ComputeManifestHash_EntryOrderChange_ProducesSameHash()
    {
        var computer = new DefaultDescriptorPackageCanonicalHashComputer(new DefaultCanonicalHashComputer());
        var evidence = CreateEvidence();
        var envelopeMetadata = CreateEnvelopeMetadata();

        var m1 = CreateManifest();
        // Reverse the entry order — canonical sorting should produce the same hash
        var m2 = new DescriptorManifest
        {
            FormatVersion = m1.FormatVersion,
            PackageId = m1.PackageId,
            PackageVersion = m1.PackageVersion,
            Name = m1.Name,
            CreatedAt = m1.CreatedAt,
            CreatedBy = m1.CreatedBy,
            Source = m1.Source,
            DescriptorCount = m1.DescriptorCount,
            DescriptorEntries = m1.DescriptorEntries.Reverse().ToArray()
        };

        var h1 = computer.ComputeHashSet(m1, evidence, envelopeMetadata);
        var h2 = computer.ComputeHashSet(m2, evidence, envelopeMetadata);

        h1.PackageManifestHash.Value.Should().Be(h2.PackageManifestHash.Value,
            "canonical sorting must produce the same hash regardless of entry input order");
    }

    // ── Evidence sensitivity tests ───────────────────────────────────

    [Fact]
    public void ComputeEvidenceHash_DifferentNodeCount_ProducesDifferentHash()
    {
        var computer = new DefaultDescriptorPackageCanonicalHashComputer(new DefaultCanonicalHashComputer());
        var manifest = CreateManifest();
        var envelopeMetadata = CreateEnvelopeMetadata();

        var e1 = CreateEvidence();
        var e2 = CreateEvidence();
        e2 = new DescriptorPackageEvidence
        {
            TopologyNodeCount = 999,
            TopologyEdgeCount = e2.TopologyEdgeCount,
            TopologyDiagnosticCounts = e2.TopologyDiagnosticCounts,
            HasTopologyErrors = e2.HasTopologyErrors,
            MaxImpactSeverity = e2.MaxImpactSeverity,
            AffectedDescriptorCount = e2.AffectedDescriptorCount,
            ImpactPathCount = e2.ImpactPathCount,
            ImpactDiagnosticCounts = e2.ImpactDiagnosticCounts,
            MaxCompatibilityLevel = e2.MaxCompatibilityLevel,
            BreakingFindingCount = e2.BreakingFindingCount,
            SecuritySensitiveFindingCount = e2.SecuritySensitiveFindingCount,
            UnsupportedFindingCount = e2.UnsupportedFindingCount,
            MaxLifecycleDecision = e2.MaxLifecycleDecision,
            RequiresReview = e2.RequiresReview,
            IsBlocked = e2.IsBlocked,
            PackageFindingCount = e2.PackageFindingCount,
            NormalizedFindings = e2.NormalizedFindings
        };

        var h1 = computer.ComputeHashSet(manifest, e1, envelopeMetadata);
        var h2 = computer.ComputeHashSet(manifest, e2, envelopeMetadata);

        h1.PackageEvidenceHash.Value.Should().NotBe(h2.PackageEvidenceHash.Value);
    }

    [Fact]
    public void ComputeEvidenceHash_DifferentMaxImpactSeverity_ProducesDifferentHash()
    {
        var computer = new DefaultDescriptorPackageCanonicalHashComputer(new DefaultCanonicalHashComputer());
        var manifest = CreateManifest();
        var envelopeMetadata = CreateEnvelopeMetadata();

        var e1 = CreateEvidence();
        var e2 = CreateEvidence();
        e2 = new DescriptorPackageEvidence
        {
            TopologyNodeCount = e2.TopologyNodeCount,
            TopologyEdgeCount = e2.TopologyEdgeCount,
            TopologyDiagnosticCounts = e2.TopologyDiagnosticCounts,
            HasTopologyErrors = e2.HasTopologyErrors,
            MaxImpactSeverity = DescriptorImpactSeverity.Critical,
            AffectedDescriptorCount = e2.AffectedDescriptorCount,
            ImpactPathCount = e2.ImpactPathCount,
            ImpactDiagnosticCounts = e2.ImpactDiagnosticCounts,
            MaxCompatibilityLevel = e2.MaxCompatibilityLevel,
            BreakingFindingCount = e2.BreakingFindingCount,
            SecuritySensitiveFindingCount = e2.SecuritySensitiveFindingCount,
            UnsupportedFindingCount = e2.UnsupportedFindingCount,
            MaxLifecycleDecision = e2.MaxLifecycleDecision,
            RequiresReview = e2.RequiresReview,
            IsBlocked = e2.IsBlocked,
            PackageFindingCount = e2.PackageFindingCount,
            NormalizedFindings = e2.NormalizedFindings
        };

        var h1 = computer.ComputeHashSet(manifest, e1, envelopeMetadata);
        var h2 = computer.ComputeHashSet(manifest, e2, envelopeMetadata);

        h1.PackageEvidenceHash.Value.Should().NotBe(h2.PackageEvidenceHash.Value);
    }

    [Fact]
    public void ComputeEvidenceHash_FindingOrderChange_ProducesSameHash()
    {
        var computer = new DefaultDescriptorPackageCanonicalHashComputer(new DefaultCanonicalHashComputer());
        var manifest = CreateManifest();
        var envelopeMetadata = CreateEnvelopeMetadata();

        var findingA = new EvidenceFinding
        {
            Severity = "Error", Code = "C", Source = "test", Message = "M3",
            RelatedRefs = new[] { new DescriptorRef("z", "z", 1) }
        };
        var findingB = new EvidenceFinding
        {
            Severity = "Error", Code = "C", Source = "test", Message = "M1",
            RelatedRefs = new[] { new DescriptorRef("a", "a", 1) }
        };

        var e1 = new DescriptorPackageEvidence
        {
            TopologyNodeCount = 1,
            MaxImpactSeverity = DescriptorImpactSeverity.Low,
            NormalizedFindings = new[] { findingA, findingB }
        };
        var e2 = new DescriptorPackageEvidence
        {
            TopologyNodeCount = 1,
            MaxImpactSeverity = DescriptorImpactSeverity.Low,
            NormalizedFindings = new[] { findingB, findingA }
        };

        var h1 = computer.ComputeHashSet(manifest, e1, envelopeMetadata);
        var h2 = computer.ComputeHashSet(manifest, e2, envelopeMetadata);

        h1.PackageEvidenceHash.Value.Should().Be(h2.PackageEvidenceHash.Value,
            "canonical sorting of findings must produce the same hash regardless of input order");
    }

    private static DescriptorManifest CreateManifest(string? packageId = null)
    {
        return new DescriptorManifest
        {
            FormatVersion = "1.0",
            PackageId = packageId ?? "test-pkg-001",
            PackageVersion = "1.0.0",
            Name = "Test Package",
            CreatedAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero),
            CreatedBy = "test-user",
            Source = "test-source",
            DescriptorCount = 2,
            DescriptorEntries = new[]
            {
                new DescriptorManifestEntry
                {
                    Ref = new DescriptorRef("schema", "s1", 1),
                    Kind = DescriptorKind.Schema,
                    Name = "UserSchema",
                    State = DescriptorState.Active,
                    ContractHash = "abc123",
                    DefinitionHash = "def456"
                },
                new DescriptorManifestEntry
                {
                    Ref = new DescriptorRef("capability", "c1", 2),
                    Kind = DescriptorKind.Capability,
                    Name = "CreateUser",
                    State = DescriptorState.Active,
                    ContractHash = "ghi789",
                    DefinitionHash = "jkl012",
                    SupersededById = "capability.c1.1"
                }
            }
        };
    }

    private static DescriptorPackageEvidence CreateEvidence()
    {
        return new DescriptorPackageEvidence
        {
            TopologyNodeCount = 2,
            TopologyEdgeCount = 1,
            HasTopologyErrors = false,
            TopologyDiagnosticCounts = new[]
            {
                new EvidenceFindingCount { Severity = "Info", Code = "T001", Count = 1 }
            },
            MaxImpactSeverity = DescriptorImpactSeverity.Low,
            AffectedDescriptorCount = 0,
            ImpactPathCount = 0,
            ImpactDiagnosticCounts = Array.Empty<EvidenceFindingCount>(),
            MaxCompatibilityLevel = DescriptorCompatibilityLevel.Compatible,
            BreakingFindingCount = 0,
            SecuritySensitiveFindingCount = 0,
            UnsupportedFindingCount = 0,
            MaxLifecycleDecision = DescriptorLifecycleDecisionKind.Allowed,
            RequiresReview = false,
            IsBlocked = false,
            PackageFindingCount = 0,
            NormalizedFindings = new[]
            {
                new EvidenceFinding
                {
                    Severity = "Info",
                    Code = "P001",
                    Source = "test",
                    Message = "Package is valid",
                    Subject = new DescriptorRef("capability", "c1", 2),
                    RelatedRefs = new[]
                    {
                        new DescriptorRef("schema", "s1", 1)
                    }
                }
            }
        };
    }

    private static DescriptorPackageEvidenceEnvelopeMetadata CreateEnvelopeMetadata()
    {
        return new DescriptorPackageEvidenceEnvelopeMetadata
        {
            PackageId = "test-pkg-001",
            PackageVersion = "1.0.0",
            CreatedAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero),
            CreatedBy = "test-user",
            Source = "test-source"
        };
    }
}
