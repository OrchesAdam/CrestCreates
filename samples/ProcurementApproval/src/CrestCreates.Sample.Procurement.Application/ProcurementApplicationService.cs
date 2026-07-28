using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using CrestCreates.Sample.Procurement.Domain.Entities;
using CrestCreates.Sample.Procurement.Domain.ValueObjects;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Sample.Procurement.Application;

public sealed class ProcurementApplicationService
{
    private readonly InMemoryProcurementRequestStore _store;
    private readonly IWorkflowEngine _workflow;

    public ProcurementApplicationService(
        InMemoryProcurementRequestStore store,
        IWorkflowEngine workflow)
    {
        _store = store;
        _workflow = workflow;
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
        _store.Add(tenantId, request);

        if (request.RequiresApproval)
        {
            var workflow = await _workflow.ExecuteAsync(
                ProcurementContractIds.ApprovalWorkflow,
                new Dictionary<string, object?>
                {
                    ["tenantId"] = tenantId,
                    ["requestId"] = request.Id,
                    ["requesterId"] = requesterId
                },
                ct).ConfigureAwait(false);
            request.AttachWorkflow(workflow.InstanceId);
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

    public ProcurementRequestResult Approve(
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
        request.Approve(approverId, input.Comment);
        return Map(request);
    }

    public ProcurementRequestResult Reject(
        RejectProcurementRequestInput input,
        string tenantId,
        string approverId)
    {
        RequireContext(tenantId, approverId);
        var request = _store.GetById(tenantId, input.RequestId)
            ?? throw NotFound(input.RequestId);
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

    private static void RequireContext(string tenantId, string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
    }
}
