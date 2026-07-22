using CrestCreates.Agent.Memory.Projection.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Agent.Memory.ReadCore;

public static class ReadCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers shared ReadCore implementations.
    /// Requires AddAgentMemoryProjectionSecurity() to have been called first
    /// (for security infrastructure) and Agent.Memory services to be registered
    /// (for IAgentMemoryRetriever, IAgentCompressedContextStore, IAgentContextSourceExpander).
    /// </summary>
    public static IServiceCollection AddAgentMemoryReadCore(
        this IServiceCollection services)
    {
        services.TryAddSingleton<IAgentMemoryReadCore, AgentMemoryReadCore>();
        services.TryAddSingleton<IAgentContextReadCore, AgentContextReadCore>();
        services.TryAddSingleton<IAgentMemorySourceExpandCore, AgentMemorySourceExpandCore>();

        return services;
    }
}
