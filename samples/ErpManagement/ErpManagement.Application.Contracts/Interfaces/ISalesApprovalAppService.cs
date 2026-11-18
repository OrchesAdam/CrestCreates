using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Domain.Shared.Permissions;

namespace ErpManagement.Application.Contracts.Interfaces;

public interface ISalesApprovalAppService
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "tasks",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { ErpPermissions.SalesApproval.Search })]
    Task<IReadOnlyList<DTOs.SalesApprovalTaskDto>> GetTasksAsync(CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "tasks/{id}/approve",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { ErpPermissions.SalesApproval.Complete })]
    Task<DTOs.SalesApprovalTaskDto> ApproveAsync(string id, DTOs.ApprovalDecisionInput input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "tasks/{id}/reject",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { ErpPermissions.SalesApproval.Complete })]
    Task<DTOs.SalesApprovalTaskDto> RejectAsync(string id, DTOs.ApprovalDecisionInput input, CancellationToken cancellationToken = default);
}