using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Accountability.InMemory;
using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.AuditLogging.Abstractions.MethodAccountability;
using CrestCreates.AuditLogging.Interceptors;
using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using CrestCreates.DynamicApi;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.Form;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Infrastructure.Permission;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.Bootstrap;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using CrestCreates.Mcp;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.OpenApi;
using CrestCreates.Runtime.Delivery;
using CrestCreates.Runtime.Persistence;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence.InMemory;
using CrestCreates.Sample.AssetManagement.Application;
using CrestCreates.Sample.AssetManagement.Application.Handlers;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Sample.AssetManagement.Contracts.Json;
using CrestCreates.Sample.AssetManagement.Host;
using CrestCreates.Sample.AssetManagement.Host.Json;
using CrestCreates.Sample.AssetManagement.Persistence;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection.Extensions;

var goldenScenario = args.Contains("--golden-scenario", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args);
if (goldenScenario)
    builder.WebHost.UseUrls("http://127.0.0.1:0");

var databasePath = builder.Configuration["AssetManagement:DatabasePath"]
    ?? Path.Combine(Path.GetTempPath(), $"crestcreates-assets-{Environment.ProcessId}-{Guid.NewGuid():N}.db");
var assetStore = new SqliteAssetStore($"Data Source={databasePath}");
var schemaRegistry = new SchemaRegistry(new RegistryValidationEngine<SchemaDescriptor>([]));
schemaRegistry.Build([new AssetDescriptorProvider<SchemaDescriptor>(AssetDescriptorCatalog.Schemas)]);
var capabilityRegistry = new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>([]));
capabilityRegistry.Build(
    DescriptorProviderRegistry.GetProviders<CapabilityDescriptor>()
        .Append(new AssetDescriptorProvider<CapabilityDescriptor>(AssetDescriptorCatalog.Capabilities)));
var humanTaskRegistry = new HumanTaskRegistry(new RegistryValidationEngine<HumanTaskDescriptor>([]));
humanTaskRegistry.Build([new AssetDescriptorProvider<HumanTaskDescriptor>([AssetDescriptorCatalog.MaintenanceHumanTask])]);
var workflowRegistry = new WorkflowRegistry(new RegistryValidationEngine<WorkflowDescriptor>([]));
workflowRegistry.Build([new AssetDescriptorProvider<WorkflowDescriptor>([AssetDescriptorCatalog.MaintenanceWorkflow])]);
var formRegistry = new FormRegistry(new RegistryValidationEngine<FormDescriptor>([]));
formRegistry.Build([new AssetDescriptorProvider<FormDescriptor>([AssetDescriptorCatalog.MaintenanceForm])]);

builder.Services.AddCapabilityRuntime();
builder.Services.AddDescriptorStableHash();
builder.Services.AddMultiTenancy();
builder.Services.AddDataFilterServices();
builder.Services.AddAccountability(options => options.RequireAtLeastOneSink = true);
builder.Services.AddRuntimePersistence();
builder.Services.AddSingleton<IRuntimeStateContractContributor, AssetRuntimeStateContractContributor>();
builder.Services.AddCrestCreatesInMemoryRuntimePersistence();
builder.Services.AddRuntimeDelivery(options =>
{
    if (goldenScenario)
        options.PollingInterval = TimeSpan.FromMilliseconds(100);
});
builder.Services.AddTransient<AccountabilityHttpTerminalObserverMiddleware>();
builder.Services.AddTransient<AccountabilityHttpOperationScopeMiddleware>();
builder.Services.AddSingleton<InMemoryAuditSink>();
builder.Services.AddSingleton<IAuditSink>(sp => sp.GetRequiredService<InMemoryAuditSink>());
builder.Services.AddScoped<IAuditedMethodAccountabilityRuntime, AuditedMethodAccountabilityRuntime>();
builder.Services.Replace(ServiceDescriptor.Singleton<ICapabilityInputValidationPolicy, AssetInputValidationPolicy>());
builder.Services.AddSingleton<ICapabilityHandlerModule>(new AssetCapabilityModule());
builder.Services.AddSingleton<IAssetStore>(assetStore);
builder.Services.AddScoped<AssetApplicationService>();
builder.Services.AddCrestCapabilityEndpoints();
builder.Services.AddCrestCompatibilityProjection();
builder.Services.AddCrestOpenApi();
builder.Services.AddSingleton<ISchemaRegistry>(schemaRegistry);
builder.Services.AddSingleton<ICapabilityRegistry>(capabilityRegistry);
builder.Services.AddSingleton<IHumanTaskRegistry>(humanTaskRegistry);
builder.Services.AddSingleton<IWorkflowRegistry>(workflowRegistry);
builder.Services.AddSingleton<IFormRegistry>(formRegistry);
builder.Services.AddFormKernel();
builder.Services.AddHumanTaskRuntime();
builder.Services.AddHumanTaskCompletionObligation(AssetContractIds.MaintenanceHumanTask, 1, AssetContractIds.MaintenanceDecisionConsumer);
builder.Services.AddOutboxRequiredConsumer<HumanTaskCompletedEvent, AssetMaintenanceDecisionConsumer>(AssetContractIds.MaintenanceDecisionConsumer);
// Keep concrete consumer activation explicit for the NativeAOT host. The
// generic registration above owns delivery metadata and resolution; this
// factory owns the concrete composition without runtime constructor discovery.
builder.Services.Replace(ServiceDescriptor.Scoped<AssetMaintenanceDecisionConsumer>(sp =>
    new AssetMaintenanceDecisionConsumer(
        sp.GetRequiredService<IHumanTaskInstanceStore>(),
        sp.GetRequiredService<IRuntimeStateContractRegistry>(),
        sp.GetRequiredService<ICapabilityDispatcher>(),
        sp.GetRequiredService<AssetExecutionIdentity>(),
        sp.GetRequiredService<ILogger<AssetMaintenanceDecisionConsumer>>())));
