using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.ControlPlane;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Activation;
using CrestCreates.Agent.Memory;
using CrestCreates.Accountability.Bootstrap;
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
using CrestCreates.Runtime.Persistence;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence.InMemory;
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
    public ICompanyCertificationStore Store { get; }
    public CompanyCertificationPersistenceOptions PersistenceOptions { get; }

    // ── Factory Methods ────────────────────────────────────────────────

    /// <summary>
    /// Creates a host with SQLite persistence. Default mode for demonstration.
    /// </summary>
    public static CompanyCertificationGoldenScenarioHost CreateSqlite(
        string databasePath,
        IReadOnlyList<IDescriptor>? runtimeInventory = null)
    {
        var options = new CompanyCertificationPersistenceOptions
        {
            Mode = CompanyCertificationPersistenceMode.Sqlite,
            DatabasePath = databasePath
        };
        return new CompanyCertificationGoldenScenarioHost(options, runtimeInventory);
    }

    /// <summary>
    /// Creates a host with in-memory persistence. Useful for fast, isolated unit-style tests.
    /// </summary>
    public static CompanyCertificationGoldenScenarioHost CreateInMemory(
        IReadOnlyList<IDescriptor>? runtimeInventory = null)
    {
        var options = new CompanyCertificationPersistenceOptions
        {
            Mode = CompanyCertificationPersistenceMode.InMemory
        };
        return new CompanyCertificationGoldenScenarioHost(options, runtimeInventory);
    }

    // ── Constructor ────────────────────────────────────────────────────

    private CompanyCertificationGoldenScenarioHost(
        CompanyCertificationPersistenceOptions persistenceOptions,
        IReadOnlyList<IDescriptor>? runtimeInventory = null)
    {
        PersistenceOptions = persistenceOptions;
        var services = new ServiceCollection();

        // ── Logging ────────────────────────────────────────────────────
        services.AddLogging();

        // ── Persistence ────────────────────────────────────────────────
        // SQLite owns only sample business data. Runtime state is explicitly
        // registered through the Full Semantic InMemory adapter in this sample.
        ICompanyCertificationStore companyStore;
        if (persistenceOptions.Mode == CompanyCertificationPersistenceMode.Sqlite)
        {
            var connectionFactory = new SqliteConnectionFactory(persistenceOptions);
            var initializer = new SqliteDatabaseInitializer(connectionFactory);
            initializer.Initialize();

            companyStore = new SqliteCompanyCertificationStore(connectionFactory);
            services.AddSingleton<ICompanyCertificationStore>(companyStore);
            services.AddSingleton(connectionFactory);
        }
        else
        {
            companyStore = new InMemoryCompanyCertificationStore();
            services.AddSingleton<ICompanyCertificationStore>(companyStore);
        }

        Store = companyStore;

        var inventory = runtimeInventory
            ?? CompanyCertificationDescriptorCloner.CopyAllDescriptors();

        RegisterRuntimeRegistries(services, inventory);
        services.AddRuntimePersistence();
        services.AddSingleton<IRuntimeStateContractContributor, CompanyCertificationRuntimeStateContributor>();
        services.AddCrestCreatesInMemoryRuntimePersistence();
        RegisterRuntimeServices(services);
        RegisterControlPlaneServices(services);

        Provider = services.BuildServiceProvider(validateScopes: true);

        // ── Wire up Capability Handler Resolver with DI-resolved invokers ──
        RegisterCapabilityHandlers(services, Provider);
    }

    // ── Runtime Registries ─────────────────────────────────────────────

    private static void RegisterRuntimeRegistries(
        IServiceCollection services,
        IReadOnlyList<IDescriptor> inventory)
    {
        // ── Capability Registry ────────────────────────────────────────
        var capabilities = inventory.OfType<CapabilityDescriptor>().ToArray();
        var capEngine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var capRegistry = new CapabilityRegistry(capEngine);
        capRegistry.Build([new InlineDescriptorProvider<CapabilityDescriptor>(capabilities)]);
        services.AddSingleton<ICapabilityRegistry>(capRegistry);

        // ── Capability Handler Resolver (empty — populated after SP build) ──
        var handlerResolver = new CapabilityHandlerResolver();
        services.AddSingleton<ICapabilityHandlerResolver>(handlerResolver);

        // ── Invokers (registered as singletons for DI resolution) ──
        services.AddSingleton<SubmitCompanyCertificationInvoker>();
        services.AddSingleton<ApproveCompanyCertificationInvoker>();
        services.AddSingleton<RejectCompanyCertificationInvoker>();

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

    private static void RegisterCapabilityHandlers(
        IServiceCollection services,
        ServiceProvider provider)
    {
        // Use the concrete type — Register() is not on the interface
        var handlerResolver = (CapabilityHandlerResolver)provider.GetRequiredService<ICapabilityHandlerResolver>();
        handlerResolver.Register("cap_submit_company_certification",
            provider.GetRequiredService<SubmitCompanyCertificationInvoker>());
        handlerResolver.Register("cap_approve_company_certification",
            provider.GetRequiredService<ApproveCompanyCertificationInvoker>());
        handlerResolver.Register("cap_reject_company_certification",
            provider.GetRequiredService<RejectCompanyCertificationInvoker>());
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
        services.AddAccountability();

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
        services.AddAgentControlPlane(AgentToolAuthorizationOptions.DevelopmentDefaults)
            .AddAgentControlPlaneInMemoryStubs();

        // ── Phase 7f Authoring ─────────────────────────────────────────────
        services.TryAddSingleton<CrestCreates.Agent.Authoring.Abstractions.Authoring.IDescriptorAuthoringAgent, FakeCompanyCertificationAuthoringAgent>();
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
