using System.Text.Json;
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
using CrestCreates.Generated.HttpProjection;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Mcp;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
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
using CrestCreates.Runtime.Persistence.InMemory;
using CrestCreates.Sample.Procurement.Application;
using CrestCreates.Sample.Procurement.Application.Handlers;
using CrestCreates.Sample.Procurement.Contracts;
using CrestCreates.Sample.Procurement.Contracts.Json;
using CrestCreates.Sample.Procurement.Host.Json;
using CrestCreates.Sample.Procurement.Host.Projections;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Sample.Procurement.Host;

/// <summary>
/// Framework contributor contract for the Procurement golden host: every service registration and
/// post-build initialization lives here, so the entry point keeps only arguments, activation and the
/// HTTP pipeline. The host owns its catalog, identity and golden-scenario choices; the framework
/// composition helpers own the generic registration semantics.
/// </summary>
internal sealed class ProcurementHostModule : ModuleBase
{
    private readonly bool _goldenScenario;
    private readonly InMemoryProcurementRequestStore _store = new();

    public ProcurementHostModule(bool goldenScenario)
    {
        _goldenScenario = goldenScenario;
    }

    public override void OnConfigureServices(IServiceCollection services)
    {
        var schemaRegistry = new SchemaRegistry(new RegistryValidationEngine<SchemaDescriptor>([]));
        schemaRegistry.Build([new ProcurementDescriptorProvider<SchemaDescriptor>(ProcurementDescriptorCatalog.Schemas)]);
        var capabilityRegistry = new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>([]));
        var humanTaskRegistry = new HumanTaskRegistry(new RegistryValidationEngine<HumanTaskDescriptor>([]));
        humanTaskRegistry.Build([new ProcurementDescriptorProvider<HumanTaskDescriptor>([ProcurementDescriptorCatalog.ApprovalHumanTask])]);
        var workflowRegistry = new WorkflowRegistry(new RegistryValidationEngine<WorkflowDescriptor>([]));
        workflowRegistry.Build([new ProcurementDescriptorProvider<WorkflowDescriptor>([ProcurementDescriptorCatalog.ApprovalWorkflow])]);
        var formRegistry = new FormRegistry(new RegistryValidationEngine<FormDescriptor>([]));
        formRegistry.Build([new ProcurementDescriptorProvider<FormDescriptor>([ProcurementDescriptorCatalog.ApprovalForm])]);

        // Host policy choices own the framework defaults through the framework contributor contract.
        services.AddCapabilityInputValidationPolicy<ProcurementInputValidationPolicy>();
        services.AddCapabilityRuntime();
        services.AddDescriptorStableHash();
        services.AddMultiTenancy();
        services.AddAccountability(options => options.RequireAtLeastOneSink = true);
        services.AddRuntimePersistence();
        services.AddSingleton<IRuntimeStateContractContributor, ProcurementRuntimeStateContractContributor>();
        services.AddCrestCreatesInMemoryRuntimePersistence();
        services.AddRuntimeDelivery();
        services.AddTransient<AccountabilityHttpTerminalObserverMiddleware>();
        services.AddTransient<AccountabilityHttpOperationScopeMiddleware>();
        services.AddSingleton<InMemoryAuditSink>();
        services.AddSingleton<IAuditSink>(sp => sp.GetRequiredService<InMemoryAuditSink>());
        services.AddScoped<IAuditedMethodAccountabilityRuntime, AuditedMethodAccountabilityRuntime>();
        services.AddProcurementCapabilityHandlers();
        services.AddSingleton<InMemoryProcurementRequestStore>(_store);
        services.AddScoped<ProcurementApplicationService>();
        services.AddCrestCapabilityEndpoints();
        services.AddCrestCompatibilityProjection();
        services.AddGeneratedAppServiceHttp();
        services.AddCrestOpenApi();
        services.AddScoped<ProcurementAppService>();
        services.AddScoped<ProcurementApprovalTaskService>();
        services.AddScoped<IProcurementApprovalOrchestrator>(sp =>
            sp.GetRequiredService<ProcurementApprovalTaskService>());
        services.AddSingleton<ProcurementDecisionReconciliationStore>();