builder.Services.AddWorkflowEngine();
builder.Services.AddScoped<IAssetMaintenanceWorkflowStarter, AssetMaintenanceWorkflowService>();
builder.Services.AddScoped<AssetMaintenanceWorkflowService>();
builder.Services.AddScoped<ILocalEventBus, AssetLocalEventBus>();
builder.Services.AddSingleton<IDescriptorLookup>(new AssetDescriptorLookup(
    AssetDescriptorCatalog.Schemas.Cast<IDescriptor>()
        .Concat(capabilityRegistry.GetAll())
        .Append(AssetDescriptorCatalog.MaintenanceForm)
        .Append(AssetDescriptorCatalog.MaintenanceHumanTask)
        .Append(AssetDescriptorCatalog.MaintenanceWorkflow)));
builder.Services.AddSingleton<ISchemaValidator, SchemaValidator>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(AssetAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, AssetAuthenticationHandler>(AssetAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddScoped<AssetExecutionIdentity>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<AssetExecutionIdentity>());
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<AssetExecutionIdentity>());
builder.Services.AddScoped<IAgentExecutionContextAccessor>(sp => sp.GetRequiredService<AssetExecutionIdentity>());
builder.Services.AddScoped<IPermissionChecker, AssetPermissionChecker>();
builder.Services.AddCrestMcpToolProjection(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.TypeInfoResolver = AssetJsonContext.Default;
});
builder.Services.AddCrestAgentTools(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.TypeInfoResolver = AssetJsonContext.Default;
});
builder.Services.AddSingleton<AssetAgentToolApprovalGate>();
builder.Services.AddSingleton<IAgentToolApprovalGate>(sp => sp.GetRequiredService<AssetAgentToolApprovalGate>());
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.TypeInfoResolver = new AssetCombinedJsonResolver();
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});

var app = builder.Build();
await assetStore.InitializeAsync();
app.UseAccountabilityHttpTerminalObserver();
app.UseAuthentication();
app.UseAccountabilityHttpOperationScope();
app.MapCrestCapabilityEndpoints();
app.MapCrestOpenApi();

if (goldenScenario)
{
    await app.StartAsync();
    try { return await AssetGoldenScenario.RunAsync(app); }
    finally { await app.StopAsync(); }
}

await app.RunAsync();
return 0;

public sealed class AssetAgentToolApprovalGate : IAgentToolApprovalGate
{
    public ValueTask<AgentToolApprovalResult> EvaluateAndClaimAsync(AgentToolApprovalRequest request, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new AgentToolApprovalResult { Decision = AgentToolApprovalDecision.NotRequired, ClaimState = AgentToolApprovalEvidenceClaimState.NotApplicable, ReasonCode = "read_only" });
}

public partial class Program;
