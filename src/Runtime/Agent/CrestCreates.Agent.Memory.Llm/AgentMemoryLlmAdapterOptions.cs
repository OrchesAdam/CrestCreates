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
}

public static class AgentMemoryLlmContractVersions
{
    public static AgentPromptTemplateId CompressionTemplateId => new("agent-memory.compression.default");
    public static AgentPromptVersion CompressionTemplateVersion => new("7gplus.v1");
    public static AgentPromptTemplateId ExtractionTemplateId => new("agent-memory.extraction.default");
    public static AgentPromptVersion ExtractionTemplateVersion => new("7gplus.v1");
    public static AgentPromptContractVersion PromptContractVersion => new("agent-memory-llm.v1");
    public static AgentPromptModelProfileRef DefaultModelProfileRef => new("agent-memory-llm.default");
    public static AgentPromptProviderProfileRef DefaultProviderProfileRef => new("recorded");
}
