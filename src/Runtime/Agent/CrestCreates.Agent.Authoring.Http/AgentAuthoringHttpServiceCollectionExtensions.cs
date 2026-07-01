using CrestCreates.Agent.Authoring.Abstractions.Model;
using CrestCreates.Agent.Authoring.Http.Credentials;
using CrestCreates.Agent.Authoring.Http.OpenAICompatible;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CrestCreates.Agent.Authoring.Http;

public static class AgentAuthoringHttpServiceCollectionExtensions
{
    public static IServiceCollection AddOpenAICompatibleAuthoringProvider(
        this IServiceCollection services,
        string providerName = "openai-compatible",
        string? credentialReference = null,
        Uri? endpoint = null)
    {
        services.TryAddSingleton<IDescriptorAuthoringCredentialProvider, DefaultDescriptorAuthoringCredentialProvider>();

        services.AddHttpClient<IDescriptorAuthoringModelClient, OpenAICompatibleDescriptorAuthoringModelClient>();

        services.AddSingleton(Options.Create(new DescriptorAuthoringProviderProfile
        {
            ProviderName = providerName,
            CredentialReference = credentialReference ?? "Authoring:Llm:ApiKey",
            Endpoint = endpoint
        }));

        return services;
    }
}
