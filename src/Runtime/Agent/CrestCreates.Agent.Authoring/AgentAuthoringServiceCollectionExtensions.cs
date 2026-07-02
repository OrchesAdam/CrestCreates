using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Agent.Authoring.Authoring;
using CrestCreates.Agent.Authoring.Parsing;
using CrestCreates.Agent.Authoring.Prompting;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Agent.Authoring;

public static class AgentAuthoringServiceCollectionExtensions
{
    public static IServiceCollection AddDescriptorAuthoring(this IServiceCollection services)
    {
        // Note: AddAgentPrompting() must be called by the consumer (host/Platform) separately.
        // Authoring does not own the Prompting runtime lifecycle.

        #pragma warning disable CS0618 // Type or member is obsolete — compatibility registration
        services.TryAddSingleton<IDescriptorAuthoringPromptInputHashService, DefaultDescriptorAuthoringPromptInputHashService>();
        #pragma warning restore CS0618
        services.TryAddSingleton<IDescriptorAuthoringPromptInputFactory, DefaultDescriptorAuthoringPromptInputFactory>();
        services.TryAddSingleton<IDescriptorAuthoringPromptBuilder, DefaultDescriptorAuthoringPromptBuilder>();
        services.TryAddSingleton<IDescriptorAuthoringOutputParser, JsonDescriptorAuthoringOutputParser>();

        services.TryAddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringPromptInput>, DescriptorAuthoringPromptInputProjector>();
        services.TryAddSingleton<IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringModelResponseEvidenceProjection>, DescriptorAuthoringModelResponseEvidenceProjector>();

        // TimeProvider for deterministic time in the authoring pipeline
        services.TryAddSingleton(TimeProvider.System);

        // Options for authoring agent configuration
        services.AddOptions<LlmDescriptorAuthoringAgentOptions>();

        // Do NOT register IDescriptorAuthoringModelClient here - it is provider-specific
        // Do NOT register IDescriptorAuthoringAgent here - it depends on model client which is provider-specific
        return services;
    }
}
