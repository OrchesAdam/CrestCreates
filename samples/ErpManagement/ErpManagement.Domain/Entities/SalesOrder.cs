using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Domain.Shared.Enums;

namespace ErpManagement.Domain.Entities;

[Entity]
public class SalesOrder : AuditedEntity<Guid>
{
    public string Number { get; private set; } = string.Empty;
    public string? WorkflowInstanceId { get; private set; }
    public string? HumanTaskInstanceId { get; private set; }
    public string? ApproverId { get; private set; }
    public string? ApprovalComment { get; private set; }
    public string? RequesterId { get; private set; }
    public bool? ApprovalAccepted { get; private set; }
    public Guid CustomerId { get; private set; }
    public SalesOrderStatus Status { get; private set; } = SalesOrderStatus.Draft;
    public decimal TotalAmount { get; private set; }
    public string? Notes { get; private set; }
    public Guid TenantId { get; private set; }

    public virtual ICollection<SalesOrderLine> Lines { get; private set; } = new List<SalesOrderLine>();

    protected SalesOrder() { }

    public SalesOrder(Guid id, string number, Guid customerId, Guid tenantId)
    {
        Id = id;
        SetNumber(number);
        CustomerId = customerId;
        TenantId = tenantId;
    }

    public void SetNumber(string number)
    {
        if (string.IsNullOrWhiteSpace(number) || number.Length > 64) throw new ArgumentException("Number must be 1..64 chars", nameof(number));
        Number = number;
    }

    public void SetCustomer(Guid customerId)
    {
        RequireDraft();
        CustomerId = customerId;
    }
    public void SetNotes(string? notes) => Notes = notes;

    public void ReplaceLines(IEnumerable<SalesOrderLine> lines)
    {
        RequireDraft();
        Lines.Clear();
        foreach (var line in lines) Lines.Add(line);
        TotalAmount = Lines.Sum(line => line.Amount);
    }

    public void Submit(string requesterId)
    {
        RequireDraft();
        if (Lines.Count == 0) throw new InvalidOperationException("A sales order must have lines.");
        ArgumentException.ThrowIfNullOrWhiteSpace(requesterId);
        RequesterId = requesterId;
        Status = SalesOrderStatus.PendingApproval;
    }

    public void MarkPendingApproval(string workflowId, string taskId)
    {
        RequirePendingDecision();
        WorkflowInstanceId = workflowId;
        HumanTaskInstanceId = taskId;
    }

    public void Approve(string approverId, string? comment) => RecordDecision(true, approverId, comment);
    public void Reject(string approverId, string? comment) => RecordDecision(false, approverId, comment);

    private void RecordDecision(bool approved, string approverId, string? comment)
    {
        RequirePendingDecision();
        ApproverId = approverId;
        ApprovalComment = comment;
        ApprovalAccepted = approved;
        Status = approved ? SalesOrderStatus.Approved : SalesOrderStatus.Rejected;
    }

    public void RecordShipment(IReadOnlyDictionary<Guid, decimal> quantities)
    {
        if (Status is not (SalesOrderStatus.Approved or SalesOrderStatus.PartiallyShipped))
            throw new InvalidOperationException("Sales order is not open for shipping.");
        if (quantities.Count == 0 || quantities.Any(entry => !Lines.Any(line => line.Id == entry.Key
            && entry.Value > 0 && entry.Value <= line.Quantity - line.ShippedQuantity)))
            throw new InvalidOperationException("Shipment exceeds the remaining quantity or contains an unknown order line.");

        foreach (var line in Lines)
            if (quantities.TryGetValue(line.Id, out var quantity)) line.AddShipped(quantity);
        MarkShipped();
    }

    private void MarkShipped() => Status = Lines.All(line => line.ShippedQuantity == line.Quantity)
        ? SalesOrderStatus.Shipped : SalesOrderStatus.PartiallyShipped;

    public void Close()
    {
        if (Status is not (SalesOrderStatus.Approved or SalesOrderStatus.PartiallyShipped or SalesOrderStatus.Shipped or SalesOrderStatus.Rejected))
            throw new InvalidOperationException("Sales order cannot be closed in its current state.");
        Status = SalesOrderStatus.Closed;
    }

    public void RequireDraft()
    {
        if (Status != SalesOrderStatus.Draft) throw new InvalidOperationException("Only draft orders can be edited.");
    }

    private void RequirePendingDecision()
    {
        if (Status != SalesOrderStatus.PendingApproval || ApprovalAccepted.HasValue)
            throw new InvalidOperationException("Sales order has no pending decision.");
    }
}
