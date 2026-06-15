using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Samples.DescriptorControlPlane;

public sealed class CompanyCertificationGoldenScenarioHost
{
    public ServiceProvider Provider { get; }
    public InMemoryCompanyCertificationStore Store { get; }

    public CompanyCertificationGoldenScenarioHost()
    {
        Store = new InMemoryCompanyCertificationStore();
        var services = new ServiceCollection();

        // --- Store ---
        services.AddSingleton(Store);

        // --- Capability Registry ---
        var capEngine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var capRegistry = new CapabilityRegistry(capEngine);
        capRegistry.Build([
            new InlineDescriptorProvider<CapabilityDescriptor>(
                CompanyCertificationDescriptors.SubmitCompanyCertification,
                CompanyCertificationDescriptors.ApproveCompanyCertification,
                CompanyCertificationDescriptors.RejectCompanyCertification)
        ]);
        services.AddSingleton<ICapabilityRegistry>(capRegistry);

        // --- Capability Handler Resolver ---
        var handlerResolver = new CapabilityHandlerResolver();
        handlerResolver.Register("cap_submit_company_certification",
            new SubmitCompanyCertificationInvoker(Store));
        handlerResolver.Register("cap_approve_company_certification",
            new ApproveCompanyCertificationInvoker(Store));
        handlerResolver.Register("cap_reject_company_certification",
            new RejectCompanyCertificationInvoker(Store));
        services.AddSingleton<ICapabilityHandlerResolver>(handlerResolver);

        // --- HumanTask Registry ---
        var htEngine = new RegistryValidationEngine<HumanTaskDescriptor>([]);
        var htRegistry = new HumanTaskRegistry(htEngine);
        htRegistry.Build([
            new InlineDescriptorProvider<HumanTaskDescriptor>(
                CompanyCertificationDescriptors.ReviewCompanyCertification)
        ]);
        services.AddSingleton<IHumanTaskRegistry>(htRegistry);

        // --- Workflow Registry ---
        var wfEngine = new RegistryValidationEngine<WorkflowDescriptor>([]);
        var wfRegistry = new WorkflowRegistry(wfEngine);
        wfRegistry.Build([
            new InlineDescriptorProvider<WorkflowDescriptor>(
                CompanyCertificationDescriptors.CompanyCertificationWorkflow)
        ]);
        services.AddSingleton<IWorkflowRegistry>(wfRegistry);

        // --- Event Validator (no-op) ---
        services.AddSingleton<IEventValidator, NoOpEventValidator>();

        // --- Local Event Bus ---
        services.AddSingleton<LocalEventBusOptions>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();

        // --- Runtime Services ---
        // Register a minimal capability pipeline with only event publishing.
        // Full middleware (auth, validation, etc.) is not needed for golden scenario.
        services.AddCapabilityPipeline(builder =>
        {
            builder.Clear();
            builder.Use<EventPublishingMiddleware>();
        });
        services.AddCapabilityRuntime();
        services.AddHumanTaskRuntime();
        services.AddWorkflowEngine();

        // --- Control Plane Services ---
        services.AddRelationshipKernel();
        services.AddTopologyKernel();
        services.AddDescriptorImpactAnalysis();
        services.AddDescriptorCompatibilityAnalysis();
        services.AddDescriptorLifecycleGovernance();
        services.AddDescriptorPackaging();
        services.AddSingleton<CompanyCertificationControlPlaneRunner>();

        Provider = services.BuildServiceProvider(validateScopes: true);
    }

    public IServiceScope CreateScope() => Provider.CreateScope();

    public void Dispose() => Provider.Dispose();

    private sealed class NoOpEventValidator : IEventValidator
    {
        public void ValidateOrThrow(string eventName, object? payload) { }
        public ValidationResult Validate(string eventName, object? payload) =>
            new(true, EventValidationError.None, null);
    }

    private sealed class InlineDescriptorProvider<T> : IDescriptorProvider<T>
        where T : IDescriptor
    {
        private readonly IReadOnlyList<T> _descriptors;
        public InlineDescriptorProvider(params T[] descriptors) => _descriptors = descriptors;
        public IReadOnlyList<T> GetDescriptors() => _descriptors;
    }
}
