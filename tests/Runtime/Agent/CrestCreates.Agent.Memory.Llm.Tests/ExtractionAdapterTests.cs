using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Llm.Clients;
using CrestCreates.Agent.Memory.Llm.Extraction;
using CrestCreates.Agent.Memory.Llm.Model;
using CrestCreates.Agent.Memory.Llm.Validation;
using CrestCreates.Agent.Memory.Llm.Compression;
using CrestCreates.Agent.Memory.Llm.Prompting;
using CrestCreates.Agent.Memory.Extraction;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Llm.Tests;

public class ExtractionAdapterTests
{
    [Fact]
    public async Task LlmExtractor_ValidResponse_ReturnsCandidates()
    {
        var client = new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
        {
            ResponseText = """{"candidates":[{"candidateId":"c1","kind":"ProjectFact","content":"Users prefer dark mode","confidence":"High","sourceRefIds":["b1"]}]}"""
        });
        var options = new AgentMemoryLlmAdapterOptions { MaxCandidateConfidence = AgentMemoryConfidence.High };
        var extractor = CreateExtractor(client, options);

        var result = await extractor.ExtractCandidatesAsync(TestCompressedContext());

        result.Should().HaveCount(1);
        result[0].Kind.Should().Be(AgentMemoryKind.ProjectFact);
        result[0].Confidence.Should().Be(AgentMemoryConfidence.High);
        result[0].Status.Should().Be(AgentMemoryStatus.Candidate);
    }

    [Fact]
    public async Task LlmExtractor_CandidateStatusAlwaysCandidate_EvenIfProviderOutputsActive()
    {
        var client = new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
        {
            ResponseText = """{"candidates":[{"candidateId":"c1","kind":"Decision","content":"Use PostgreSQL","confidence":"Medium","status":"Active","sourceRefIds":["b1"]}]}"""
        });
        var extractor = CreateExtractor(client);

        var result = await extractor.ExtractCandidatesAsync(TestCompressedContext());

        result.Should().NotBeEmpty();
        foreach (var candidate in result)
        {
            candidate.Status.Should().Be(AgentMemoryStatus.Candidate);
        }
    }

    [Fact]
    public async Task LlmExtractor_ParseFailure_FallsBackWithDiagnostic()
    {
        var client = new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
        {
            ResponseText = "not-json"
        });
        var extractor = CreateExtractor(client);

        var result = await extractor.ExtractCandidatesAsync(TestCompressedContext());

        result.Should().NotBeEmpty();
        result.Should().Contain(c => c.SanitizationDiagnostics.Any(d =>
            d.Code == AgentMemoryLlmDiagnosticCodes.FallbackToDeterministicExtractor));
    }

    [Fact]
    public async Task LlmExtractor_ProviderFailure_FallsBack()
    {
        var client = new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
        {
            FailureKind = AgentMemoryLlmProviderFailureKind.RateLimited,
            FailureDetail = "Rate limited"
        });
        var extractor = CreateExtractor(client);

        var result = await extractor.ExtractCandidatesAsync(TestCompressedContext());

        result.Should().NotBeEmpty();
        result.Should().Contain(c => c.SanitizationDiagnostics.Any(d =>
            d.Code == AgentMemoryLlmDiagnosticCodes.FallbackToDeterministicExtractor));
    }

    [Fact]
    public async Task LlmExtractor_UnknownKind_DefaultsToProjectFact()
    {
        var client = new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
        {
            ResponseText = """{"candidates":[{"candidateId":"c1","kind":"UnknownKind","content":"Some content","confidence":"Low","sourceRefIds":["b1"]}]}"""
        });
        var extractor = CreateExtractor(client);

        var result = await extractor.ExtractCandidatesAsync(TestCompressedContext());

        result.Should().HaveCount(1);
        result[0].Kind.Should().Be(AgentMemoryKind.ProjectFact);
    }

    [Fact]
    public async Task LlmExtractor_UnknownConfidence_DefaultsToUnknown()
    {
        var client = new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
        {
            ResponseText = """{"candidates":[{"candidateId":"c1","kind":"Preference","content":"Some content","confidence":"SuperHigh","sourceRefIds":["b1"]}]}"""
        });
        var extractor = CreateExtractor(client);

        var result = await extractor.ExtractCandidatesAsync(TestCompressedContext());

        result.Should().HaveCount(1);
        result[0].Confidence.Should().Be(AgentMemoryConfidence.Unknown);
    }

    [Fact]
    public async Task LlmExtractor_HighConfidenceCappedToMax_ByDefault()
    {
        var client = new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
        {
            ResponseText = """{"candidates":[{"candidateId":"c1","kind":"ProjectFact","content":"Some content","confidence":"High","sourceRefIds":["b1"]}]}"""
        });
        // Default MaxCandidateConfidence is Medium — High should be capped
        var extractor = CreateExtractor(client);

        var result = await extractor.ExtractCandidatesAsync(TestCompressedContext());

        result.Should().HaveCount(1);
        result[0].Confidence.Should().Be(AgentMemoryConfidence.Medium);
        result[0].SanitizationDiagnostics.Should().Contain(d =>
            d.Code == AgentMemoryLlmDiagnosticCodes.CandidateConfidenceCapped);
    }

    private static LlmAgentMemoryExtractor CreateExtractor(
        IAgentMemoryLlmModelClient client,
        AgentMemoryLlmAdapterOptions? options = null)
    {
        return AgentMemoryLlmTestData.CreateExtractor(client, options);
    }

    private static AgentCompressedContext TestCompressedContext()
    {
        return new AgentCompressedContext
        {
            ContextId = "ctx-1",
            TenantId = "tenant-1",
            Blocks = [
                new AgentCompressedContextBlock
                {
                    BlockId = "b1",
                    TenantId = "tenant-1",
                    Content = "Users prefer dark mode for the dashboard",
                    CanonicalContentHash = new CanonicalHash
                    {
                        Value = "test-hash",
                        Algorithm = "SHA-256",
                        AlgorithmVersion = "sha256-canonical-json-v1",
                        ArtifactKind = CanonicalHashArtifactNames.AgentMemoryContent,
                        Purpose = CanonicalHashPurposeNames.SourceIdentity,
                        Scope = CanonicalHashScopeNames.InternalFull,
                        ContractVersion = "memory-hash-v1",
                        CanonicalShapeVersion = "memory-content-hash-v1"
                    },
                    SourceRefs = [
                        new AgentContextSourceRef
                        {
                            SourceKind = AgentSourceKind.CompressedContextBlock,
                            TenantId = "tenant-1",
                            SourceId = "b1"
                        }
                    ]
                }
            ]
        };
    }
}
