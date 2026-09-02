using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Activation;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using CrestCreates.Runtime.Delivery.Abstractions.Registration;
using CrestCreates.Localization.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestCreates.Agent.ControlPlane;

public static class AgentControlPlaneServiceCollectionExtensions
{
    /// <summary>
    /// Registers Agent Control Plane core services. Read-only/context tools are allowed by
    /// default; mutating and activation handoff tools require explicit permission grants via
    /// <see cref="AgentToolAuthorizationOptions"/>. InMemory stubs are NOT included — call
    /// <see cref="AddAgentControlPlaneInMemoryStubs"/> for development/testing, or register
    /// real implementations for production.
    /// </summary>
    public static IServiceCollection AddAgentControlPlane(this IServiceCollection services)
    {
        var options = AgentToolAuthorizationOptions.ProductionDefaults;
        services.TryAddSingleton<IAgentToolManifestProvider, StaticAgentToolManifestProvider>();
        services.TryAddSingleton(options);
        services.TryAddSingleton<IAgentToolAuthorizationService>(_ =>
            new DefaultAgentToolAuthorizationService(options));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IDescriptorReviewReportBuilder, DefaultDescriptorReviewReportBuilder>();
        services.TryAddSingleton<IDescriptorReviewReportRenderer, DefaultDescriptorReviewReportRenderer>();
        AddDescriptorReviewMessageTemplateCatalog(services);
        services.TryAddSingleton<ActivationBindingHashValidator>();
        services.TryAddSingleton<IDescriptorActivationPolicyProvider, DefaultDescriptorActivationPolicyProvider>();
        services.TryAddSingleton<IActivationEvidenceRechecker, DefaultActivationEvidenceRechecker>();
        services.TryAddSingleton<IDescriptorActivationRequestService, DefaultDescriptorActivationRequestService>();
        services.TryAddSingleton<IActivationReviewOrchestrator, DefaultActivationReviewOrchestrator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRuntimeStateContractContributor, DescriptorActivationRuntimeStateContractContributor>());
        AddActivationReviewConsumer(services);
        services.TryAddSingleton<IAgentControlPlaneToolService>(sp =>
            ActivatorUtilities.CreateInstance<DefaultAgentControlPlaneToolService>(sp, options));
        return services;
    }

    /// <summary>
    /// Registers Agent Control Plane core services with the specified authorization options.
    /// InMemory stubs are NOT included — call <see cref="AddAgentControlPlaneInMemoryStubs"/>
    /// for development/testing, or register real implementations for production.
    /// </summary>
    public static IServiceCollection AddAgentControlPlane(
        this IServiceCollection services,
        AgentToolAuthorizationOptions options)
    {
        services.TryAddSingleton<IAgentToolManifestProvider, StaticAgentToolManifestProvider>();
        services.TryAddSingleton(options);
        services.TryAddSingleton<IAgentToolAuthorizationService>(_ =>
            new DefaultAgentToolAuthorizationService(options));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IDescriptorReviewReportBuilder, DefaultDescriptorReviewReportBuilder>();
        services.TryAddSingleton<IDescriptorReviewReportRenderer, DefaultDescriptorReviewReportRenderer>();
        AddDescriptorReviewMessageTemplateCatalog(services);
        services.TryAddSingleton<ActivationBindingHashValidator>();
        services.TryAddSingleton<IDescriptorActivationPolicyProvider, DefaultDescriptorActivationPolicyProvider>();
        services.TryAddSingleton<IActivationEvidenceRechecker, DefaultActivationEvidenceRechecker>();
        services.TryAddSingleton<IDescriptorActivationRequestService, DefaultDescriptorActivationRequestService>();
        services.TryAddSingleton<IActivationReviewOrchestrator, DefaultActivationReviewOrchestrator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRuntimeStateContractContributor, DescriptorActivationRuntimeStateContractContributor>());
        AddActivationReviewConsumer(services);
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
    /// InMemory stubs are NOT included — call <see cref="AddAgentControlPlaneInMemoryStubs"/>
    /// for development/testing, or register real implementations for production.
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
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IDescriptorReviewReportBuilder, DefaultDescriptorReviewReportBuilder>();
        services.TryAddSingleton<IDescriptorReviewReportRenderer, DefaultDescriptorReviewReportRenderer>();
        AddDescriptorReviewMessageTemplateCatalog(services);
        services.TryAddSingleton<ActivationBindingHashValidator>();
        services.TryAddSingleton<IDescriptorActivationPolicyProvider, DefaultDescriptorActivationPolicyProvider>();
        services.TryAddSingleton<IActivationEvidenceRechecker, DefaultActivationEvidenceRechecker>();
        services.TryAddSingleton<IDescriptorActivationRequestService, DefaultDescriptorActivationRequestService>();
        services.TryAddSingleton<IActivationReviewOrchestrator, DefaultActivationReviewOrchestrator>();
        AddActivationReviewConsumer(services);
        services.TryAddSingleton<IAgentControlPlaneToolService>(sp =>
            ActivatorUtilities.CreateInstance<DefaultAgentControlPlaneToolService>(sp, options));
        return services;
    }

    /// <summary>
    /// Registers InMemory stub implementations for services that require persistent storage
    /// in production. These stubs are suitable for development and testing only — data is
    /// lost on process restart and the activation gate does not perform real runtime activation.
    /// </summary>
    /// <remarks>
    /// Call this after <see cref="AddAgentControlPlane"/> to override the default stubs
    /// with InMemory implementations. Production deployments should register real implementations
    /// for <see cref="IAgentToolInvocationAuditor"/>, <see cref="IActivationBindingArtifactResolver"/>,
    /// <see cref="IDescriptorActivationAuditor"/>, and <see cref="IRuntimeActivationGate"/>.
    /// </remarks>
    public static IServiceCollection AddAgentControlPlaneInMemoryStubs(this IServiceCollection services)
    {
        services.TryAddSingleton<IAgentToolInvocationAuditor, InMemoryAgentToolInvocationAuditor>();
        services.TryAddSingleton<IActivationBindingArtifactResolver, InMemoryActivationBindingArtifactResolver>();
        services.TryAddSingleton<IDescriptorActivationAuditor, InMemoryDescriptorActivationAuditor>();
        services.TryAddSingleton<IRuntimeActivationGate, InMemoryRuntimeActivationGate>();
        return services;
    }

    private static void AddActivationReviewConsumer(IServiceCollection services)
    {
        services.AddSingleton(new HumanTaskCompletionObligationPolicyRegistration(
            DescriptorActivationHumanTaskIds.ActivationReview.Value!,
            1,
            DescriptorActivationReviewHumanTaskEventHandler.ConsumerIdValue));
        services.TryAddSingleton<DescriptorActivationReviewHumanTaskEventHandler>();
        services.AddSingleton(new OutboxRequiredConsumerRegistration<HumanTaskCompletedEvent>(
            DescriptorActivationReviewHumanTaskEventHandler.ConsumerIdValue,
            sp => sp.GetRequiredService<DescriptorActivationReviewHumanTaskEventHandler>()));
        services.AddSingleton(new OutboxRequiredConsumerMetadata(DescriptorActivationReviewHumanTaskEventHandler.ConsumerIdValue));
        services.AddSingleton(new OutboxRequiredConsumerValidationRegistration(
            DescriptorActivationReviewHumanTaskEventHandler.ConsumerIdValue,
            sp => _ = sp.GetRequiredService<DescriptorActivationReviewHumanTaskEventHandler>()));
    }

    private static void AddDescriptorReviewMessageTemplateCatalog(IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorReviewMessageTemplateCatalog>(serviceProvider =>
            new DefaultDescriptorReviewMessageTemplateCatalog(
                serviceProvider.GetService<ILocalizationService>(),
                serviceProvider.GetService<ILogger<DefaultDescriptorReviewMessageTemplateCatalog>>()
                    ?? NullLogger<DefaultDescriptorReviewMessageTemplateCatalog>.Instance));
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
