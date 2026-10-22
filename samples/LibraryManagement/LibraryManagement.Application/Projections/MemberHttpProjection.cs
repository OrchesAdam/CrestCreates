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
    ContractType = typeof(MemberHttpProjection),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/member",
    CapabilityIdPrefix = "compat.appservice.member")]
public sealed class MemberHttpProjection(IMemberAppService service)
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "by-email/{email}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Member.Get" })]
    public Task<MemberDto?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        => service.GetByEmailAsync(email, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "get-active-members",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Member.Search" })]
    public Task<PagedResultDto<MemberDto>> GetActiveMembersAsync(
        int? pageIndex,
        int? pageSize,
        CancellationToken cancellationToken = default)
        => service.GetActiveMembersAsync(pageIndex ?? 0, pageSize ?? 0, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "pay-balance",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "Member.Search" })]
    public Task PayBalanceAsync(Guid id, decimal amount, CancellationToken cancellationToken = default)
        => service.PayBalanceAsync(id, amount, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "Member.Create" })]
    public Task<MemberDto> CreateAsync(CreateMemberDto input, CancellationToken cancellationToken = default)
        => service.CreateAsync(input, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Member.Get" })]
    public Task<MemberDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => service.GetByIdAsync(id, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "all",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Member.Search" })]
    public Task<IReadOnlyList<MemberDto>> GetAllAsync(CancellationToken cancellationToken = default)
        => service.GetAllAsync(cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Member.Search" })]
    public Task<PagedResultDto<MemberDto>> GetListAsync(
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
        Permissions = new[] { "Member.Search" })]
    public Task<PagedResultDto<MemberDto>> QueryAsync(QueryRequest<Member> request, CancellationToken cancellationToken = default)
        => service.QueryAsync(request, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Put,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "Member.Update" })]
    public Task<MemberDto> UpdateAsync(Guid id, MemberDto input, CancellationToken cancellationToken = default)
        => service.UpdateAsync(id, input, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Delete,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "Member.Delete" },
        RequiresConcurrencyToken = true)]
    public Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default)
        => service.DeleteAsync(id, expectedStamp, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "exists/{id}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Member.Search" })]
    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
        => service.ExistsAsync(id, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "count",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Member.Search" })]
    public Task<int> CountAsync(CancellationToken cancellationToken = default)
        => service.CountAsync(cancellationToken);
}