        services.AddSingleton<ISchemaRegistry>(schemaRegistry);
        services.AddSingleton<ICapabilityRegistry>(capabilityRegistry);
        services.AddSingleton<IHumanTaskRegistry>(humanTaskRegistry);
        services.AddSingleton<IWorkflowRegistry>(workflowRegistry);
        services.AddSingleton<IFormRegistry>(formRegistry);
        services.AddFormKernel();
        services.AddHumanTaskRuntime();
        services.AddHumanTaskCompletionObligation(
            ProcurementContractIds.ApprovalHumanTask,
            1,
            ProcurementHumanTaskDecisionHandler.ConsumerIdValue);
        services.AddOutboxRequiredConsumer<HumanTaskCompletedEvent, ProcurementHumanTaskDecisionHandler>(ProcurementHumanTaskDecisionHandler.ConsumerIdValue);
        services.AddWorkflowEngine();
        services.AddScoped<ProcurementLocalEventBus>();
        services.AddScoped<ILocalEventBus>(sp =>
            sp.GetRequiredService<ProcurementLocalEventBus>());

        services.AddSingleton<ICapabilityHandlerRegistry, ProcurementHandlerRegistry>();
        services.AddSingleton<IDescriptorLookup>(new ProcurementDescriptorLookup(
            ProcurementDescriptorCatalog.Schemas.Cast<IDescriptor>()
                .Concat(ProcurementDescriptorCatalog.NativeCapabilities)
                .Append(ProcurementDescriptorCatalog.ApprovalForm)
                .Append(ProcurementDescriptorCatalog.ApprovalHumanTask)
                .Append(ProcurementDescriptorCatalog.ApprovalWorkflow)));
        services.AddSingleton<ISchemaValidator, SchemaValidator>();
        services.AddHttpContextAccessor();
        services.AddAuthorization();
        services.AddAuthentication(SampleAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, SampleAuthenticationHandler>(
                SampleAuthenticationHandler.SchemeName,
                _ => { });
        services.AddScoped<SampleExecutionIdentity>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<SampleExecutionIdentity>());
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<SampleExecutionIdentity>());
        services.AddScoped<IAgentExecutionContextAccessor>(sp => sp.GetRequiredService<SampleExecutionIdentity>());
        services.AddScoped<IPermissionChecker, SamplePermissionChecker>();

        services.AddCrestMcpToolProjection(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.TypeInfoResolver = ProcurementJsonContext.Default;
        });
        services.AddSingleton<DevelopmentInMemoryAgentToolInvocationGate>();
        services.AddSingleton<IAgentToolInvocationGate>(sp =>
            sp.GetRequiredService<DevelopmentInMemoryAgentToolInvocationGate>());
        services.AddSingleton<IAgentToolInvocationLeaseAbandoner>(sp =>
            sp.GetRequiredService<DevelopmentInMemoryAgentToolInvocationGate>());
        services.AddSingleton<SampleAgentToolApprovalGate>();
        services.AddSingleton<IAgentToolApprovalGate>(sp =>
            sp.GetRequiredService<SampleAgentToolApprovalGate>());
        services.AddSingleton<SampleAgentToolBudgetGate>();
        services.AddSingleton<IAgentToolBudgetGate>(sp =>
            sp.GetRequiredService<SampleAgentToolBudgetGate>());
        services.AddSingleton<DevelopmentInMemoryAgentToolGovernanceAuditor>();
        services.AddSingleton<IAgentToolGovernanceAuditor>(sp =>
            sp.GetRequiredService<DevelopmentInMemoryAgentToolGovernanceAuditor>());
        services.AddCrestAgentTools(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.TypeInfoResolver = ProcurementJsonContext.Default;
        });

        services.AddCrestHttpJsonResolver(new ProcurementCombinedJsonResolver());
    }

    public override Task OnApplicationInitializationAsync(IHost host)
    {
        // #129 S2.3: compose the host capability registry from its static catalog plus exactly the
        // generated contributions this host activated; mapping verifies the frozen result.
        host.Services.BuildFromHostCatalog(ProcurementDescriptorCatalog.NativeCapabilities);
        return Task.CompletedTask;
    }
}
