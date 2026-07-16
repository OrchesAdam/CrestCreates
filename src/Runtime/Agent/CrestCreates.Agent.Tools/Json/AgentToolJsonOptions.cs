using System.Text.Json;

namespace CrestCreates.Agent.Tools;

public sealed class AgentToolJsonOptions
{
    public JsonSerializerOptions SerializerOptions { get; } = new();
}
