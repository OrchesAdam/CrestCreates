using CrestCreates.Agent.Authoring.Authoring;
using CrestCreates.Agent.Authoring.Parsing;
using CrestCreates.Agent.Authoring.Prompting;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Agent.Authoring;

public static class AgentAuthoringServiceCollectionExtensions
{
    public static IServiceCollection AddDescriptorAuthoring(this IServiceCollection services)
    {
        services.TryAddSingleton<ICanonicalHashComputer, DefaultCanonicalHashComputer>();
        services.TryAddSingleton<IDescriptorAuthoringPromptInputHashService, DefaultDescriptorAuthoringPromptInputHashService>();
        services.TryAddSingleton<IDescriptorAuthoringPromptInputFactory, DefaultDescriptorAuthoringPromptInputFactory>();
        services.TryAddSingleton<IDescriptorAuthoringPromptBuilder, DefaultDescriptorAuthoringPromptBuilder>();
        services.TryAddSingleton<IDescriptorAuthoringOutputParser, JsonDescriptorAuthoringOutputParser>();

        // TimeProvider for deterministic time in the authoring pipeline
        services.TryAddSingleton(TimeProvider.System);

        // Options for authoring agent configuration
        services.AddOptions<LlmDescriptorAuthoringAgentOptions>();

        // Do NOT register IDescriptorAuthoringModelClient here - it is provider-specific
        // Do NOT register IDescriptorAuthoringAgent here - it depends on model client which is provider-specific
        return services;
    }
}
