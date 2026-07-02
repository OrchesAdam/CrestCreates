using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Agent.Prompting;

public static class AgentPromptingServiceCollectionExtensions
{
    public static IServiceCollection AddAgentPrompting(this IServiceCollection services)
    {
        services.TryAddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAgentPromptHashService, DefaultAgentPromptHashService>();
        services.TryAddSingleton<IAgentPromptEvidenceFactory, DefaultAgentPromptEvidenceFactory>();
        services.TryAddSingleton<IAgentPromptTemplateRegistry>(_ =>
            new InMemoryAgentPromptTemplateRegistry(Array.Empty<AgentPromptTemplateDescriptor>()));
        return services;
    }
}
