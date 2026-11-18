using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Domain.Shared.Permissions;

namespace ErpManagement.Application.Contracts.Interfaces;

public interface IApprovalCenterAppService
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "tasks",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { ErpPermissions.HumanTasks.Search })]
    Task<IReadOnlyList<DTOs.ApprovalTaskDto>> GetTasksAsync(CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "tasks/{id}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { ErpPermissions.HumanTasks.Get })]
    Task<DTOs.ApprovalTaskDto?> GetTaskAsync(string id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "tasks/{id}/approve",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required)]
    Task<DTOs.ApprovalTaskDto> ApproveAsync(string id, DTOs.ApprovalDecisionInput input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "tasks/{id}/reject",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required)]
    Task<DTOs.ApprovalTaskDto> RejectAsync(string id, DTOs.ApprovalDecisionInput input, CancellationToken cancellationToken = default);
}