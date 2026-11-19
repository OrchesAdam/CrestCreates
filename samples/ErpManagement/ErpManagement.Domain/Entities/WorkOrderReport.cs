using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace ErpManagement.Domain.Entities;

[Entity]
public class WorkOrderReport : AuditedEntity<Guid>
{
    private string? _operationResult;

    public Guid WorkOrderId { get; private set; }
    public decimal ReportedQuantity { get; private set; }
    public string ReportSource { get; private set; } = string.Empty;
    public DateTimeOffset ReportedAt { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? OperationId { get; private set; }

    protected WorkOrderReport() { }

    public WorkOrderReport(Guid id, Guid workOrderId, decimal quantity, string source, DateTimeOffset at, Guid tenantId, Guid operationId)
    {
        Id = id;
        WorkOrderId = workOrderId;
        ReportedQuantity = quantity;
        ReportSource = source;
        ReportedAt = at;
        TenantId = tenantId;
        OperationId = operationId;
    }

    public void RecordOperationResult(string result) => _operationResult = result;

    public string GetOperationResult() => _operationResult
        ?? throw new InvalidOperationException("The work order report has no stored operation result.");
}
