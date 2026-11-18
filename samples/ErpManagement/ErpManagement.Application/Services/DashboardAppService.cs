using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.MultiTenancy.Abstract;
using ErpManagement.Application.Contracts.DTOs;
using ErpManagement.Application.Contracts.Interfaces;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Shared.Enums;
using ErpManagement.Domain.Shared.Permissions;
using Microsoft.EntityFrameworkCore;

namespace ErpManagement.Application.Services;

[CrestService]
[CapabilityCompatibilityProjection(
    ContractType = typeof(IDashboardAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/dashboard",
    CapabilityIdPrefix = "compat.appservice.dashboard")]
public class DashboardAppService(
    ICurrentTenant tenant,
    IPermissionChecker permissions,
    IFeatureChecker features,
    IPurchaseRequisitionRepository requisitions,
    IPurchaseOrderRepository purchases,
    ISalesOrderRepository sales,
    IArApLedgerRepository ledger,
    ICrestRepositoryBase<StockBalance, Guid> balances,
    ICrestRepositoryBase<Material, Guid> materials) : IDashboardAppService
{
    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        await permissions.CheckAsync(ErpPermissions.Dashboard.Get);
        var tenantId = tenant.GetErpTenantId();
        var now = DateTime.UtcNow;
        var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);
        var purchaseOrders = purchases.GetQueryable().Where(order => order.TenantId == tenantId
            && order.CreationTime >= start && order.CreationTime < end
            && (order.Status == PurchaseOrderStatus.Confirmed || order.Status == PurchaseOrderStatus.Received));
        var salesOrders = sales.GetQueryable().Where(order => order.TenantId == tenantId
            && order.CreationTime >= start && order.CreationTime < end
            && (order.Status == SalesOrderStatus.Approved || order.Status == SalesOrderStatus.Shipped));
        var outstanding = ledger.GetQueryable().Where(entry => entry.TenantId == tenantId && entry.Status != ArApStatus.Settled);
        var salesEnabled = await features.IsEnabledAsync("Erp.Sales", cancellationToken);
        var financeEnabled = await features.IsEnabledAsync("Erp.Finance", cancellationToken);

        return new DashboardSummaryDto
        {
            PendingRequisitionCount = await requisitions.GetQueryable().CountAsync(
                order => order.TenantId == tenantId && order.Status == RequisitionStatus.PendingApproval, cancellationToken),
            LowStockMaterialCount = await new LowStockQuery(balances, materials).MaterialIds(tenantId).CountAsync(cancellationToken),
            MonthlyPurchaseAmount = await purchaseOrders.SumAsync(order => order.TotalAmount, cancellationToken),
            MonthlySalesAmount = salesEnabled ? await salesOrders.SumAsync(order => order.TotalAmount, cancellationToken) : 0,
            OutstandingReceivableAmount = financeEnabled ? await outstanding.Where(entry => entry.Direction == ArApDirection.Receivable)
                .SumAsync(entry => entry.Amount - entry.SettledAmount, cancellationToken) : 0,
            OutstandingPayableAmount = financeEnabled ? await outstanding.Where(entry => entry.Direction == ArApDirection.Payable)
                .SumAsync(entry => entry.Amount - entry.SettledAmount, cancellationToken) : 0
        };
    }
}
