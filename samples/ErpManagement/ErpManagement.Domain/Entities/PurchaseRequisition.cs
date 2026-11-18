using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Domain.Shared.Enums;

namespace ErpManagement.Domain.Entities;

[Entity]
public class PurchaseRequisition : AuditedEntity<Guid>
{
    public string Number { get; private set; } = string.Empty;
    public string RequesterId { get; private set; } = string.Empty;
    public Guid? SupplierId { get; private set; }
    public decimal TotalAmount { get; private set; }
    public RequisitionStatus Status { get; private set; } = RequisitionStatus.Draft;
    public string? WorkflowInstanceId { get; private set; }
    public string? HumanTaskInstanceId { get; private set; }
    public string? ApproverId { get; private set; }
    public string? ApprovalComment { get; private set; }
    public Guid? ConvertedOrderId { get; private set; }
    public string? Notes { get; private set; }
    public Guid TenantId { get; private set; }

    public virtual ICollection<PurchaseRequisitionLine> Lines { get; private set; } = new List<PurchaseRequisitionLine>();

    protected PurchaseRequisition() { }

    public PurchaseRequisition(Guid id, string number, string requesterId, Guid tenantId)
    {
        Id = id;
        SetNumber(number);
        if (string.IsNullOrWhiteSpace(requesterId)) throw new ArgumentException("Requester is required", nameof(requesterId));
        RequesterId = requesterId;
        TenantId = tenantId;
    }

    public void SetNumber(string number)
    {
        if (string.IsNullOrWhiteSpace(number) || number.Length > 64) throw new ArgumentException("Number must be 1..64 chars", nameof(number));
        Number = number;
    }

    public void SetSupplier(Guid? supplierId) => SupplierId = supplierId;
    public void SetNotes(string? notes) => Notes = notes;

    public void ReplaceLines(IEnumerable<PurchaseRequisitionLine> lines)
    {
        RequireDraft();
        Lines.Clear();
        foreach (var line in lines) Lines.Add(line);
        TotalAmount = Lines.Sum(line => line.Amount);
    }

    public void Submit()
    {
        if (Status != RequisitionStatus.Draft) throw new InvalidOperationException("Only draft requisitions can be submitted.");
        if (Lines.Count == 0) throw new InvalidOperationException("A requisition without lines cannot be submitted.");
        Status = RequisitionStatus.PendingApproval;
    }

    public void MarkPendingApproval(string workflowInstanceId, string humanTaskInstanceId)
    {
        WorkflowInstanceId = workflowInstanceId;
        HumanTaskInstanceId = humanTaskInstanceId;
    }

    public void Approve(string approverId, string comment)
    {
        if (Status != RequisitionStatus.PendingApproval) throw new InvalidOperationException("Only pending requisitions can be approved.");
        Status = RequisitionStatus.Approved;
        ApproverId = approverId;
        ApprovalComment = comment;
    }

    public void Reject(string approverId, string comment)
    {
        if (Status != RequisitionStatus.PendingApproval) throw new InvalidOperationException("Only pending requisitions can be rejected.");
        Status = RequisitionStatus.Rejected;
        ApproverId = approverId;
        ApprovalComment = comment;
    }

    public void MarkConverted(Guid orderId)
    {
        if (Status != RequisitionStatus.Approved) throw new InvalidOperationException("Only approved requisitions can be converted.");
        Status = RequisitionStatus.Converted;
        ConvertedOrderId = orderId;
    }

    private void RequireDraft()
    {
        if (Status != RequisitionStatus.Draft) throw new InvalidOperationException("Only draft requisitions can be edited.");
    }
}
