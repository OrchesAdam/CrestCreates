namespace ErpManagement.Application.Contracts.DTOs;

public sealed class DashboardSummaryDto
{
    public int PendingRequisitionCount { get; init; }
    public int LowStockMaterialCount { get; init; }
    public decimal MonthlyPurchaseAmount { get; init; }
    public decimal MonthlySalesAmount { get; init; }
    public decimal OutstandingReceivableAmount { get; init; }
    public decimal OutstandingPayableAmount { get; init; }
}
