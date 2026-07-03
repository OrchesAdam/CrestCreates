using System.Collections.Generic;
using System.Text.Json.Serialization;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Llm.Compression;
using CrestCreates.Agent.Memory.Llm.Extraction;
using CrestCreates.Agent.Memory.Llm.Model;
using CrestCreates.Agent.Memory.Llm.Prompting;

namespace CrestCreates.Agent.Memory.Llm.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AgentMemoryLlmModelRequest))]
[JsonSerializable(typeof(AgentMemoryLlmModelResponse))]
[JsonSerializable(typeof(AgentMemoryCompressionPromptInput))]
[JsonSerializable(typeof(AgentMemoryExtractionPromptInput))]
[JsonSerializable(typeof(AgentMemoryCompressionProviderOutputDto))]
[JsonSerializable(typeof(AgentMemoryExtractionProviderOutputDto))]
[JsonSerializable(typeof(AgentMemoryCompressedBlockDto))]
[JsonSerializable(typeof(AgentMemoryCandidateDto))]
[JsonSerializable(typeof(IReadOnlyList<AgentMemoryCompressionPromptSource>))]
[JsonSerializable(typeof(IReadOnlyList<AgentCompressedContextBlock>))]
[JsonSerializable(typeof(IReadOnlyList<AgentMemoryCandidate>))]
[JsonSerializable(typeof(IReadOnlyList<AgentMemoryCandidateDto>))]
[JsonSerializable(typeof(IReadOnlyList<AgentMemoryCompressedBlockDto>))]
[JsonSerializable(typeof(AgentMemoryLlmModelResponseEvidenceProjection))]
public sealed partial class AgentMemoryLlmJsonSerializerContext : JsonSerializerContext;
