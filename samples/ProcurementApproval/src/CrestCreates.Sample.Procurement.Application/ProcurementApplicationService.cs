using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using CrestCreates.Sample.Procurement.Domain;
using CrestCreates.Sample.Procurement.Domain.Entities;
using CrestCreates.Sample.Procurement.Domain.ValueObjects;

namespace CrestCreates.Sample.Procurement.Application;

public sealed class ProcurementApplicationService
{
    private readonly InMemoryProcurementRequestStore _store;
    private readonly IProcurementApprovalOrchestrator _approval;

    public ProcurementApplicationService(
        InMemoryProcurementRequestStore store,
        IProcurementApprovalOrchestrator approval)
    {
        _store = store;
        _approval = approval;
    }

    public async Task<SubmitProcurementRequestResult> SubmitAsync(
        SubmitProcurementRequestInput input,
        string tenantId,
        string requesterId,
        CancellationToken ct)
    {
        RequireContext(tenantId, requesterId);
        var request = new ProcurementRequest(
            Guid.NewGuid(),
            input.Title,
            input.Description,
            new Money(input.Amount, input.Currency),
            requesterId,
            input.Category);
        request.Submit();

        if (request.RequiresApproval)
        {
            var lease = await _approval.StartAsync(
                request.Id,
                tenantId,
                requesterId,
                ct).ConfigureAwait(false);
            try
            {
                request.AttachWorkflow(lease.WorkflowInstanceId);
                _store.Add(tenantId, request);
            }
            catch
            {
                await _approval.RollbackAsync(lease, ct).ConfigureAwait(false);
                throw;
            }
        }
        else
        {
            _store.Add(tenantId, request);
        }

        return new SubmitProcurementRequestResult
        {
            RequestId = request.Id,
            Status = request.Status.ToString(),
            Amount = request.Amount.Amount,
            Currency = request.Amount.Currency,
            RequiresApproval = request.RequiresApproval
        };
    }

    public ProcurementRequestResult Get(GetProcurementRequestInput input, string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var request = _store.GetById(tenantId, input.RequestId)
            ?? throw NotFound(input.RequestId);
        return Map(request);
    }

    public ProcurementRequestResult ApplyApprovalDecision(
        ApproveProcurementRequestInput input,
        string tenantId,
        string approverId)
    {
        RequireContext(tenantId, approverId);
        var request = _store.GetById(tenantId, input.RequestId)
            ?? throw NotFound(input.RequestId);
        if (string.Equals(request.RequesterId, approverId, StringComparison.Ordinal))
        {
            throw new CapabilityFailureException(
                "CAPABILITY_FORBIDDEN",
                "A requester cannot approve their own procurement request.");
        }
        if (request.Status == ProcurementRequestStatus.Approved)
            return Map(request);
        if (request.Status == ProcurementRequestStatus.Rejected)
            throw DecisionConflict(input.RequestId, "approve", request.Status);
        request.Approve(approverId, input.Comment);
        return Map(request);
    }

    public ProcurementRequestResult ApplyRejectionDecision(
        RejectProcurementRequestInput input,
        string tenantId,
        string approverId)
    {
        RequireContext(tenantId, approverId);
        var request = _store.GetById(tenantId, input.RequestId)
            ?? throw NotFound(input.RequestId);
        if (request.Status == ProcurementRequestStatus.Rejected)
            return Map(request);
        if (request.Status == ProcurementRequestStatus.Approved)
            throw DecisionConflict(input.RequestId, "reject", request.Status);
        request.Reject(approverId, input.Reason);
        return Map(request);
    }

    private static ProcurementRequestResult Map(ProcurementRequest request) => new()
    {
        Id = request.Id,
        RequestId = request.Id,
        Title = request.Title,
        Description = request.Description,
        Amount = request.Amount.Amount,
        Currency = request.Amount.Currency,
        RequesterId = request.RequesterId,
        Category = request.Category,
        Status = request.Status.ToString(),
        ApproverId = request.ApproverId,
        WorkflowInstanceId = request.WorkflowInstanceId,
        ApprovedAt = request.ApprovedAt,
        RejectedAt = request.RejectedAt
    };

    private static CapabilityFailureException NotFound(Guid requestId)
        => new(
            "CAPABILITY_RESOURCE_NOT_FOUND",
            $"Procurement request '{requestId}' is unavailable.");

    private static CapabilityFailureException DecisionConflict(
        Guid requestId,
        string decision,
        ProcurementRequestStatus status)
        => new(
            "CAPABILITY_DECISION_CONFLICT",
            $"Procurement request '{requestId}' cannot be {decision}d from status '{status}'.");

    private static void RequireContext(string tenantId, string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
    }
}
