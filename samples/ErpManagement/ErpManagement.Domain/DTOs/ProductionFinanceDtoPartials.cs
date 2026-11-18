namespace ErpManagement.Application.Contracts.DTOs;

public partial class WorkOrderDto
{
    public List<WorkOrderMaterialDto> Materials { get; set; } = new();
    public List<WorkOrderReportDto> Reports { get; set; } = new();
}

public partial class ArApLedgerDto
{
    public List<SettlementRecordDto> Settlements { get; set; } = new();
}

public sealed class WorkOrderReportInput
{
    public decimal ReportedQuantity { get; set; }
}

public sealed class SettlementInput
{
    public decimal Amount { get; set; }
}
