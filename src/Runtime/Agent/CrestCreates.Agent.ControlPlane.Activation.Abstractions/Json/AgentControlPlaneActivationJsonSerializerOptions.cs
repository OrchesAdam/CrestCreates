using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Json;

/// <summary>
/// Source-generated options for the complete Control Plane tool surface, including activation contracts.
/// </summary>
public static class AgentControlPlaneActivationJsonSerializerOptions
{
    public static JsonSerializerOptions CreateDefault()
    {
        return new JsonSerializerOptions
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                AgentControlPlaneActivationJsonSerializerContext.Default)
        };
    }
}
