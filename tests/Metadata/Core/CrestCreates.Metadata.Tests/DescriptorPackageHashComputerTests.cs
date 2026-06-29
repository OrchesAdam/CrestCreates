using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using FluentAssertions;
using Xunit;
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Metadata.Tests;

public class DescriptorPackageHashComputerTests
{
    [Fact]
    public void ComputeContentHash_SameInput_ProducesSameHash()
    {
        var refs = new[]
        {
            new DescriptorManifestEntry { Ref = new DescriptorRef("schema", "s1", 1), Kind = DescriptorKind.Schema, Name = "S1", State = DescriptorState.Active, ContractHash = "abc", DefinitionHash = "def" },
            new DescriptorManifestEntry { Ref = new DescriptorRef("capability", "c1", 2), Kind = DescriptorKind.Capability, Name = "C1", State = DescriptorState.Active, ContractHash = "ghi", DefinitionHash = "jkl" }
        };
        var relationships = Array.Empty<DescriptorPackageRelationshipEntry>();

        var hash1 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs, relationships);
        var hash2 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs, relationships);

        hash1.Should().Be(hash2);
        hash1.Should().NotBeEmpty();
        hash1.Should().HaveLength(64);
    }

    [Fact]
    public void ComputeContentHash_DifferentInputOrder_SameHash()
    {
        var refs1 = new[]
        {
            new DescriptorManifestEntry { Ref = new DescriptorRef("schema", "s1", 1), Kind = DescriptorKind.Schema, Name = "S1", State = DescriptorState.Active },
            new DescriptorManifestEntry { Ref = new DescriptorRef("capability", "c1", 2), Kind = DescriptorKind.Capability, Name = "C1", State = DescriptorState.Active }
        };
        var refs2 = new[]
        {
            new DescriptorManifestEntry { Ref = new DescriptorRef("capability", "c1", 2), Kind = DescriptorKind.Capability, Name = "C1", State = DescriptorState.Active },
            new DescriptorManifestEntry { Ref = new DescriptorRef("schema", "s1", 1), Kind = DescriptorKind.Schema, Name = "S1", State = DescriptorState.Active }
        };
        var relationships = Array.Empty<DescriptorPackageRelationshipEntry>();

        var hash1 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs1, relationships);
        var hash2 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs2, relationships);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeContentHash_ChangedDescriptorRef_ChangesHash()
    {
        var refs1 = new[] { new DescriptorManifestEntry { Ref = new DescriptorRef("schema", "s1", 1), Kind = DescriptorKind.Schema, Name = "S1", State = DescriptorState.Active } };
        var refs2 = new[] { new DescriptorManifestEntry { Ref = new DescriptorRef("schema", "s1", 2), Kind = DescriptorKind.Schema, Name = "S1", State = DescriptorState.Active } };
        var relationships = Array.Empty<DescriptorPackageRelationshipEntry>();

        var hash1 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs1, relationships);
        var hash2 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs2, relationships);

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeContentHash_IgnoresContractHash()
    {
        var refs1 = new[] { new DescriptorManifestEntry { Ref = new DescriptorRef("schema", "s1", 1), Kind = DescriptorKind.Schema, Name = "S1", State = DescriptorState.Active, ContractHash = "old" } };
        var refs2 = new[] { new DescriptorManifestEntry { Ref = new DescriptorRef("schema", "s1", 1), Kind = DescriptorKind.Schema, Name = "S1", State = DescriptorState.Active, ContractHash = "new" } };
        var relationships = Array.Empty<DescriptorPackageRelationshipEntry>();

        var hash1 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs1, relationships);
        var hash2 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs2, relationships);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeContentHash_IncludesRelationships()
    {
        var entries = new[] { new DescriptorManifestEntry { Ref = new DescriptorRef("schema", "s1", 1), Kind = DescriptorKind.Schema, Name = "S1", State = DescriptorState.Active } };
        var rels = new[] { new DescriptorPackageRelationshipEntry { From = new DescriptorRef("schema", "s1", 1), To = new DescriptorRef("capability", "c1", 1), Kind = RelationshipKind.References, Strength = RelationshipStrength.Strong, IsRuntimeBinding = false } };
        var noRels = Array.Empty<DescriptorPackageRelationshipEntry>();

        var hashWith = DescriptorPackageHashComputer.ComputeContentHash("1.0", entries, rels);
        var hashWithout = DescriptorPackageHashComputer.ComputeContentHash("1.0", entries, noRels);

        hashWith.Should().NotBe(hashWithout);
    }

    [Fact]
    public void ComputeContentHash_DifferentEvidence_SameHash()
    {
        var entries = new[] { new DescriptorManifestEntry { Ref = new DescriptorRef("schema", "s1", 1), Kind = DescriptorKind.Schema, Name = "S1", State = DescriptorState.Active } };
        var relationships = Array.Empty<DescriptorPackageRelationshipEntry>();

        var hashA = DescriptorPackageHashComputer.ComputeContentHash("1.0", entries, relationships);
        var hashB = DescriptorPackageHashComputer.ComputeContentHash("1.0", entries, relationships);

        hashA.Should().Be(hashB);
    }

    [Fact]
    public void ComputeEnvelopeHash_DifferentEvidence_DifferentHash()
    {
        var contentHash = "deadbeef";
        var createdAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var hash1 = DescriptorPackageHashComputer.ComputeEnvelopeHash(contentHash, "evidence1", "pkg", "1.0", createdAt, null, null);
        var hash2 = DescriptorPackageHashComputer.ComputeEnvelopeHash(contentHash, "evidence2", "pkg", "1.0", createdAt, null, null);

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeEnvelopeHash_SameContentDifferentEvidences_DifferentEnvelopeHash()
    {
        var contentHash = "deadbeef";
        var createdAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var hash1 = DescriptorPackageHashComputer.ComputeEnvelopeHash(contentHash, "evidenceA", "pkg", "1.0", createdAt, null, null);
        var hash2 = DescriptorPackageHashComputer.ComputeEnvelopeHash(contentHash, "evidenceB", "pkg", "1.0", createdAt, null, null);

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeEvidenceHash_Deterministic()
    {
        var evidence = new DescriptorPackageEvidence
        {
            TopologyNodeCount = 5, TopologyEdgeCount = 10, HasTopologyErrors = false,
            MaxImpactSeverity = DescriptorImpactSeverity.Critical, AffectedDescriptorCount = 3,
            MaxCompatibilityLevel = DescriptorCompatibilityLevel.Breaking, BreakingFindingCount = 1,
            MaxLifecycleDecision = DescriptorLifecycleDecisionKind.ReviewRequired, RequiresReview = true
        };

        var hash1 = DescriptorPackageHashComputer.ComputeEvidenceHash(evidence);
        var hash2 = DescriptorPackageHashComputer.ComputeEvidenceHash(evidence);

        hash1.Should().Be(hash2);
        hash1.Should().HaveLength(64);
    }

    // ── Post-canonicalization regression tests ──────────────────

    [Fact]
    public void ComputeContentHash_UsesOrdinalOrdering()
    {
        // Names that differ only by case or culture-sensitivity must produce stable order
        var entries = new[]
        {
            new DescriptorManifestEntry { Ref = new DescriptorRef("Schema", "Zebra", 1), Kind = DescriptorKind.Schema, Name = "Z", State = DescriptorState.Active },
            new DescriptorManifestEntry { Ref = new DescriptorRef("schema", "alpha", 1), Kind = DescriptorKind.Schema, Name = "A", State = DescriptorState.Active },
        };
        var relationships = Array.Empty<DescriptorPackageRelationshipEntry>();

        var hash1 = DescriptorPackageHashComputer.ComputeContentHash("1.0", entries, relationships);
        var hash2 = DescriptorPackageHashComputer.ComputeContentHash("1.0", entries, relationships);

        hash1.Should().Be(hash2);
        hash1.Should().HaveLength(64);
    }

    [Fact]
    public void ComputeContentHash_DelimiterCharacters_DoNotCollide()
    {
        var entries1 = new[]
        {
            new DescriptorManifestEntry { Ref = new DescriptorRef("sch|ema", "s1", 1), Kind = DescriptorKind.Schema, Name = "S1", State = DescriptorState.Active }
        };
        var entries2 = new[]
        {
            new DescriptorManifestEntry { Ref = new DescriptorRef("sch", "ema|s1", 1), Kind = DescriptorKind.Schema, Name = "S1", State = DescriptorState.Active }
        };
        var relationships = Array.Empty<DescriptorPackageRelationshipEntry>();

        var h1 = DescriptorPackageHashComputer.ComputeContentHash("1.0", entries1, relationships);
        var h2 = DescriptorPackageHashComputer.ComputeContentHash("1.0", entries2, relationships);

        h1.Should().NotBe(h2, "pipe in field values must not cause delimiter ambiguity");
    }

    [Fact]
    public void ComputeEnvelopeHash_NullAndEmptyStrings_AreDifferent()
    {
        var contentHash = "deadbeef";
        var createdAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var hashNull = DescriptorPackageHashComputer.ComputeEnvelopeHash(contentHash, "ev", "pkg", "1.0", createdAt, createdBy: null, source: null);
        var hashEmpty = DescriptorPackageHashComputer.ComputeEnvelopeHash(contentHash, "ev", "pkg", "1.0", createdAt, createdBy: "", source: "");

        hashNull.Should().NotBe(hashEmpty, "null and empty string must produce different hashes");
    }

    [Fact]
    public void ComputeEnvelopeHash_UsesInvariantTimestamp()
    {
        var contentHash = "deadbeef";
        var createdAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var hash1 = DescriptorPackageHashComputer.ComputeEnvelopeHash(contentHash, "ev", "pkg", "1.0", createdAt, null, null);
        var hash2 = DescriptorPackageHashComputer.ComputeEnvelopeHash(contentHash, "ev", "pkg", "1.0", createdAt, null, null);

        hash1.Should().Be(hash2);
        hash1.Should().HaveLength(64);
    }

    [Fact]
    public void ComputeEvidenceHash_RelatedRefs_OrderInsensitive_WithOrdinalOrdering()
    {
        var finding1 = new EvidenceFinding
        {
            Source = "test", Code = new DiagnosticCode("T001"), Severity = SeverityLevel.Error, Message = "msg",
            RelatedRefs = new[]
            {
                new DescriptorRef("capability", "c2", 1),
                new DescriptorRef("schema", "s1", 1),
            }
        };
        var finding2 = new EvidenceFinding
        {
            Source = "test", Code = new DiagnosticCode("T001"), Severity = SeverityLevel.Error, Message = "msg",
            RelatedRefs = new[]
            {
                new DescriptorRef("schema", "s1", 1),
                new DescriptorRef("capability", "c2", 1),
            }
        };

        var evidence1 = new DescriptorPackageEvidence { NormalizedFindings = new[] { finding1 } };
        var evidence2 = new DescriptorPackageEvidence { NormalizedFindings = new[] { finding2 } };

        var h1 = DescriptorPackageHashComputer.ComputeEvidenceHash(evidence1);
        var h2 = DescriptorPackageHashComputer.ComputeEvidenceHash(evidence2);

        h1.Should().Be(h2, "related refs order must be canonicalized");
    }

    [Fact]
    public void ComputeEvidenceHash_NormalizedFindings_OrderInsensitive_WhenRelatedRefsDiffer()
    {
        // Two findings with identical Source/Code/Severity/Subject/Message
        // but different RelatedRefs — outer finding order must be stable.
        var findingA = new EvidenceFinding
        {
            Source = "test", Code = new DiagnosticCode("T001"), Severity = SeverityLevel.Error, Message = "msg",
            RelatedRefs = new[] { new DescriptorRef("capability", "c1", 1) }
        };
        var findingB = new EvidenceFinding
        {
            Source = "test", Code = new DiagnosticCode("T001"), Severity = SeverityLevel.Error, Message = "msg",
            RelatedRefs = new[] { new DescriptorRef("schema", "s1", 1) }
        };

        var evidence1 = new DescriptorPackageEvidence { NormalizedFindings = new[] { findingA, findingB } };
        var evidence2 = new DescriptorPackageEvidence { NormalizedFindings = new[] { findingB, findingA } };

        var h1 = DescriptorPackageHashComputer.ComputeEvidenceHash(evidence1);
        var h2 = DescriptorPackageHashComputer.ComputeEvidenceHash(evidence2);

        h1.Should().Be(h2,
            "finding order must be canonicalized even when RelatedRefs differ but outer keys match");
    }
}
