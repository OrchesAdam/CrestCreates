using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Prompting.Abstractions;

namespace CrestCreates.Agent.Memory.Llm;

public sealed class AgentMemoryLlmAdapterOptions
{
    public bool EnableDeterministicFallback { get; set; } = true;
    public int MaxCompressedBlockCount { get; set; } = 32;
    public int MaxCompressedBlockCharacters { get; set; } = 4_000;
    public int MaxCandidateCount { get; set; } = 16;
    public int MaxCandidateCharacters { get; set; } = 2_000;
    public AgentMemoryConfidence MaxCandidateConfidence { get; set; } = AgentMemoryConfidence.Medium;
    public AgentPromptTemplateId CompressionTemplateId { get; set; } = new("agent-memory.compression.default");
    public AgentPromptVersion CompressionTemplateVersion { get; set; } = new("7gplus.v1");
    public AgentPromptTemplateId ExtractionTemplateId { get; set; } = new("agent-memory.extraction.default");
    public AgentPromptVersion ExtractionTemplateVersion { get; set; } = new("7gplus.v1");
    public AgentPromptContractVersion PromptContractVersion { get; set; } = new("agent-memory-llm.v1");
    public AgentPromptModelProfileRef ModelProfileRef { get; set; } = new("agent-memory-llm.default");
    public AgentPromptProviderProfileRef ProviderProfileRef { get; set; } = new("recorded");
}
