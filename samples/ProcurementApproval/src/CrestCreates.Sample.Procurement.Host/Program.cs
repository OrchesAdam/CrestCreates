using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.DynamicApi;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.Form;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using CrestCreates.Mcp;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.OpenApi;
using CrestCreates.Sample.Procurement.Application;
using CrestCreates.Sample.Procurement.Application.Handlers;
using CrestCreates.Sample.Procurement.Contracts;
using CrestCreates.Sample.Procurement.Contracts.Json;
using CrestCreates.Sample.Procurement.Host.Json;
using CrestCreates.Sample.Procurement.Host.Projections;
using CrestCreates.Sample.Procurement.Host;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;

var goldenScenario = args.Contains("--golden-scenario", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args);
if (goldenScenario)
    builder.WebHost.UseUrls("http://127.0.0.1:0");

var store = new InMemoryProcurementRequestStore();
var schemaRegistry = new SchemaRegistry(new RegistryValidationEngine<SchemaDescriptor>([]));
schemaRegistry.Build([new ProcurementDescriptorProvider<SchemaDescriptor>(ProcurementDescriptorCatalog.Schemas)]);
var capabilityRegistry = new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>([]));
capabilityRegistry.Build(
    DescriptorProviderRegistry.GetProviders<CapabilityDescriptor>()
        .Append(new ProcurementDescriptorProvider<CapabilityDescriptor>(
            ProcurementDescriptorCatalog.NativeCapabilities)));
var humanTaskRegistry = new HumanTaskRegistry(new RegistryValidationEngine<HumanTaskDescriptor>([]));
humanTaskRegistry.Build([new ProcurementDescriptorProvider<HumanTaskDescriptor>([ProcurementDescriptorCatalog.ApprovalHumanTask])]);
var workflowRegistry = new WorkflowRegistry(new RegistryValidationEngine<WorkflowDescriptor>([]));
workflowRegistry.Build([new ProcurementDescriptorProvider<WorkflowDescriptor>([ProcurementDescriptorCatalog.ApprovalWorkflow])]);
var formRegistry = new FormRegistry(new RegistryValidationEngine<FormDescriptor>([]));
formRegistry.Build([new ProcurementDescriptorProvider<FormDescriptor>([ProcurementDescriptorCatalog.ApprovalForm])]);
var humanTaskInstanceStore = new InMemoryHumanTaskInstanceStore();
var workflowInstanceStore = new InMemoryWorkflowInstanceStore();

builder.Services.AddCapabilityRuntime();
builder.Services.AddInMemoryCapabilityAudit();
builder.Services.AddSingleton<ICapabilityHandlerModule>(new ProcurementCapabilityModule());
builder.Services.AddSingleton<InMemoryProcurementRequestStore>(store);
builder.Services.AddScoped<ProcurementApplicationService>();
builder.Services.AddCrestCapabilityEndpoints();
builder.Services.AddCrestCompatibilityProjection();
builder.Services.AddCrestOpenApi();
builder.Services.AddScoped<ProcurementAppService>();
builder.Services.AddScoped<ProcurementApprovalTaskService>();
builder.Services.AddScoped<IProcurementApprovalOrchestrator>(sp =>
    sp.GetRequiredService<ProcurementApprovalTaskService>());
builder.Services.AddSingleton<ProcurementDecisionReconciliationStore>();

builder.Services.AddSingleton<ISchemaRegistry>(schemaRegistry);
builder.Services.AddSingleton<ICapabilityRegistry>(capabilityRegistry);
builder.Services.AddSingleton<IHumanTaskRegistry>(humanTaskRegistry);
builder.Services.AddSingleton<IWorkflowRegistry>(workflowRegistry);
builder.Services.AddSingleton<IFormRegistry>(formRegistry);
builder.Services.AddSingleton(humanTaskInstanceStore);
builder.Services.AddSingleton<IHumanTaskInstanceStore>(humanTaskInstanceStore);
builder.Services.AddSingleton(workflowInstanceStore);
builder.Services.AddSingleton<IWorkflowInstanceStore>(workflowInstanceStore);
builder.Services.AddFormKernel();
builder.Services.AddHumanTaskRuntime();
builder.Services.AddScoped<ILocalEventHandler<HumanTaskCompletedEvent>, ProcurementHumanTaskDecisionHandler>();
builder.Services.AddWorkflowEngine();
builder.Services.AddScoped<ILocalEventBus, ProcurementLocalEventBus>();

