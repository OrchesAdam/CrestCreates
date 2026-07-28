using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Sample.Procurement.Domain.Exceptions;
using CrestCreates.Sample.Procurement.Domain.Events;
using CrestCreates.Sample.Procurement.Domain.ValueObjects;

namespace CrestCreates.Sample.Procurement.Domain.Entities;

public class ProcurementRequest : AuditedAggregateRoot<Guid>
{
    public string Title { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public Money Amount { get; private set; } = default!;
    public string RequesterId { get; private set; } = default!;
    public string Category { get; private set; } = default!;
    public ProcurementRequestStatus Status { get; private set; }
    public string? ApproverId { get; private set; }
    public string? ApprovalComment { get; private set; }
    public string? RejectionReason { get; private set; }
    public string? WorkflowInstanceId { get; private set; }
    public DateTime? ApprovedAt { get; private set; }
    public DateTime? RejectedAt { get; private set; }

    private ProcurementRequest() { }

    public ProcurementRequest(
        Guid id,
        string title,
        string description,
        Money amount,
        string requesterId,
        string category)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidProcurementRequestException("Title is required.");
        if (amount is null)
            throw new InvalidProcurementRequestException("Amount is required.");
        if (string.IsNullOrWhiteSpace(requesterId))
            throw new InvalidProcurementRequestException("RequesterId is required.");
        if (string.IsNullOrWhiteSpace(category))
            throw new InvalidProcurementRequestException("Category is required.");

        Id = id;
        Title = title;
        Description = description;
        Amount = amount;
        RequesterId = requesterId;
        Category = category;
        Status = ProcurementRequestStatus.Draft;
    }

    public void Submit()
    {
        if (Status != ProcurementRequestStatus.Draft)
            throw new InvalidProcurementRequestException($"Cannot submit request in status {Status}.");

        Status = Amount.IsAboveThreshold(ApprovalThreshold)
            ? ProcurementRequestStatus.PendingApproval
            : ProcurementRequestStatus.Approved;

        AddDomainEvent(new ProcurementRequestSubmittedEvent(Id, Title, Amount.Amount, Amount.Currency, RequesterId, Category, Status));
    }

    public void AttachWorkflow(string workflowInstanceId)
    {
        if (Status != ProcurementRequestStatus.PendingApproval)
            throw new InvalidProcurementRequestException("Only pending requests may be attached to an approval workflow.");
        if (string.IsNullOrWhiteSpace(workflowInstanceId))
            throw new InvalidProcurementRequestException("WorkflowInstanceId is required.");
        if (WorkflowInstanceId is not null && !string.Equals(WorkflowInstanceId, workflowInstanceId, StringComparison.Ordinal))
            throw new InvalidProcurementRequestException("The request is already attached to another workflow.");

        WorkflowInstanceId = workflowInstanceId;
    }

    public void Approve(string approverId, string comment)
    {
        if (Status != ProcurementRequestStatus.PendingApproval)
            throw new InvalidProcurementRequestException($"Cannot approve request in status {Status}.");
        if (string.Equals(RequesterId, approverId, StringComparison.Ordinal))
            throw new InvalidProcurementRequestException("A requester cannot approve their own procurement request.");

        ApproverId = approverId;
        ApprovalComment = comment;
        Status = ProcurementRequestStatus.Approved;
        ApprovedAt = DateTime.UtcNow;

        AddDomainEvent(new ProcurementRequestApprovedEvent(Id, ApproverId, ApprovalComment));
    }

    public void Reject(string approverId, string reason)
    {
        if (Status != ProcurementRequestStatus.PendingApproval)
            throw new InvalidProcurementRequestException($"Cannot reject request in status {Status}.");

        ApproverId = approverId;
        RejectionReason = reason;
        Status = ProcurementRequestStatus.Rejected;
        RejectedAt = DateTime.UtcNow;

        AddDomainEvent(new ProcurementRequestRejectedEvent(Id, ApproverId, RejectionReason));
    }

    public void Cancel()
    {
        if (Status is ProcurementRequestStatus.Approved or ProcurementRequestStatus.Rejected)
            throw new InvalidProcurementRequestException($"Cannot cancel request in status {Status}.");

        Status = ProcurementRequestStatus.Cancelled;
    }

    public static readonly decimal ApprovalThreshold = 10000m;

    public bool RequiresApproval => Status == ProcurementRequestStatus.PendingApproval || Amount.IsAboveThreshold(ApprovalThreshold);
}
