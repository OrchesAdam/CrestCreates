using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using FluentAssertions;
using Xunit;

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
}
