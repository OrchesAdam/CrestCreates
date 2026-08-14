using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Persistence;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Tests.Persistence;

public sealed class AgentMemoryPersistenceComparerTests
{
    private readonly DefaultAgentMemoryPersistenceComparer _comparer = new();

    [Fact]
    public void Equal_DeepSnapshots_Should_CompareEqual()
    {
        var baseline = Memory();
        var snapshot = baseline.Snapshot();

        _comparer.Equals(baseline, snapshot).Should().BeTrue();
        _comparer.Equals(snapshot, baseline).Should().BeTrue();
    }

    [Theory]
    [InlineData("tenant-b", nameof(AgentMemoryItem.TenantId))]
    [InlineData("memory-2", nameof(AgentMemoryItem.MemoryId))]
    public void AnyIdentityFieldDifference_Should_CompareUnequal(string value, string field)
    {
        var left = Memory();
        var right = field switch
        {
            nameof(AgentMemoryItem.TenantId) => left with { TenantId = value },
            nameof(AgentMemoryItem.MemoryId) => left with { MemoryId = value },
            _ => left
        };

        _comparer.Equals(left, right).Should().BeFalse($"{field} is part of the persisted identity.");
    }

    [Fact]
    public void KindDifference_Should_CompareUnequal()
        => _comparer.Equals(Memory(), Memory() with { Kind = AgentMemoryKind.Decision }).Should().BeFalse();

    [Fact]
    public void ContentDifference_Should_CompareUnequal()
        => _comparer.Equals(Memory(), Memory() with { Content = "other" }).Should().BeFalse();

    [Fact]
    public void CanonicalContentHashDifference_Should_CompareUnequal()
        => _comparer.Equals(Memory(), Memory() with { CanonicalContentHash = Hash("other") }).Should().BeFalse();

    [Fact]
    public void PromotedAtDifference_Should_CompareUnequal()
        => _comparer.Equals(Memory(), Memory() with { PromotedAt = DateTimeOffset.UnixEpoch.AddDays(1) }).Should().BeFalse();

    [Fact]
    public void ConfidenceDifference_Should_CompareUnequal()
        => _comparer.Equals(Memory(), Memory() with { Confidence = AgentMemoryConfidence.High }).Should().BeFalse();

    [Fact]
    public void StatusDifference_Should_CompareUnequal()
        => _comparer.Equals(Memory(), Memory() with { Status = AgentMemoryStatus.Superseded }).Should().BeFalse();

    [Fact]
    public void IsAuthoritativeDifference_Should_CompareUnequal()
        => _comparer.Equals(Memory(), Memory() with { IsAuthoritative = true }).Should().BeFalse();

    [Fact]
    public void TagsDifference_Should_CompareUnequal()
        => _comparer.Equals(Memory(), Memory() with { Tags = ["tag-1"] }).Should().BeFalse();

    [Fact]
    public void TagOrderDifference_Should_CompareUnequal()
    {
        var left = Memory() with { Tags = ["tag-a", "tag-b"] };
        var right = Memory() with { Tags = ["tag-b", "tag-a"] };

        _comparer.Equals(left, right).Should().BeFalse("collection order is part of the persisted snapshot.");
    }

    [Fact]
    public void DescriptorRefsDifference_Should_CompareUnequal()
        => _comparer.Equals(
            Memory(),
            Memory() with { DescriptorRefs = [new DescriptorRef("ns", "d-1")] }).Should().BeFalse();

    [Fact]
    public void SourceRefsDifference_Should_CompareUnequal()
        => _comparer.Equals(
            Memory(),
            Memory() with { SourceRefs = [new AgentContextSourceRef { SourceKind = AgentSourceKind.TaskRecord, TenantId = "tenant-a", SourceId = "s-1" }] }).Should().BeFalse();

    [Fact]
    public void SupersedesMemoryIdDifference_Should_CompareUnequal()
        => _comparer.Equals(Memory(), Memory() with { SupersedesMemoryId = "memory-old" }).Should().BeFalse();

    [Fact]
    public void SupersededByMemoryIdDifference_Should_CompareUnequal()
        => _comparer.Equals(Memory(), Memory() with { SupersededByMemoryId = "memory-new" }).Should().BeFalse();

    [Fact]
    public void RedactionKindsDifference_Should_CompareUnequal()
        => _comparer.Equals(Memory(), Memory() with { RedactionKinds = ["credential"] }).Should().BeFalse();

    [Fact]
    public void SanitizationDiagnosticsDifference_Should_CompareUnequal()
        => _comparer.Equals(
            Memory(),
            Memory() with
            {
                SanitizationDiagnostics =
                [
                    new AgentMemoryDiagnostic
                    {
                        Code = AgentMemoryDiagnosticCodes.ContentRedacted,
                        Message = "redacted",
                        Severity = SeverityLevel.Info
                    }
                ]
            }).Should().BeFalse();

    [Fact]
    public void NestedSourceRefDifference_Should_CompareUnequal()
    {
        var left = Memory() with
        {
            SourceRefs = [new AgentContextSourceRef { SourceKind = AgentSourceKind.TaskRecord, TenantId = "tenant-a", SourceId = "s-1" }]
        };
        var right = left with
        {
            SourceRefs = [new AgentContextSourceRef { SourceKind = AgentSourceKind.TaskRecord, TenantId = "tenant-a", SourceId = "s-2" }]
        };

        _comparer.Equals(left, right).Should().BeFalse("nested snapshot values participate in equality.");
    }

    private static AgentMemoryItem Memory()
        => new()
        {
            MemoryId = "memory-1",
            TenantId = "tenant-a",
            Kind = AgentMemoryKind.Preference,
            Content = "content",
            CanonicalContentHash = Hash("hash"),
            PromotedAt = DateTimeOffset.UnixEpoch,
            Confidence = AgentMemoryConfidence.Medium,
            Status = AgentMemoryStatus.Active,
            IsAuthoritative = false
        };

    private static Metadata.Abstractions.CanonicalHashing.CanonicalHash Hash(string value)
        => new()
        {
            Value = value,
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = "AgentMemoryTest",
            Scope = "InternalFull",
            Purpose = "Test",
            ContractVersion = "memory-hash-v1",
            CanonicalShapeVersion = "test-v1"
        };
}
