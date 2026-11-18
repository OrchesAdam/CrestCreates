using CrestCreates.Aop.Interceptors;
using CrestCreates.Application.Services;
using CrestCreates.Authorization;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Settings;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.DataFilter;
using CrestCreates.MultiTenancy.Abstract;
using ErpManagement.Application.Contracts.DTOs;
using ErpManagement.Application.Contracts.Interfaces;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Inventory;
using ErpManagement.Domain.Shared.Enums;
using ErpManagement.Domain.Shared.Permissions;

namespace ErpManagement.Application.Services;

[CrestService]
[CapabilityCompatibilityProjection(
    ContractType = typeof(IStockTransferAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/stock-transfers",
    CapabilityIdPrefix = "compat.appservice.stock-transfers")]
public class StockTransferAppService
    : CrestAppServiceBase<StockTransfer, Guid, StockTransferDto, CreateStockTransferDto, UpdateStockTransferDto>,
      IStockTransferAppService
{
    private readonly IStockTransferRepository _transferRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly ISettingProvider _settingProvider;
    private readonly StockPostingManager _stockPostingManager;

    public StockTransferAppService(
        IStockTransferRepository transferRepository,
        IServiceProvider serviceProvider,
        ICurrentUser currentUser,
        IDataPermissionFilter dataPermissionFilter,
        IPermissionChecker permissionChecker,
        ICurrentTenant currentTenant,
        ISettingProvider settingProvider,
        StockPostingManager stockPostingManager)
        : base(transferRepository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
        _transferRepository = transferRepository;
        _currentTenant = currentTenant;
        _settingProvider = settingProvider;
        _stockPostingManager = stockPostingManager;
    }

    private Guid CurrentTenantId => Guid.TryParse(_currentTenant.Id, out var tid) ? tid : Guid.Empty;

    [UnitOfWorkMo]
    public override async Task<StockTransferDto> CreateAsync(CreateStockTransferDto input, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(CreatePermissionName, cancellationToken);
        var number = await DocumentNumberGenerator.GenerateAsync(
            "ST", (n, ct) => _transferRepository.AnyAsync(t => t.Number == n, ct), cancellationToken);
        var transfer = new StockTransfer(Guid.NewGuid(), number, CurrentTenantId);
        transfer.SetNotes(input.Notes);
        transfer.ReplaceLines(BuildLines(input.Lines, transfer.Id, CurrentTenantId));
        await SetCreationAuditPropertiesAsync(transfer);
        var created = await _transferRepository.InsertAsync(transfer, cancellationToken);
        return MapToDto(created);
    }

    public override async Task<StockTransferDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ReadPermissionName, cancellationToken);
        var transfer = await _transferRepository.GetWithLinesAsync(id, cancellationToken);
        return transfer == null ? null : MapToDto(transfer);
    }

    public override async Task<IReadOnlyList<StockTransferDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(SearchPermissionName, cancellationToken);
        var transfers = await _transferRepository.GetAllWithLinesAsync(cancellationToken);
        return transfers.Select(MapToDto).ToList();
    }

    [UnitOfWorkMo]
    public override async Task<StockTransferDto> UpdateAsync(Guid id, UpdateStockTransferDto input, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(UpdatePermissionName, cancellationToken);
        var transfer = await _transferRepository.GetWithLinesAsync(id, cancellationToken)
                       ?? throw new KeyNotFoundException($"调拨单不存在: {id}");
        transfer.SetNotes(input.Notes);
        transfer.ReplaceLines(BuildLines(input.Lines, transfer.Id, CurrentTenantId));
        await SetModificationAuditPropertiesAsync(transfer);
        var updated = await _transferRepository.UpdateAsync(transfer, cancellationToken);
        return MapToDto(updated);
    }

    [UnitOfWorkMo]
    public async Task<StockTransferDto> PostAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ErpPermissions.StockTransfers.Post, cancellationToken);
        var transfer = await _transferRepository.GetWithLinesAsync(id, cancellationToken)
                       ?? throw new KeyNotFoundException($"调拨单不存在: {id}");
        if (transfer.Status != StockTransferStatus.Draft)
        {
            throw new InvalidOperationException("只有草稿调拨单可以过账。");
        }

        var allowNegativeValue = await _settingProvider.GetOrNullAsync("Erp.AllowNegativeStockGlobal", cancellationToken);
        var allowNegativeStock = bool.TryParse(allowNegativeValue, out var parsed) && parsed;

        // Double entry per line: source -q (TransferOut) and destination +q (TransferIn), one UoW.
        var movements = new List<StockMovementInput>();
        foreach (var line in transfer.Lines)
        {
            movements.Add(new StockMovementInput(
                line.MaterialId, line.FromWarehouseId, line.FromStorageLocationId, -line.Quantity, StockMovementType.TransferOut));
            movements.Add(new StockMovementInput(
                line.MaterialId, line.ToWarehouseId, line.ToStorageLocationId, line.Quantity, StockMovementType.TransferIn));
        }

        await _stockPostingManager.PostAsync(
            CurrentTenantId, movements, allowNegativeStock, "StockTransfer", transfer.Id, cancellationToken);

        transfer.Post();
        await SetModificationAuditPropertiesAsync(transfer);
        var updated = await _transferRepository.UpdateAsync(transfer, cancellationToken);
        return MapToDto(updated);
    }

    private static List<StockTransferLine> BuildLines(List<TransferLineInput> lines, Guid transferId, Guid tenantId)
    {
        return lines.Select(l => new StockTransferLine(
            Guid.NewGuid(), transferId, l.MaterialId, l.FromWarehouseId, l.FromStorageLocationId,
            l.ToWarehouseId, l.ToStorageLocationId, l.Quantity, tenantId)).ToList();
    }

    protected override StockTransfer MapToEntity(CreateStockTransferDto dto)
    {
        var transfer = new StockTransfer(Guid.NewGuid(), dto.Number, CurrentTenantId);
        transfer.SetNotes(dto.Notes);
        return transfer;
    }

    protected override StockTransferDto MapToDto(StockTransfer entity)
    {
        return new StockTransferDto
        {
            Id = entity.Id,
            Number = entity.Number,
            Status = entity.Status,
            Notes = entity.Notes,
            TenantId = entity.TenantId,
            CreationTime = entity.CreationTime,
            ConcurrencyStamp = entity.ConcurrencyStamp,
            Lines = entity.Lines.Select(l => new StockTransferLineDto
            {
                Id = l.Id,
                TransferId = l.TransferId,
                MaterialId = l.MaterialId,
                FromWarehouseId = l.FromWarehouseId,
                FromStorageLocationId = l.FromStorageLocationId,
                ToWarehouseId = l.ToWarehouseId,
                ToStorageLocationId = l.ToStorageLocationId,
                Quantity = l.Quantity,
                TenantId = l.TenantId
            }).ToList()
        };
    }

    protected override void MapToEntity(UpdateStockTransferDto dto, StockTransfer entity)
    {
        entity.SetNotes(dto.Notes);
    }
}