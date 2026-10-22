using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Application.Contracts.Query;
using CrestCreates.Domain.Shared.Attributes;
using LibraryManagement.Application.Contracts.DTOs;
using LibraryManagement.Application.Contracts.Interfaces;
using LibraryManagement.Domain.Entities;

namespace LibraryManagement.Application.Projections;

[CapabilityCompatibilityProjection(
    ContractType = typeof(LoanHttpProjection),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/loan",
    CapabilityIdPrefix = "compat.appservice.loan")]
public sealed class LoanHttpProjection(ILoanAppService service)
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "search",
        Kind = HttpProjectionOperationKind.Query,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "Loan.Search" })]
    public Task<PagedResultDto<LoanDto>> SearchAsync(LoanSearchRequest request, CancellationToken cancellationToken = default)
        => service.SearchAsync(request, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "overdue-loans",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Loan.Search" })]
    public Task<IReadOnlyList<LoanDto>> GetOverdueLoansAsync(CancellationToken cancellationToken = default)
        => service.GetOverdueLoansAsync(cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "return-book",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "Loan.Search" })]
    public Task<LoanDto> ReturnBookAsync(ReturnBookDto input, CancellationToken cancellationToken = default)
        => service.ReturnBookAsync(input, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "extend-loan",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "Loan.Search" })]
    public Task<LoanDto> ExtendLoanAsync(ExtendLoanDto input, CancellationToken cancellationToken = default)
        => service.ExtendLoanAsync(input, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "process-overdue-loans",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "Loan.Search" })]
    public Task ProcessOverdueLoansAsync(CancellationToken cancellationToken = default)
        => service.ProcessOverdueLoansAsync(cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "Loan.Create" })]
    public Task<LoanDto> CreateAsync(CreateLoanDto input, CancellationToken cancellationToken = default)
        => service.CreateAsync(input, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Loan.Get" })]
    public Task<LoanDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => service.GetByIdAsync(id, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "all",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Loan.Search" })]
    public Task<IReadOnlyList<LoanDto>> GetAllAsync(CancellationToken cancellationToken = default)
        => service.GetAllAsync(cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Loan.Search" })]
    public Task<PagedResultDto<LoanDto>> GetListAsync(
        int? pageIndex,
        int? pageSize,
        CancellationToken cancellationToken = default)
        => service.GetListAsync(
            new PagedRequestDto { PageIndex = pageIndex ?? 0, PageSize = pageSize ?? 0 },
            cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "query",
        Kind = HttpProjectionOperationKind.Query,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "Loan.Search" })]
    public Task<PagedResultDto<LoanDto>> QueryAsync(QueryRequest<Loan> request, CancellationToken cancellationToken = default)
        => service.QueryAsync(request, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Put,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "Loan.Update" })]
    public Task<LoanDto> UpdateAsync(Guid id, LoanDto input, CancellationToken cancellationToken = default)
        => service.UpdateAsync(id, input, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Delete,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "Loan.Delete" },
        RequiresConcurrencyToken = true)]
    public Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default)
        => service.DeleteAsync(id, expectedStamp, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "exists/{id}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Loan.Search" })]
    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
        => service.ExistsAsync(id, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "count",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Loan.Search" })]
    public Task<int> CountAsync(CancellationToken cancellationToken = default)
        => service.CountAsync(cancellationToken);
}
