using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Shared.Attributes;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Contracts.Interfaces;

public interface IDashboardAppService
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "summary",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Dashboard.Search" })]
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
}
