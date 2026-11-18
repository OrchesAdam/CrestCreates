using CrestCreates.Authorization;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.Modularity;
using ErpManagement.Application.Approval;
using ErpManagement.Application.Authorization;
using ErpManagement.Application.Contracts.Events;
using ErpManagement.Application.Contracts.Interfaces;
using ErpManagement.Application.Contracts.Modules;
using ErpManagement.Application.Events;
using ErpManagement.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ErpManagement.Application.Modules;

[CrestModule(typeof(ApplicationContractsModule), Order = -100)]
public class ApplicationModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddPermissionDefinitionProvider<ErpPermissionDefinitionProvider>();
        services.AddScoped<IMaterialAppService, MaterialAppService>();
        services.AddScoped<IMaterialCategoryAppService, MaterialCategoryAppService>();
        services.AddScoped<IUnitOfMeasureAppService, UnitOfMeasureAppService>();
        services.AddScoped<IWarehouseAppService, WarehouseAppService>();
        services.AddScoped<IStorageLocationAppService, StorageLocationAppService>();
        services.AddScoped<ISupplierAppService, SupplierAppService>();
        services.AddScoped<CustomerAppService>();
        services.AddScoped<ICustomerAppService>(sp => sp.GetRequiredService<CustomerAppService>());
        services.AddScoped<IBomAppService, BomAppService>();

        // P1 procurement + inventory + approval center.
        services.AddScoped<IPurchaseRequisitionAppService, PurchaseRequisitionAppService>();
        services.AddScoped<IPurchaseOrderAppService, PurchaseOrderAppService>();
        services.AddScoped<IGoodsReceiptAppService, GoodsReceiptAppService>();
        services.AddScoped<IStockBalanceAppService, StockBalanceAppService>();
        services.AddScoped<IStockTransactionAppService, StockTransactionAppService>();
        services.AddScoped<IStockTransferAppService, StockTransferAppService>();
        services.AddScoped<IStockCountAppService, StockCountAppService>();
        services.AddScoped<IApprovalCenterAppService, ApprovalCenterAppService>();

        services.AddScoped<ISalesOrderAppService, SalesOrderAppService>();
        services.AddScoped<ISalesShipmentAppService, SalesShipmentAppService>();
        services.AddScoped<ISalesApprovalAppService, SalesApprovalAppService>();
        services.AddScoped<IWorkOrderAppService, WorkOrderAppService>();
        services.AddScoped<IArApLedgerAppService, ArApLedgerAppService>();
        services.AddScoped<IDashboardAppService, DashboardAppService>();
        services.AddScoped<ErpSalesApprovalOrchestrator>();

        services.AddScoped<ErpRequisitionApprovalOrchestrator>();
        services.AddScoped<StockPostedEventPublisher>();
        services.AddScoped<ILocalEventHandler<StockChanged>, StockChangedNotificationHandler>();
    }
}