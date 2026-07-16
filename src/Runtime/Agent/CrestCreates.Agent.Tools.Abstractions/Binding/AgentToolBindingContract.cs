using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.Agent.Tools;

public sealed class AgentToolBindingContract
{
    public required string ToolDescriptorId { get; init; }

    public int ToolDescriptorVersion { get; init; }

    public Type? InputType { get; init; }

    public Type? OutputType { get; init; }

    public required Func<JsonElement, JsonTypeInfo?, CancellationToken, ValueTask<object?>> BindInputAsync { get; init; }

    public required Func<object?, JsonTypeInfo?, CancellationToken, ValueTask<JsonElement?>> SerializeOutputAsync { get; init; }
}
