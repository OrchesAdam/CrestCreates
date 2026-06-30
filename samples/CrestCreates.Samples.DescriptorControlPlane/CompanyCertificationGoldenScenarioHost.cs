using CrestCreates.Agent.ControlPlane;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Activation;
using CrestCreates.Agent.Memory;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using CrestCreates.DescriptorDraft;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.ContextPack;
using CrestCreates.Samples.DescriptorControlPlane.Authoring;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Samples.DescriptorControlPlane;

public sealed class CompanyCertificationGoldenScenarioHost : IDisposable
{
    public ServiceProvider Provider { get; }
    public InMemoryCompanyCertificationStore Store { get; }

    public CompanyCertificationGoldenScenarioHost(
        IReadOnlyList<IDescriptor>? runtimeInventory = null,
        InMemoryCompanyCertificationStore? store = null)
    {
        Store = store ?? new InMemoryCompanyCertificationStore();
        var services = new ServiceCollection();

        // ── Store ──────────────────────────────────────────────────────
        services.AddSingleton(Store);

        // ── Logging ────────────────────────────────────────────────────
        services.AddLogging();

        var inventory = runtimeInventory
            ?? CompanyCertificationDescriptorCloner.CopyAllDescriptors();

        RegisterRuntimeRegistries(services, inventory, Store);
        RegisterRuntimeServices(services);
        RegisterControlPlaneServices(services);

        Provider = services.BuildServiceProvider(validateScopes: true);
    }

    // ── Runtime Registries ─────────────────────────────────────────────

    private static void RegisterRuntimeRegistries(
        IServiceCollection services,
        IReadOnlyList<IDescriptor> inventory,
        InMemoryCompanyCertificationStore store)
    {
        // ── Capability Registry ────────────────────────────────────────
        var capabilities = inventory.OfType<CapabilityDescriptor>().ToArray();
        var capEngine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var capRegistry = new CapabilityRegistry(capEngine);
        capRegistry.Build([new InlineDescriptorProvider<CapabilityDescriptor>(capabilities)]);
        services.AddSingleton<ICapabilityRegistry>(capRegistry);

        // ── Capability Handler Resolver ────────────────────────────────
        var handlerResolver = new CapabilityHandlerResolver();
        handlerResolver.Register("cap_submit_company_certification",
            new SubmitCompanyCertificationInvoker(store));
        handlerResolver.Register("cap_approve_company_certification",
            new ApproveCompanyCertificationInvoker(store));
        handlerResolver.Register("cap_reject_company_certification",
            new RejectCompanyCertificationInvoker(store));
        services.AddSingleton<ICapabilityHandlerResolver>(handlerResolver);

        // ── HumanTask Registry ─────────────────────────────────────────
        var humanTasks = inventory.OfType<HumanTaskDescriptor>().ToArray();
        var htEngine = new RegistryValidationEngine<HumanTaskDescriptor>([]);
        var htRegistry = new HumanTaskRegistry(htEngine);
        htRegistry.Build([new InlineDescriptorProvider<HumanTaskDescriptor>(humanTasks)]);
        services.AddSingleton<IHumanTaskRegistry>(htRegistry);

        // ── Workflow Registry ──────────────────────────────────────────
        var workflows = inventory.OfType<WorkflowDescriptor>().ToArray();
        var wfEngine = new RegistryValidationEngine<WorkflowDescriptor>([]);
        var wfRegistry = new WorkflowRegistry(wfEngine);
        wfRegistry.Build([new InlineDescriptorProvider<WorkflowDescriptor>(workflows)]);
        services.AddSingleton<IWorkflowRegistry>(wfRegistry);
    }

    // ── Runtime Services ───────────────────────────────────────────────

    private static void RegisterRuntimeServices(IServiceCollection services)
    {
        // ── Event Validator (no-op) ────────────────────────────────────
        services.AddSingleton<IEventValidator, NoOpEventValidator>();

        // ── Local Event Bus ────────────────────────────────────────────
        services.AddSingleton<LocalEventBusOptions>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();

        // ── Capability Pipeline & Runtime ──────────────────────────────
        services.AddCapabilityPipeline(builder =>
        {
            builder.Clear();
            builder.Use<EventPublishingMiddleware>();
        });
        services.AddCapabilityRuntime();

        // ── HumanTask Runtime ──────────────────────────────────────────
        services.AddHumanTaskRuntime();

        // ── Workflow Engine ────────────────────────────────────────────
        services.AddWorkflowEngine();
    }

    // ── Control Plane Services ─────────────────────────────────────────

    private static void RegisterControlPlaneServices(IServiceCollection services)
    {
        services.AddRelationshipKernel();
        services.AddTopologyKernel();
        services.AddMetadataContextPack();
        services.AddDescriptorImpactAnalysis();
        services.AddDescriptorCompatibilityAnalysis();
        services.AddDescriptorLifecycleGovernance();
        services.AddDescriptorPackaging();

        // ── Descriptor Drafts ──────────────────────────────────────────────
        services.AddDescriptorStableHash();
        services.AddDescriptorDrafts();

        // ── Agent Memory ───────────────────────────────────────────────────
        services.AddAgentMemoryRuntime();

        // ── Agent Control Plane ────────────────────────────────────────────
        services.AddAgentControlPlane(AgentToolAuthorizationOptions.DevelopmentDefaults);

        // ── Activation Services ────────────────────────────────────────────
        services.TryAddSingleton<IDescriptorActivationPolicyProvider, DefaultDescriptorActivationPolicyProvider>();
        services.TryAddSingleton<IDescriptorActivationAuditor, InMemoryDescriptorActivationAuditor>();
        services.TryAddSingleton<IRuntimeActivationGate, InMemoryRuntimeActivationGate>();
        services.TryAddSingleton<IActivationEvidenceRechecker, DefaultActivationEvidenceRechecker>();
        services.TryAddSingleton<IDescriptorActivationRequestService, DefaultDescriptorActivationRequestService>();
        services.TryAddSingleton<IActivationReviewOrchestrator, DefaultActivationReviewOrchestrator>();

        // ── Phase 7f Authoring ─────────────────────────────────────────────
        services.TryAddSingleton<IDescriptorAuthoringAgent, FakeCompanyCertificationAuthoringAgent>();
        services.TryAddSingleton<CompanyCertificationAuthoringGoldenScenarioRunner>();
        services.TryAddSingleton<ActivationBindingReferenceRegistry>();

        services.AddSingleton<CompanyCertificationControlPlaneRunner>();
    }

    // ── Scope / Dispose ────────────────────────────────────────────────

    public IServiceScope CreateScope() => Provider.CreateScope();

    public void Dispose() => Provider.Dispose();

    // ── Nested types ───────────────────────────────────────────────────

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
