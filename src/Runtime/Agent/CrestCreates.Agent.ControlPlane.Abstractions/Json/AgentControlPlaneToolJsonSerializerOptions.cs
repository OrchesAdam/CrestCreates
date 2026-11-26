using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Json;

/// <summary>
/// Options for read, draft, review, and package-preview tools. CreateDefault preserves
/// the legacy factory wire format; CreateContextAligned matches the generated context.
/// </summary>
public static class AgentControlPlaneToolJsonSerializerOptions
{
    public static JsonSerializerOptions CreateDefault()
    {
        return new JsonSerializerOptions
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                AgentControlPlaneToolJsonSerializerContext.Default)
        };
    }

    public static JsonSerializerOptions CreateContextAligned()
    {
        return new JsonSerializerOptions(AgentControlPlaneToolJsonSerializerContext.Default.Options);
    }
}
