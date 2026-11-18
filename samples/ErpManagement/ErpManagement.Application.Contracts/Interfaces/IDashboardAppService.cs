using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Application.Contracts.DTOs;
using ErpManagement.Domain.Shared.Permissions;

namespace ErpManagement.Application.Contracts.Interfaces;

public interface IDashboardAppService
{
    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "summary", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { ErpPermissions.Dashboard.Get })]
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
}
