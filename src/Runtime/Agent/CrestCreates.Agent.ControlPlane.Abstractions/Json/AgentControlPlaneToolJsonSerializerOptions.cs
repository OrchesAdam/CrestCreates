using System.Text.Json;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Json;

/// <summary>
/// Source-generated options for read, draft, review, and package-preview tools.
/// Activation consumers use AgentControlPlaneActivationJsonSerializerOptions.
/// </summary>
public static class AgentControlPlaneToolJsonSerializerOptions
{
    public static JsonSerializerOptions CreateDefault()
    {
        return new JsonSerializerOptions(AgentControlPlaneToolJsonSerializerContext.Default.Options);
    }
}
