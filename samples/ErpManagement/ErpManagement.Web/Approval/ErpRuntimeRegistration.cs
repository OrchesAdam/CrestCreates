using System.Linq;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.Form;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Bootstrap;
using CrestCreates.Metadata.Registry;
using CrestCreates.Runtime.Delivery;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using CrestCreates.Runtime.Persistence;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using ErpManagement.Application.Approval;
using ErpManagement.Application.Contracts.Events;
using ErpManagement.Application.Events;
using ErpManagement.Application.Contracts.Integration;
using ErpManagement.Application.Integration;
using ErpManagement.Domain.Shared.Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ErpManagement.Web.Approval;

/// <summary>
/// Wires the formal Workflow + HumanTask + Outbox runtime (PG, same database / dedicated schema)
/// for the ERP approval chain. Called from the host Program where the configuration is available.
/// </summary>
public static class ErpRuntimeRegistration
{
    public static IServiceCollection AddErpApprovalRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        var schemaRegistry = new SchemaRegistry(new RegistryValidationEngine<SchemaDescriptor>([]));
        schemaRegistry.Build([new ErpDescriptorProvider<SchemaDescriptor>(ErpDescriptorCatalog.Schemas)]);
        var humanTaskRegistry = new HumanTaskRegistry(new RegistryValidationEngine<HumanTaskDescriptor>([]));
        humanTaskRegistry.Build([new ErpDescriptorProvider<HumanTaskDescriptor>([ErpDescriptorCatalog.ApprovalHumanTask, ErpDescriptorCatalog.SalesApprovalHumanTask])]);
        var workflowRegistry = new WorkflowRegistry(new RegistryValidationEngine<WorkflowDescriptor>([]));
        workflowRegistry.Build([new ErpDescriptorProvider<WorkflowDescriptor>([ErpDescriptorCatalog.ApprovalWorkflow, ErpDescriptorCatalog.SalesApprovalWorkflow])]);
        var formRegistry = new FormRegistry(new RegistryValidationEngine<FormDescriptor>([]));
        formRegistry.Build([new ErpDescriptorProvider<FormDescriptor>([ErpDescriptorCatalog.ApprovalForm, ErpDescriptorCatalog.SalesApprovalForm])]);

        services.AddDescriptorStableHash();
        services.AddRuntimePersistence();
        services.AddSingleton<IRuntimeStateContractContributor, ErpRuntimeStateContractContributor>();

        var runtimeConnectionString = configuration["Erp:Runtime:ConnectionString"]
                                      ?? configuration.GetConnectionString("Default")
                                      ?? throw new InvalidOperationException("Erp runtime requires a PostgreSQL connection string.");
        services.AddCrestCreatesPostgreSqlRuntimePersistence(new PostgreSqlRuntimePersistenceOptions
        {
            ConnectionString = runtimeConnectionString,
            Schema = configuration["Erp:Runtime:Schema"] ?? "crest_erp_runtime",
            ApplyMigrations = true
        });
        services.AddRuntimeDelivery();
        services.AddOutboxDeliveryHandler<ErpManagement.Application.Events.ErpStockPostedOutboxHandler>(
            ErpManagement.Domain.Shared.Constants.ErpContractIds.StockPostedContractId);

        services.AddSingleton<ISchemaRegistry>(schemaRegistry);
        services.AddSingleton<IHumanTaskRegistry>(humanTaskRegistry);
        services.AddSingleton<IWorkflowRegistry>(workflowRegistry);
        services.AddSingleton<IFormRegistry>(formRegistry);
        services.AddSingleton<IHumanTaskAssigneeResolver, ErpHumanTaskAssigneeResolver>();

        services.AddFormKernel();
        services.AddHumanTaskRuntime();
        services.AddWorkflowEngine();

        // Requisition approval decision: the HumanTask completion is durably owed to the
        // decision consumer, which is the sole requisition write-back path.
        services.AddHumanTaskCompletionObligation(
            ErpContractIds.RequisitionApprovalHumanTask, 1, ErpRequisitionDecisionConsumer.ConsumerIdValue);
        services.AddOutboxRequiredConsumer<HumanTaskCompletedEvent, ErpRequisitionDecisionConsumer>(
            ErpContractIds.RequisitionDecisionConsumerId);

        services.AddHumanTaskCompletionObligation(
            ErpContractIds.SalesApprovalHumanTask, 1, ErpSalesOrderDecisionConsumer.ConsumerIdValue);
        services.AddOutboxRequiredConsumer<HumanTaskCompletedEvent, ErpSalesOrderDecisionConsumer>(
            ErpContractIds.SalesDecisionConsumerId);

        // StockPosted integration event: reliable consumption proven by the business inbox.
        services.AddOutboxRequiredConsumer<StockPostedEvent, ErpStockPostedInboxConsumer>(
            ErpContractIds.StockPostedConsumerId);

        // P3 demonstrates outbound contracts in-process. Production RabbitMQ would bind canonical
        // events with [CrestEvent], JsonContracts codecs, and AddRabbitMqEventBus, using
        // deploy/docker-compose.rabbitmq.yml as the broker overlay.
        services.AddCrestLocalEventDispatch(builder =>
        {
            builder.Declare<StockChanged>();
            builder.Declare<MesWorkOrderReleased>();
            builder.Declare<MesWorkOrderCompleted>();
            builder.Declare<CrmCustomerChanged>();
        });
        services.AddScoped<IntegrationOutboundLogHandler>();
        services.AddScoped<ILocalEventHandler<MesWorkOrderReleased>>(sp => sp.GetRequiredService<IntegrationOutboundLogHandler>());
        services.AddScoped<ILocalEventHandler<MesWorkOrderCompleted>>(sp => sp.GetRequiredService<IntegrationOutboundLogHandler>());
        services.AddScoped<ILocalEventHandler<CrmCustomerChanged>>(sp => sp.GetRequiredService<IntegrationOutboundLogHandler>());

        services.AddSingleton<IDescriptorLookup>(new ErpDescriptorLookup(
            ErpDescriptorCatalog.Schemas.Cast<IDescriptor>()
                .Append(ErpDescriptorCatalog.ApprovalForm)
                .Append(ErpDescriptorCatalog.SalesApprovalForm)
                .Append(ErpDescriptorCatalog.ApprovalHumanTask)
                .Append(ErpDescriptorCatalog.SalesApprovalHumanTask)
                .Append(ErpDescriptorCatalog.ApprovalWorkflow)
                .Append(ErpDescriptorCatalog.SalesApprovalWorkflow)));

        return services;
    }
}