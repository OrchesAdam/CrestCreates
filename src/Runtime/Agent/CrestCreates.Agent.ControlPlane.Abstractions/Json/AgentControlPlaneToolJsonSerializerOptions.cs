using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Json;

/// <summary>
/// Factory for JsonSerializerOptions pre-configured with the 7c.v1
/// source-generated contract. Adapters should use CreateDefault() to
/// obtain a ready-to-use options instance.
/// When upstream projects expose their own JsonContext, switch to
/// JsonTypeInfoResolver.Combine() to chain contexts.
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
