using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Compression;
using CrestCreates.Agent.Memory.Extraction;
using CrestCreates.Agent.Memory.Llm.Clients;
using CrestCreates.Agent.Memory.Llm.Compression;
using CrestCreates.Agent.Memory.Llm.Extraction;
using CrestCreates.Agent.Memory.Llm.Model;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Llm.Tests;

public static class AgentMemoryLlmTestData
{
    public static AgentMemoryLlmAdapterOptions DefaultOptions => new();
    public static IAgentPromptEvidenceFactory DefaultTestEvidenceFactory => new TestPromptEvidenceFactory();

    public static LlmAgentContextCompressor Compressor(
        IAgentMemoryLlmModelClient? client = null,
        AgentMemoryLlmAdapterOptions? options = null)
    {
        var sanitizer = new PassThroughSanitizer();
        IAgentContextCompressor fallback = new DefaultAgentContextCompressor(sanitizer);
        var promptBuilder = new DefaultAgentMemoryCompressionPromptBuilder();
        var parser = new JsonAgentMemoryCompressionOutputParser();
        var evidenceFactory = new TestPromptEvidenceFactory();

        return new LlmAgentContextCompressor(
            sanitizer,
            fallback,
            promptBuilder,
            client ?? new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
            {
                ResponseText = """{"blocks":[{"blockId":"b1","content":"summary","sourceRefIds":["conv-1_turn-1"]}]}"""
            }),
            parser,
            evidenceFactory,
            options ?? DefaultOptions);
    }

    public static LlmAgentMemoryExtractor CreateExtractor(
        IAgentMemoryLlmModelClient? client = null,
        AgentMemoryLlmAdapterOptions? options = null)
    {
        var sanitizer = new PassThroughSanitizer();
        IAgentMemoryExtractor fallback = new DefaultAgentMemoryExtractor();
        var promptBuilder = new DefaultAgentMemoryExtractionPromptBuilder();
        var parser = new JsonAgentMemoryExtractionOutputParser();
        var evidenceFactory = new TestPromptEvidenceFactory();

        return new LlmAgentMemoryExtractor(
            sanitizer,
            fallback,
            promptBuilder,
            client ?? new FakeAgentMemoryLlmModelClient(new AgentMemoryLlmModelResponse
            {
                ResponseText = """{"candidates":[{"candidateId":"c1","kind":"ProjectFact","content":"Test","confidence":"Medium","sourceRefIds":["b1"]}]}"""
            }),
            parser,
            evidenceFactory,
            options ?? DefaultOptions);
    }

    public static AgentConversationRecord Conversation(
        string conversationId = "conv-1",
        string tenantId = "tenant-1",
        params string[] turnContents)
    {
        var turns = turnContents.Select((content, i) => new AgentConversationTurn
        {
            TurnId = $"turn-{i + 1}",
            TenantId = tenantId,
            Role = i % 2 == 0 ? AgentConversationRole.User : AgentConversationRole.Assistant,
            Content = content,
            SourceRefs = [new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.ConversationTurn,
                TenantId = tenantId,
                SourceId = $"{conversationId}_turn-{i + 1}"
            }]
        }).ToArray();

        return new AgentConversationRecord
        {
            ConversationId = conversationId,
            TenantId = tenantId,
            Turns = turns
        };
    }

    public static AgentConversationRecord ConversationWithSecret(string secret)
    {
        return Conversation("conv-1", "tenant-1", $"Hello {secret}", "World");
    }

    public static AgentTaskRecord Task(
        string taskId = "task-1",
        string tenantId = "tenant-1",
        string title = "Test Task",
        string? summary = "Test summary")
    {
        return new AgentTaskRecord
        {
            TaskId = taskId,
            TenantId = tenantId,
            Title = title,
            Summary = summary,
            Events = []
        };
    }

    private sealed class PassThroughSanitizer : IAgentMemoryContentSanitizer
    {
        public SanitizedAgentContent Sanitize(string tenantId, string content, IReadOnlyList<AgentContextSourceRef> sourceRefs)
        {
            return new SanitizedAgentContent
            {
                SanitizedContent = content,
                CanonicalContentHash = new CanonicalHash
                {
                    Value = "hash-" + content.GetHashCode().ToString("x"),
                    Algorithm = "SHA-256",
                    AlgorithmVersion = "sha256-canonical-json-v1",
                    ArtifactKind = CanonicalHashArtifactNames.AgentMemoryContent,
                    Purpose = CanonicalHashPurposeNames.SourceIdentity,
                    Scope = CanonicalHashScopeNames.InternalFull,
                    ContractVersion = "memory-hash-v1",
                    CanonicalShapeVersion = "memory-content-hash-v1"
                }
            };
        }
    }

    private sealed class TestPromptEvidenceFactory : IAgentPromptEvidenceFactory
    {
        public AgentPromptInputEvidence<TInput> CreateInputEvidence<TInput>(AgentPromptEvidenceCreationRequest<TInput> request)
        {
            return new AgentPromptInputEvidence<TInput>
            {
                TemplateId = request.TemplateId,
                TemplateVersion = request.TemplateVersion,
                Purpose = request.Purpose,
                ContractVersion = request.ContractVersion,
                ModelProfileRef = request.ModelProfileRef,
                ProviderProfileRef = request.ProviderProfileRef,
                Input = request.Payload,
                InputHash = new CanonicalHash
                {
                    Value = "test-input-hash",
                    Algorithm = "SHA-256",
                    AlgorithmVersion = "sha256-canonical-json-v1",
                    ArtifactKind = CanonicalHashArtifactNames.AgentPromptInputEvidence,
                    Purpose = CanonicalHashPurposeNames.SourceIdentity,
                    Scope = CanonicalHashScopeNames.InternalFull,
                    ContractVersion = "test-v1",
                    CanonicalShapeVersion = "test-shape-v1"
                },
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        public AgentPromptOutputEvidence<TOutput> CreateOutputEvidence<TOutput>(
            AgentPromptEvidenceCreationRequest<TOutput> request,
            CanonicalHash inputHash,
            AgentPromptProviderObservation? providerObservation = null,
            string? artifactKind = null,
            string? canonicalShapeVersion = null,
            string? purpose = null)
        {
            return new AgentPromptOutputEvidence<TOutput>
            {
                TemplateId = request.TemplateId,
                TemplateVersion = request.TemplateVersion,
                Purpose = request.Purpose,
                ContractVersion = request.ContractVersion,
                ModelProfileRef = request.ModelProfileRef,
                ProviderProfileRef = request.ProviderProfileRef,
                InputHash = inputHash,
                Output = request.Payload,
                OutputHash = new CanonicalHash
                {
                    Value = "test-output-hash",
                    Algorithm = "SHA-256",
                    AlgorithmVersion = "sha256-canonical-json-v1",
                    ArtifactKind = CanonicalHashArtifactNames.AgentPromptOutputEvidence,
                    Purpose = CanonicalHashPurposeNames.SourceIdentity,
                    Scope = CanonicalHashScopeNames.InternalFull,
                    ContractVersion = "test-v1",
                    CanonicalShapeVersion = "test-shape-v1"
                },
                CreatedAt = DateTimeOffset.UtcNow
            };
        }
    }
}