builder.Services.AddSingleton<ICapabilityHandlerRegistry, ProcurementHandlerRegistry>();
builder.Services.AddSingleton<IDescriptorLookup>(new ProcurementDescriptorLookup(
    ProcurementDescriptorCatalog.Schemas.Cast<IDescriptor>()
        .Concat(ProcurementDescriptorCatalog.NativeCapabilities)
        .Append(ProcurementDescriptorCatalog.ApprovalForm)
        .Append(ProcurementDescriptorCatalog.ApprovalHumanTask)
        .Append(ProcurementDescriptorCatalog.ApprovalWorkflow)));
builder.Services.AddSingleton<ISchemaValidator, SchemaValidator>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SampleExecutionIdentity>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<SampleExecutionIdentity>());
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<SampleExecutionIdentity>());
builder.Services.AddScoped<IAgentExecutionContextAccessor>(sp => sp.GetRequiredService<SampleExecutionIdentity>());
builder.Services.AddScoped<IPermissionChecker, SamplePermissionChecker>();

builder.Services.AddCrestMcpToolProjection(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.TypeInfoResolver = ProcurementJsonContext.Default;
});
builder.Services.AddSingleton<DevelopmentInMemoryAgentToolInvocationGate>();
builder.Services.AddSingleton<IAgentToolInvocationGate>(sp =>
    sp.GetRequiredService<DevelopmentInMemoryAgentToolInvocationGate>());
builder.Services.AddSingleton<IAgentToolInvocationLeaseAbandoner>(sp =>
    sp.GetRequiredService<DevelopmentInMemoryAgentToolInvocationGate>());
builder.Services.AddSingleton<SampleAgentToolApprovalGate>();
builder.Services.AddSingleton<IAgentToolApprovalGate>(sp =>
    sp.GetRequiredService<SampleAgentToolApprovalGate>());
builder.Services.AddSingleton<SampleAgentToolBudgetGate>();
builder.Services.AddSingleton<IAgentToolBudgetGate>(sp =>
    sp.GetRequiredService<SampleAgentToolBudgetGate>());
builder.Services.AddSingleton<DevelopmentInMemoryAgentToolGovernanceAuditor>();
builder.Services.AddSingleton<IAgentToolGovernanceAuditor>(sp =>
    sp.GetRequiredService<DevelopmentInMemoryAgentToolGovernanceAuditor>());
builder.Services.AddCrestAgentTools(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.TypeInfoResolver = ProcurementJsonContext.Default;
});

builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.TypeInfoResolver = new ProcurementCombinedJsonResolver();
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});

var app = builder.Build();

app.MapCrestCapabilityEndpoints();
app.MapCrestOpenApi();

if (goldenScenario)
{
    await app.StartAsync();
    try
    {
        return await ProcurementGoldenScenario.RunAsync(app);
    }
    finally
    {
        await app.StopAsync();
    }
}

await app.RunAsync();
return 0;

public sealed class ProcurementHandlerRegistry : ICapabilityHandlerRegistry
{
    public IReadOnlyDictionary<string, Type> GetHandlerMappings() => new Dictionary<string, Type>
    {
        ["procurement.submit-request"] = typeof(SubmitProcurementRequestHandler),
        ["procurement.approve-request"] = typeof(ApproveProcurementRequestHandler),
        ["procurement.reject-request"] = typeof(RejectProcurementRequestHandler),
        ["procurement.request.apply-approval"] = typeof(ApplyApprovalDecisionHandler),
        ["procurement.request.apply-rejection"] = typeof(ApplyRejectionDecisionHandler),
        ["procurement.get-request"] = typeof(GetProcurementRequestHandler),
    };
}

public partial class Program;
