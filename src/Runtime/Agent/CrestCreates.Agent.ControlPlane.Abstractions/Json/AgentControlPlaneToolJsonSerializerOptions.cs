using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Json;

/// <summary>
/// Source-generated options for read, draft, review, and package-preview tools.
/// Activation consumers use AgentControlPlaneActivationJsonSerializerOptions.
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
}
