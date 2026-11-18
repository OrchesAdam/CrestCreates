using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Application.Contracts.DTOs;
using ErpManagement.Domain.Shared.Permissions;

namespace ErpManagement.Application.Contracts.Interfaces;

public interface IArApLedgerAppService
{
    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "all", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { ErpPermissions.ArApLedger.Search })]
    Task<IReadOnlyList<ArApLedgerDto>> GetAllAsync(CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "{id}", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { ErpPermissions.ArApLedger.Get })]
    Task<ArApLedgerDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Post, Route = "{id}/settle", Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required, Permissions = new[] { ErpPermissions.ArApLedger.Settle })]
    Task<ArApLedgerDto> SettleAsync(Guid id, SettlementInput input, CancellationToken cancellationToken = default);
}
