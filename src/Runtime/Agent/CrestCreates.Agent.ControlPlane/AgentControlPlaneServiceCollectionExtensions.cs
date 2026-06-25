using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Activation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Agent.ControlPlane;

public static class AgentControlPlaneServiceCollectionExtensions
{
    /// <summary>
    /// Registers Agent Control Plane services with production-safe default authorization.
    /// Read-only/context tools are allowed by default; mutating and activation handoff
    /// tools require explicit permission grants via <see cref="AgentToolAuthorizationOptions"/>.
    /// For development/testing, use <see cref="AddAgentControlPlane(IServiceCollection, AgentToolAuthorizationOptions)"/>
    /// with <see cref="AgentToolAuthorizationOptions.DevelopmentDefaults"/>.
    /// </summary>
    public static IServiceCollection AddAgentControlPlane(this IServiceCollection services)
    {
        var options = AgentToolAuthorizationOptions.ProductionDefaults;
        services.TryAddSingleton<IAgentToolManifestProvider, StaticAgentToolManifestProvider>();
        services.TryAddSingleton(options);
        services.TryAddSingleton<IAgentToolAuthorizationService>(_ =>
            new DefaultAgentToolAuthorizationService(options));
        services.TryAddSingleton<IAgentToolInvocationAuditor, InMemoryAgentToolInvocationAuditor>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IDescriptorReviewReportBuilder, DefaultDescriptorReviewReportBuilder>();
        services.TryAddSingleton<IDescriptorReviewReportRenderer, DefaultDescriptorReviewReportRenderer>();
        services.TryAddSingleton<IDescriptorReviewMessageTemplateCatalog, DefaultDescriptorReviewMessageTemplateCatalog>();
        services.TryAddSingleton<IActivationBindingArtifactResolver, InMemoryActivationBindingArtifactResolver>();
        services.TryAddSingleton<IAgentControlPlaneToolService>(sp =>
            ActivatorUtilities.CreateInstance<DefaultAgentControlPlaneToolService>(sp, options));
        return services;
    }

    /// <summary>
    /// Registers Agent Control Plane services with the specified authorization options.
    /// The options are used by both the coarse authorization service and the
    /// descriptor kind visibility scope, ensuring a single policy truth source.
    /// </summary>
    public static IServiceCollection AddAgentControlPlane(
        this IServiceCollection services,
        AgentToolAuthorizationOptions options)
    {
        services.TryAddSingleton<IAgentToolManifestProvider, StaticAgentToolManifestProvider>();
        services.TryAddSingleton(options);
        services.TryAddSingleton<IAgentToolAuthorizationService>(_ =>
            new DefaultAgentToolAuthorizationService(options));
        services.TryAddSingleton<IAgentToolInvocationAuditor, InMemoryAgentToolInvocationAuditor>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IDescriptorReviewReportBuilder, DefaultDescriptorReviewReportBuilder>();
        services.TryAddSingleton<IDescriptorReviewReportRenderer, DefaultDescriptorReviewReportRenderer>();
        services.TryAddSingleton<IDescriptorReviewMessageTemplateCatalog, DefaultDescriptorReviewMessageTemplateCatalog>();
        services.TryAddSingleton<IActivationBindingArtifactResolver, InMemoryActivationBindingArtifactResolver>();
        services.TryAddSingleton<IAgentControlPlaneToolService>(sp =>
            ActivatorUtilities.CreateInstance<DefaultAgentControlPlaneToolService>(sp, options));
        return services;
    }

    /// <summary>
    /// Registers Agent Control Plane services with a legacy authorization policy.
    /// The policy is converted to equivalent <see cref="AgentToolAuthorizationOptions"/> internally.
    /// The converted options are registered as the single policy truth source for both
    /// the coarse authorization service and the descriptor kind visibility scope.
    /// Prefer using <see cref="AddAgentControlPlane(IServiceCollection, AgentToolAuthorizationOptions)"/> for new code.
    /// </summary>
    public static IServiceCollection AddAgentControlPlane(
        this IServiceCollection services,
        AgentToolAuthorizationPolicy policy)
    {
        // Convert legacy policy to options so both the authorization service
        // and the visibility scope share a single policy truth.
        var options = PolicyToOptions(policy);

        services.TryAddSingleton<IAgentToolManifestProvider, StaticAgentToolManifestProvider>();
        services.TryAddSingleton(options);
        services.TryAddSingleton<IAgentToolAuthorizationService>(_ =>
            new DefaultAgentToolAuthorizationService(options));
        services.TryAddSingleton<IAgentToolInvocationAuditor, InMemoryAgentToolInvocationAuditor>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IDescriptorReviewReportBuilder, DefaultDescriptorReviewReportBuilder>();
        services.TryAddSingleton<IDescriptorReviewReportRenderer, DefaultDescriptorReviewReportRenderer>();
        services.TryAddSingleton<IDescriptorReviewMessageTemplateCatalog, DefaultDescriptorReviewMessageTemplateCatalog>();
        services.TryAddSingleton<IActivationBindingArtifactResolver, InMemoryActivationBindingArtifactResolver>();
        services.TryAddSingleton<IAgentControlPlaneToolService>(sp =>
            ActivatorUtilities.CreateInstance<DefaultAgentControlPlaneToolService>(sp, options));
        return services;
    }

    /// <summary>
    /// Converts a legacy <see cref="AgentToolAuthorizationPolicy"/> to equivalent
    /// <see cref="AgentToolAuthorizationOptions"/>. Mirrors the conversion in
    /// <see cref="DefaultAgentToolAuthorizationService"/> to ensure a single policy truth.
    /// </summary>
    private static AgentToolAuthorizationOptions PolicyToOptions(AgentToolAuthorizationPolicy policy)
    {
        return new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.ExplicitPolicy,
            AllowReadOnlyToolsByDefault = true,
            AllowMutationToolsByDefault = false,
            AllowActivationHandoffToolsByDefault = false,
            DeniedPermissions = policy.DeniedPermissionNames,
            DeniedDescriptorKinds = policy.DeniedDescriptorKinds,
            DeniedToolNames = policy.DeniedToolNames,
            DeniedActorKinds = policy.DeniedActorKinds
        };
    }
}
