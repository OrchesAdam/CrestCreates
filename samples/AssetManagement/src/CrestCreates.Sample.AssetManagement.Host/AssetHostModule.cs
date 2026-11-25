using System.Text.Json;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Accountability.InMemory;
using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Authorization;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.AuditLogging.Abstractions.MethodAccountability;
using CrestCreates.AuditLogging.Interceptors;
using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using CrestCreates.Domain.Shared.Permissions;
using CrestCreates.DynamicApi;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.Form;
using CrestCreates.Form.Abstractions;
using CrestCreates.Generated.HttpProjection;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Infrastructure.Permission;
using CrestCreates.Mcp;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Metadata.Bootstrap;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using CrestCreates.Modularity;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.OpenApi;
using CrestCreates.Runtime.Delivery;
using CrestCreates.Runtime.Persistence;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Sample.AssetManagement.Application;
using CrestCreates.Sample.AssetManagement.Application.Handlers;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Sample.AssetManagement.Contracts.Json;
using CrestCreates.Sample.AssetManagement.Host.ApprovedReleases;
using CrestCreates.Sample.AssetManagement.Host.Json;
using CrestCreates.Sample.AssetManagement.Host.Projections;
using CrestCreates.Sample.AssetManagement.Host.ProductionRelease;
using CrestCreates.Sample.AssetManagement.Persistence;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Sample.AssetManagement.Host;

/// <summary>
/// Framework contributor contract for the AssetManagement golden host: every service registration and
/// post-build initialization lives here, so the entry point keeps only arguments, activation and the
/// HTTP pipeline. The host owns its catalog, identity, persistence and golden-scenario choices; the
/// framework composition helpers own the generic registration semantics.
/// </summary>
internal sealed class AssetHostModule : ModuleBase
{
    private readonly ConfigurationManager _configuration;
    private readonly bool _goldenScenario;
    private readonly bool _candidateGoldenScenario;
    private readonly bool _evolutionScenario;
    private readonly SqliteAssetStore _assetStore;

    public AssetHostModule(
        ConfigurationManager configuration,
        bool goldenScenario,
        bool candidateGoldenScenario,
        bool evolutionScenario)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _goldenScenario = goldenScenario;
        _candidateGoldenScenario = candidateGoldenScenario;
        _evolutionScenario = evolutionScenario || configuration.GetValue<bool>(AssetProductionEvolutionOperator.EnabledSetting);

