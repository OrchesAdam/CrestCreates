using System.Text.Json;
using CrestCreates.Aop.Interceptors;
using CrestCreates.Application.Services;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.DataFilter;
using CrestCreates.Domain.Shared.Exceptions;
using CrestCreates.MultiTenancy.Abstract;
using ErpManagement.Application.Contracts.DTOs;
using ErpManagement.Application.Contracts.Interfaces;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Shared.Permissions;

namespace ErpManagement.Application.Services;

[CrestService]
[CapabilityCompatibilityProjection(
    ContractType = typeof(IArApLedgerAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/arap-ledger",
    CapabilityIdPrefix = "compat.appservice.arap-ledger")]
public class ArApLedgerAppService(
    IArApLedgerRepository repository,
    IServiceProvider serviceProvider,
    ICurrentUser currentUser,
    IDataPermissionFilter dataPermissionFilter,
    IPermissionChecker permissionChecker,
    ICurrentTenant currentTenant,
    IFeatureChecker features)
    : CrestAppServiceBase<ArApLedger, Guid, ArApLedgerDto, CreateArApLedgerDto, UpdateArApLedgerDto>(
        repository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker), IArApLedgerAppService
{
    protected override async Task CheckPermissionAsync(string permissionName, CancellationToken cancellationToken = default)
    {
        await base.CheckPermissionAsync(permissionName, cancellationToken);
        await features.RequireAsync("Erp.Finance", cancellationToken);
    }

    public override async Task<IReadOnlyList<ArApLedgerDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ErpPermissions.ArApLedger.Search, cancellationToken);
        return (await repository.GetAllWithLinesAsync(currentTenant.GetErpTenantId(), cancellationToken)).Select(MapToDto).ToList();
    }

    public override async Task<ArApLedgerDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ErpPermissions.ArApLedger.Get, cancellationToken);
        var ledger = await repository.GetWithLinesAsync(currentTenant.GetErpTenantId(), id, cancellationToken);
        return ledger is null ? null : MapToDto(ledger);
    }

    [UnitOfWorkMo]
    public async Task<ArApLedgerDto> SettleAsync(Guid id, SettlementInput input, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ErpPermissions.ArApLedger.Settle, cancellationToken);
        if (input.OperationId == Guid.Empty)
            throw new CrestValidationException("结算需要非空的 operationId。");

        var tenantId = currentTenant.GetErpTenantId();
        var ledger = await repository.GetWithLinesForUpdateAsync(tenantId, id, cancellationToken)
            ?? throw new KeyNotFoundException($"台账 {id} 不存在。");
        var recorded = ledger.Settlements.SingleOrDefault(settlement => settlement.TenantId == tenantId && settlement.OperationId == input.OperationId);
        var resultType = ErpOperationReplayJsonContext.Default.ArApLedgerDto;
        if (recorded is not null)
        {
            if (recorded.Amount != input.Amount)
                throw new ErpOperationConflictException();
            return JsonSerializer.Deserialize(recorded.GetOperationResult(), resultType)
                ?? throw new InvalidOperationException("The stored settlement result is empty.");
        }

        var settlement = ledger.Settle(input.Amount, input.OperationId);
        await SetModificationAuditPropertiesAsync(ledger);
        var result = MapToDto(await repository.UpdateAsync(ledger, cancellationToken));
        settlement.RecordOperationResult(JsonSerializer.Serialize(result, resultType));
        return result;
    }

    protected override ArApLedger MapToEntity(CreateArApLedgerDto dto) => throw new NotSupportedException();
    protected override void MapToEntity(UpdateArApLedgerDto dto, ArApLedger entity) => throw new NotSupportedException();

    protected override ArApLedgerDto MapToDto(ArApLedger entity) => new()
    {
        Id = entity.Id,
        Direction = entity.Direction,
        SourceDocType = entity.SourceDocType,
        SourceDocId = entity.SourceDocId,
        PartyId = entity.PartyId,
        Amount = entity.Amount,
        SettledAmount = entity.SettledAmount,
        DueDate = entity.DueDate,
        Status = entity.Status,
        TenantId = entity.TenantId,
        CreationTime = entity.CreationTime,
        ConcurrencyStamp = entity.ConcurrencyStamp,
        Settlements = entity.Settlements.OrderBy(settlement => settlement.SettledAt).Select(settlement => new SettlementRecordDto
        {
            Id = settlement.Id,
            LedgerId = settlement.LedgerId,
            Amount = settlement.Amount,
            SettledAt = settlement.SettledAt,
            TenantId = settlement.TenantId
        }).ToList()
    };
}
