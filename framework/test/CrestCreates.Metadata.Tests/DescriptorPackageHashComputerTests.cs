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
            new DescriptorManifestEntry
            {
                Ref = new DescriptorRef("schema", "s1", 1),
                Kind = DescriptorKind.Schema,
                Name = "S1",
                State = DescriptorState.Active,
                ContractHash = "abc",
                DefinitionHash = "def"
            },
            new DescriptorManifestEntry
            {
                Ref = new DescriptorRef("capability", "c1", 2),
                Kind = DescriptorKind.Capability,
                Name = "C1",
                State = DescriptorState.Active,
                ContractHash = "ghi",
                DefinitionHash = "jkl"
            }
        };

        var relationships = Array.Empty<DescriptorPackageRelationshipEntry>();
        var evidenceHash = "evhash123";

        var hash1 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs, relationships, evidenceHash);
        var hash2 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs, relationships, evidenceHash);

        hash1.Should().Be(hash2);
        hash1.Should().NotBeEmpty();
        hash1.Should().HaveLength(64); // SHA-256 hex is 64 chars
    }

    [Fact]
    public void ComputeContentHash_DifferentInputOrder_SameHash()
    {
        var refs1 = new[]
        {
            new DescriptorManifestEntry
            {
                Ref = new DescriptorRef("schema", "s1", 1),
                Kind = DescriptorKind.Schema,
                Name = "S1",
                State = DescriptorState.Active
            },
            new DescriptorManifestEntry
            {
                Ref = new DescriptorRef("capability", "c1", 2),
                Kind = DescriptorKind.Capability,
                Name = "C1",
                State = DescriptorState.Active
            }
        };

        var refs2 = new[]
        {
            new DescriptorManifestEntry
            {
                Ref = new DescriptorRef("capability", "c1", 2),
                Kind = DescriptorKind.Capability,
                Name = "C1",
                State = DescriptorState.Active
            },
            new DescriptorManifestEntry
            {
                Ref = new DescriptorRef("schema", "s1", 1),
                Kind = DescriptorKind.Schema,
                Name = "S1",
                State = DescriptorState.Active
            }
        };

        var relationships = Array.Empty<DescriptorPackageRelationshipEntry>();
        var evidenceHash = "evhash123";

        var hash1 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs1, relationships, evidenceHash);
        var hash2 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs2, relationships, evidenceHash);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeContentHash_ChangedDescriptorRef_ChangesHash()
    {
        var refs1 = new[]
        {
            new DescriptorManifestEntry
            {
                Ref = new DescriptorRef("schema", "s1", 1),
                Kind = DescriptorKind.Schema,
                Name = "S1",
                State = DescriptorState.Active
            }
        };

        var refs2 = new[]
        {
            new DescriptorManifestEntry
            {
                Ref = new DescriptorRef("schema", "s1", 2), // version changed
                Kind = DescriptorKind.Schema,
                Name = "S1",
                State = DescriptorState.Active
            }
        };

        var relationships = Array.Empty<DescriptorPackageRelationshipEntry>();
        var evidenceHash = "evhash123";

        var hash1 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs1, relationships, evidenceHash);
        var hash2 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs2, relationships, evidenceHash);

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeContentHash_IgnoresContractHash()
    {
        var refs1 = new[]
        {
            new DescriptorManifestEntry
            {
                Ref = new DescriptorRef("schema", "s1", 1),
                Kind = DescriptorKind.Schema,
                Name = "S1",
                State = DescriptorState.Active,
                ContractHash = "old-contract-hash"
            }
        };

        var refs2 = new[]
        {
            new DescriptorManifestEntry
            {
                Ref = new DescriptorRef("schema", "s1", 1),
                Kind = DescriptorKind.Schema,
                Name = "S1",
                State = DescriptorState.Active,
                ContractHash = "new-contract-hash"
            }
        };

        var relationships = Array.Empty<DescriptorPackageRelationshipEntry>();
        var evidenceHash = "evhash123";

        var hash1 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs1, relationships, evidenceHash);
        var hash2 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs2, relationships, evidenceHash);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeContentHash_IncludesRelationships()
    {
        var entries = new[]
        {
            new DescriptorManifestEntry
            {
                Ref = new DescriptorRef("schema", "s1", 1),
                Kind = DescriptorKind.Schema,
                Name = "S1",
                State = DescriptorState.Active
            }
        };

        var rels = new[]
        {
            new DescriptorPackageRelationshipEntry
            {
                From = new DescriptorRef("schema", "s1", 1),
                To = new DescriptorRef("capability", "c1", 1),
                Kind = RelationshipKind.References,
                Strength = RelationshipStrength.Strong,
                IsRuntimeBinding = false
            }
        };

        var noRels = Array.Empty<DescriptorPackageRelationshipEntry>();
        var evidenceHash = "evhash123";

        var hashWith = DescriptorPackageHashComputer.ComputeContentHash("1.0", entries, rels, evidenceHash);
        var hashWithout = DescriptorPackageHashComputer.ComputeContentHash("1.0", entries, noRels, evidenceHash);

        hashWith.Should().NotBe(hashWithout);
    }

    [Fact]
    public void ComputeEvidenceHash_Deterministic()
    {
        var evidence = new DescriptorPackageEvidence
        {
            TopologyNodeCount = 5,
            TopologyEdgeCount = 10,
            HasTopologyErrors = false,
            MaxImpactSeverity = DescriptorImpactSeverity.Critical,
            AffectedDescriptorCount = 3,
            MaxCompatibilityLevel = DescriptorCompatibilityLevel.Breaking,
            BreakingFindingCount = 1,
            MaxLifecycleDecision = DescriptorLifecycleDecisionKind.ReviewRequired,
            RequiresReview = true
        };

        var hash1 = DescriptorPackageHashComputer.ComputeEvidenceHash(evidence);
        var hash2 = DescriptorPackageHashComputer.ComputeEvidenceHash(evidence);

        hash1.Should().Be(hash2);
        hash1.Should().HaveLength(64);
    }
}