        var databasePath = configuration["AssetManagement:DatabasePath"]
            ?? Path.Combine(Path.GetTempPath(), $"crestcreates-assets-{Environment.ProcessId}-{Guid.NewGuid():N}.db");
        configuration["AssetManagement:DatabasePath"] = databasePath;
        _assetStore = new SqliteAssetStore($"Data Source={databasePath}");
    }

    public override void OnConfigureServices(IServiceCollection services)
    {
        var runtimeConnectionString = _configuration["AssetManagement:RuntimeConnectionString"]
            ?? Environment.GetEnvironmentVariable("ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(runtimeConnectionString))
            throw new InvalidOperationException("AssetManagement:RuntimeConnectionString is required; the golden application has no in-memory Runtime fallback.");
        var runtimeSchema = _configuration["AssetManagement:RuntimeSchema"]
            ?? Environment.GetEnvironmentVariable("ASSET_MANAGEMENT_RUNTIME_SCHEMA")
            ?? "crest_asset_runtime";

        var baseline = AssetApprovedRelease.GetBaseline();
        IDescriptor[] productionBaseline =
        [
            ..baseline,
            AssetDescriptorCatalog.MaintenanceInitialHumanTask,
            ProductionRelease.AssetProductionDescriptors.TrialTask,
            ProductionRelease.AssetProductionDescriptors.QualityTask,
            ProductionRelease.AssetProductionDescriptors.ReleaseInputSchema,
            ProductionRelease.AssetProductionDescriptors.ReleaseOutputSchema,
            ProductionRelease.AssetProductionDescriptors.ReleaseCapability
        ];
        var receipt = _configuration[AssetApprovedRelease.ReceiptSetting];
        if (receipt is not null && _candidateGoldenScenario)
            throw new InvalidOperationException("An Asset approved release cannot be used with the candidate golden scenario.");
        var release = receipt is null ? null : AssetApprovedRelease.Load(
            receipt, _configuration[AssetApprovedRelease.SigningKeySetting], baseline);
        var productionReceipt = _configuration[ProductionRelease.AssetProductionRelease.ReceiptSetting];
        if (productionReceipt is not null && _candidateGoldenScenario)
            throw new InvalidOperationException("An Asset production release cannot be combined with the candidate golden scenario.");
        var productionRelease = productionReceipt is null ? null : ProductionRelease.AssetProductionRelease.Load(
            productionReceipt, _configuration[ProductionRelease.AssetProductionRelease.SigningKeySetting], productionBaseline);
        IDescriptor[] inventory = productionRelease is not null
            ? release is not null
                ? [..productionBaseline, productionRelease.Workflow, release.Workflow]
                : [..productionBaseline, productionRelease.Workflow]
            : release is not null
                ? [..baseline, release.InitialTask, release.Workflow]
                : _candidateGoldenScenario
                    ? [..baseline, AssetDescriptorCatalog.MaintenanceInitialHumanTask, AssetCandidateDescriptorCatalog.MaintenanceWorkflow]
                    : baseline;

        var schemaRegistry = new SchemaRegistry(new RegistryValidationEngine<SchemaDescriptor>([]));
        schemaRegistry.Build([new AssetDescriptorProvider<SchemaDescriptor>(inventory.OfType<SchemaDescriptor>().ToArray())]);
        var capabilityRegistry = new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>([]));
        var humanTaskRegistry = new HumanTaskRegistry(new RegistryValidationEngine<HumanTaskDescriptor>([]));
        humanTaskRegistry.Build([new AssetDescriptorProvider<HumanTaskDescriptor>(inventory.OfType<HumanTaskDescriptor>().ToArray())]);
        var workflowRegistry = new WorkflowRegistry(new RegistryValidationEngine<WorkflowDescriptor>([]));
        workflowRegistry.Build([new AssetDescriptorProvider<WorkflowDescriptor>(inventory.OfType<WorkflowDescriptor>().ToArray())]);
        var formRegistry = new FormRegistry(new RegistryValidationEngine<FormDescriptor>([]));
        formRegistry.Build([new AssetDescriptorProvider<FormDescriptor>(inventory.OfType<FormDescriptor>().ToArray())]);

        // Host policy choices own the framework defaults through the framework contributor contract.
        services.AddCapabilityInputValidationPolicy<AssetInputValidationPolicy>();
        services.AddCapabilityRuntime();
        services.AddDescriptorStableHash();
        services.AddMultiTenancy();
        services.AddInMemoryTenantProvider();
        services.AddDataFilterServices();
        services.AddCrestAuthorization();
        services.AddAccountability(options => options.RequireAtLeastOneSink = true);
        services.AddRuntimePersistence();
        services.AddSingleton<IRuntimeStateContractContributor, AssetRuntimeStateContractContributor>();
        services.AddCrestCreatesPostgreSqlRuntimePersistence(new PostgreSqlRuntimePersistenceOptions
        {
            ConnectionString = runtimeConnectionString,
            Schema = runtimeSchema,
            ApplyMigrations = true
        });
        services.AddRuntimeDelivery(options =>
        {
            if (_goldenScenario)
                options.PollingInterval = TimeSpan.FromMilliseconds(100);
        });
        services.AddTransient<AccountabilityHttpTerminalObserverMiddleware>();
        services.AddTransient<AccountabilityHttpOperationScopeMiddleware>();
        services.AddSingleton<InMemoryAuditSink>();
        services.AddSingleton<IAuditSink>(sp => sp.GetRequiredService<InMemoryAuditSink>());
        services.AddScoped<IAuditedMethodAccountabilityRuntime, AuditedMethodAccountabilityRuntime>();
        services.AddSingleton<ICapabilityHandlerModule>(new AssetCapabilityModule());
        services.AddSingleton<CrestCreates.Metadata.Abstractions.DescriptorCapability.ICapabilityHandlerRegistry>(new AssetCapabilityHandlerRegistry());
        services.AddSingleton<ICapabilityHandlerModule>(new ProductionRelease.AssetProductionCapabilityModule());
        services.AddAssetMes(_configuration);
        if (productionRelease is not null)
        {
            services.AddHostedService(sp => new AssetMesTrialEventWorker(
                sp.GetRequiredService<IHttpClientFactory>(), _configuration, sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<ILogger<AssetMesTrialEventWorker>>(), productionRelease.TenantId));
        }
        services.AddSingleton(new AssetProductionRunEvidence(productionRelease));
        services.AddSingleton<IAssetStore>(_assetStore);
        services.AddSingleton<CrestCreates.Domain.Repositories.Permission.IPermissionGrantRepository, AssetPermissionGrantRepository>();
        services.AddScoped<AssetApplicationService>();
        services.AddScoped<AssetCompatibilityProjection>();
        services.AddCrestCapabilityEndpoints();
        services.AddCrestCompatibilityProjection();
        services.AddGeneratedAppServiceHttp();
        services.AddCrestOpenApi();
        services.AddSingleton<ISchemaRegistry>(schemaRegistry);
        services.AddSingleton<ICapabilityRegistry>(capabilityRegistry);
        services.AddSingleton<IHumanTaskRegistry>(humanTaskRegistry);
        services.AddSingleton<IWorkflowRegistry>(workflowRegistry);
        services.AddSingleton<IFormRegistry>(formRegistry);
        services.AddFormKernel();
        services.AddHumanTaskRuntime();
        services.AddSingleton<AssetMaintenanceTaskContractResolver>();
        services.AddHumanTaskCompletionObligation(AssetContractIds.MaintenanceHumanTask, 1, AssetContractIds.MaintenanceDecisionConsumer);
        services.AddHumanTaskCompletionObligation(AssetContractIds.MaintenanceInitialHumanTask, 1, AssetContractIds.MaintenanceDecisionConsumer);
        services.AddHumanTaskCompletionObligation(ProductionRelease.AssetProductionDescriptors.TrialTaskId, 1, AssetContractIds.MaintenanceDecisionConsumer);
        services.AddHumanTaskCompletionObligation(ProductionRelease.AssetProductionDescriptors.QualityTaskId, 1, AssetContractIds.MaintenanceDecisionConsumer);
        services.AddOutboxRequiredConsumer<HumanTaskCompletedEvent, AssetMaintenanceDecisionConsumer>(AssetContractIds.MaintenanceDecisionConsumer);
        services.AddWorkflowEngine();
        services.AddScoped<IAssetMaintenanceWorkflowVersionSelector>(sp =>
            new AssetMaintenanceWorkflowVersionSelector(sp.GetRequiredService<IAssetStore>(), release, productionRelease));
        services.AddScoped<IAssetMaintenanceWorkflowStarter, AssetMaintenanceWorkflowService>();
        services.AddScoped<AssetMaintenanceWorkflowService>();
        services.AddScoped<ILocalEventBus, AssetLocalEventBus>();
        services.AddSingleton<IDescriptorLookup>(new AssetDescriptorLookup(inventory));
        services.AddSingleton<ISchemaValidator, SchemaValidator>();
        services.AddHttpContextAccessor();
        services.AddAuthentication(AssetAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, AssetAuthenticationHandler>(AssetAuthenticationHandler.SchemeName, _ => { });
        services.AddScoped<AssetExecutionIdentity>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<AssetExecutionIdentity>());
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<AssetExecutionIdentity>());
        services.AddScoped<IAgentExecutionContextAccessor>(sp => sp.GetRequiredService<AssetExecutionIdentity>());
        services.AddCrestMcpToolProjection(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.TypeInfoResolver = AssetJsonContext.Default;
        });
        services.AddCrestAgentTools(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.TypeInfoResolver = AssetJsonContext.Default;
        });
        services.AddSingleton<AssetAgentToolApprovalGate>();
        services.AddSingleton<IAgentToolApprovalGate>(sp => sp.GetRequiredService<AssetAgentToolApprovalGate>());
        services.AddCrestHttpJsonResolver(new AssetCombinedJsonResolver());
        if (_evolutionScenario)
            services.AddAssetProductionEvolutionOperator();
    }

    public override async Task OnApplicationInitializationAsync(IHost host)
    {
        var services = host.Services;

        // #129 S2.3: compose the host capability registry from its static catalog plus exactly the
        // generated contributions this host activated; mapping verifies the frozen result.
        services.BuildFromHostCatalog([..AssetDescriptorCatalog.Capabilities, ProductionRelease.AssetProductionDescriptors.ReleaseCapability]);

        await SeedAssetPermissionsAsync(services);
        await _assetStore.InitializeAsync();
    }

    private static async Task SeedAssetPermissionsAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IPermissionGrantManager>();
        var userPermissions = new[] { AssetPermissions.Assets.Read, AssetPermissions.Assets.Search };
        var managerPermissions = userPermissions.Concat(new[]
        {
            AssetPermissions.Assets.Register,
            AssetPermissions.Assets.Update,
            AssetPermissions.Assets.Assign,
            AssetPermissions.Assets.Return,
            AssetPermissions.Assets.Transfer,
            AssetPermissions.Assets.RequestMaintenance,
            AssetPermissions.Assets.CompleteMaintenance
        });
        foreach (var permission in userPermissions)
            await manager.GrantAsync(new PermissionGrantInfo { PermissionName = permission, ProviderType = PermissionGrantProviderType.Role, ProviderKey = "asset-user", Scope = PermissionGrantScope.Global });
        foreach (var permission in managerPermissions)
            await manager.GrantAsync(new PermissionGrantInfo { PermissionName = permission, ProviderType = PermissionGrantProviderType.Role, ProviderKey = "asset-manager", Scope = PermissionGrantScope.Global });
        foreach (var permission in userPermissions.Concat(new[] { AssetPermissions.Assets.CompleteMaintenance, AssetPermissions.Assets.ReleaseProduction }))
            await manager.GrantAsync(new PermissionGrantInfo { PermissionName = permission, ProviderType = PermissionGrantProviderType.Role, ProviderKey = ProductionRelease.AssetProductionDescriptors.QualityRole, Scope = PermissionGrantScope.Global });
        foreach (var permission in userPermissions)
            await manager.GrantAsync(new PermissionGrantInfo { PermissionName = permission, ProviderType = PermissionGrantProviderType.Role, ProviderKey = ProductionRelease.AssetProductionDescriptors.MesEvidenceRole, Scope = PermissionGrantScope.Global });
    }
}
