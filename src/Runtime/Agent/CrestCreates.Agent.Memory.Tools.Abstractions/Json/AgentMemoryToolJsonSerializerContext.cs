using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;

namespace CrestCreates.Agent.Memory.Tools;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonContractSurface(typeof(AgentMemoryToolSpecifications))]
public partial class AgentMemoryToolJsonSerializerContext : JsonSerializerContext;
