using System.Text.Json;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Llm.Compression;
using CrestCreates.Agent.Memory.Llm.Extraction;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Llm.Tests;

public sealed class PromptEvidenceTests
{
    [Fact]
    public void CompressionOutputHash_DoesNotChange_WhenProviderBlockIdChanges()
    {
        var projector = new AgentMemoryCompressionOutputProjector();
        var contentHash = Hash("content-hash-1");
        var sourceRef = new AgentContextSourceRef { SourceKind = AgentSourceKind.ConversationTurn, TenantId = "t1", SourceId = "s1" };

        var blocksA = new List<AgentCompressedContextBlock>
        {
            new() { BlockId = "provider-id-A", TenantId = "t1", Content = "same", CanonicalContentHash = contentHash, SourceRefs = [sourceRef] }
        };
        var blocksB = new List<AgentCompressedContextBlock>
        {
            new() { BlockId = "provider-id-B", TenantId = "t1", Content = "same", CanonicalContentHash = contentHash, SourceRefs = [sourceRef] }
        };

        var jsonA = ProjectToJson(projector, blocksA);
        var jsonB = ProjectToJson(projector, blocksB);

        jsonA.Should().Be(jsonB);
    }

    [Fact]
    public void CompressionOutputHash_DoesNotChange_WhenProviderOmitsBlockId()
    {
        var projector = new AgentMemoryCompressionOutputProjector();
        var contentHash = Hash("content-hash-2");
        var sourceRef = new AgentContextSourceRef { SourceKind = AgentSourceKind.ConversationTurn, TenantId = "t1", SourceId = "s1" };

        var blocksWithId = new List<AgentCompressedContextBlock>
        {
            new() { BlockId = "explicit-id", TenantId = "t1", Content = "same", CanonicalContentHash = contentHash, SourceRefs = [sourceRef] }
        };
        var blocksWithoutId = new List<AgentCompressedContextBlock>
        {
            new() { BlockId = Guid.NewGuid().ToString("N"), TenantId = "t1", Content = "same", CanonicalContentHash = contentHash, SourceRefs = [sourceRef] }
        };

        var jsonWithId = ProjectToJson(projector, blocksWithId);
        var jsonWithoutId = ProjectToJson(projector, blocksWithoutId);

        jsonWithId.Should().Be(jsonWithoutId);
    }

    [Fact]
    public void CandidateOutputHash_DoesNotChange_WhenProviderCandidateIdChanges()
    {
        var projector = new AgentMemoryExtractionOutputProjector();
        var contentHash = Hash("content-hash-3");
        var sourceRef = new AgentContextSourceRef { SourceKind = AgentSourceKind.ConversationTurn, TenantId = "t1", SourceId = "s1" };

        var candidatesA = new List<AgentMemoryCandidate>
        {
            new() { CandidateId = "provider-id-A", TenantId = "t1", Kind = AgentMemoryKind.ProjectFact, Content = "same", CanonicalContentHash = contentHash, Confidence = AgentMemoryConfidence.Medium, SourceRefs = [sourceRef], Status = AgentMemoryStatus.Candidate }
        };
        var candidatesB = new List<AgentMemoryCandidate>
        {
            new() { CandidateId = "provider-id-B", TenantId = "t1", Kind = AgentMemoryKind.ProjectFact, Content = "same", CanonicalContentHash = contentHash, Confidence = AgentMemoryConfidence.Medium, SourceRefs = [sourceRef], Status = AgentMemoryStatus.Candidate }
        };

        var jsonA = ProjectToJson(projector, candidatesA);
        var jsonB = ProjectToJson(projector, candidatesB);

        jsonA.Should().Be(jsonB);
    }

    [Fact]
    public void CandidateOutputHash_DoesNotChange_WhenProviderOmitsCandidateId()
    {
        var projector = new AgentMemoryExtractionOutputProjector();
        var contentHash = Hash("content-hash-4");
        var sourceRef = new AgentContextSourceRef { SourceKind = AgentSourceKind.ConversationTurn, TenantId = "t1", SourceId = "s1" };

        var candidatesWithId = new List<AgentMemoryCandidate>
        {
            new() { CandidateId = "explicit-id", TenantId = "t1", Kind = AgentMemoryKind.ProjectFact, Content = "same", CanonicalContentHash = contentHash, Confidence = AgentMemoryConfidence.Medium, SourceRefs = [sourceRef], Status = AgentMemoryStatus.Candidate }
        };
        var candidatesWithoutId = new List<AgentMemoryCandidate>
        {
            new() { CandidateId = Guid.NewGuid().ToString("N"), TenantId = "t1", Kind = AgentMemoryKind.ProjectFact, Content = "same", CanonicalContentHash = contentHash, Confidence = AgentMemoryConfidence.Medium, SourceRefs = [sourceRef], Status = AgentMemoryStatus.Candidate }
        };

        var jsonWithId = ProjectToJson(projector, candidatesWithId);
        var jsonWithoutId = ProjectToJson(projector, candidatesWithoutId);

        jsonWithId.Should().Be(jsonWithoutId);
    }

    private static CanonicalHash Hash(string value) => new()
    {
        Value = value,
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = CanonicalHashArtifactNames.AgentMemoryContent,
        Purpose = CanonicalHashPurposeNames.SourceIdentity,
        Scope = CanonicalHashScopeNames.InternalFull,
        ContractVersion = "test-v1",
        CanonicalShapeVersion = "test-shape-v1"
    };

    private static string ProjectToJson<T>(IAgentPromptCanonicalPayloadProjector<T> projector, T payload)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        projector.Write(writer, payload);
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
