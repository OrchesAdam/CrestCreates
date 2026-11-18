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
    ContractType = typeof(IStockCountAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/stock-counts",
    CapabilityIdPrefix = "compat.appservice.stock-counts")]
public class StockCountAppService
    : CrestAppServiceBase<StockCount, Guid, StockCountDto, CreateStockCountDto, UpdateStockCountDto>,
      IStockCountAppService
{
    private readonly IStockCountRepository _countRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly ISettingProvider _settingProvider;
    private readonly StockPostingManager _stockPostingManager;

    public StockCountAppService(
        IStockCountRepository countRepository,
        IServiceProvider serviceProvider,
        ICurrentUser currentUser,
        IDataPermissionFilter dataPermissionFilter,
        IPermissionChecker permissionChecker,
        ICurrentTenant currentTenant,
        ISettingProvider settingProvider,
        StockPostingManager stockPostingManager)
        : base(countRepository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
        _countRepository = countRepository;
        _currentTenant = currentTenant;
        _settingProvider = settingProvider;
        _stockPostingManager = stockPostingManager;
    }

    private Guid CurrentTenantId => Guid.TryParse(_currentTenant.Id, out var tid) ? tid : Guid.Empty;

    [UnitOfWorkMo]
    public override async Task<StockCountDto> CreateAsync(CreateStockCountDto input, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(CreatePermissionName, cancellationToken);
        var number = await DocumentNumberGenerator.GenerateAsync(
            "SC", (n, ct) => _countRepository.AnyAsync(c => c.Number == n, ct), cancellationToken);
        var count = new StockCount(Guid.NewGuid(), number, input.WarehouseId, CurrentTenantId);
        count.SetNotes(input.Notes);
        count.ReplaceLines(BuildLines(input.Lines, count.Id, CurrentTenantId));
        await SetCreationAuditPropertiesAsync(count);
        var created = await _countRepository.InsertAsync(count, cancellationToken);
        return MapToDto(created);
    }

    public override async Task<StockCountDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ReadPermissionName, cancellationToken);
        var count = await _countRepository.GetWithLinesAsync(id, cancellationToken);
        return count == null ? null : MapToDto(count);
    }

    public override async Task<IReadOnlyList<StockCountDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(SearchPermissionName, cancellationToken);
        var counts = await _countRepository.GetAllWithLinesAsync(cancellationToken);
        return counts.Select(MapToDto).ToList();
    }

    [UnitOfWorkMo]
    public override async Task<StockCountDto> UpdateAsync(Guid id, UpdateStockCountDto input, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(UpdatePermissionName, cancellationToken);
        var count = await _countRepository.GetWithLinesAsync(id, cancellationToken)
                    ?? throw new KeyNotFoundException($"盘点单不存在: {id}");
        count.SetNotes(input.Notes);
        count.ReplaceLines(BuildLines(input.Lines, count.Id, CurrentTenantId));
        await SetModificationAuditPropertiesAsync(count);
        var updated = await _countRepository.UpdateAsync(count, cancellationToken);
        return MapToDto(updated);
    }

    [UnitOfWorkMo]
    public async Task<StockCountDto> PostAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ErpPermissions.StockCounts.Post, cancellationToken);
        var count = await _countRepository.GetWithLinesAsync(id, cancellationToken)
                    ?? throw new KeyNotFoundException($"盘点单不存在: {id}");
        if (count.Status != StockCountStatus.Draft)
        {
            throw new InvalidOperationException("只有草稿盘点单可以过账。");
        }

        var allowNegativeValue = await _settingProvider.GetOrNullAsync("Erp.AllowNegativeStockGlobal", cancellationToken);
        var allowNegativeStock = bool.TryParse(allowNegativeValue, out var parsed) && parsed;

        // Only non-zero differences post: gain (+delta) or loss (-delta), signed quantity = delta.
        var movements = count.Lines
            .Where(l => l.Difference != 0)
            .Select(l => new StockMovementInput(
                l.MaterialId, count.WarehouseId, l.StorageLocationId, l.Difference,
                l.Difference > 0 ? StockMovementType.CountGain : StockMovementType.CountLoss))
            .ToList();

        await _stockPostingManager.PostAsync(
            CurrentTenantId, movements, allowNegativeStock, "StockCount", count.Id, cancellationToken);

        count.Post();
        await SetModificationAuditPropertiesAsync(count);
        var updated = await _countRepository.UpdateAsync(count, cancellationToken);
        return MapToDto(updated);
    }

    private static List<StockCountLine> BuildLines(List<CountLineInput> lines, Guid countId, Guid tenantId)
    {
        return lines.Select(l => new StockCountLine(
            Guid.NewGuid(), countId, l.MaterialId, l.StorageLocationId,
            l.BookQuantity, l.CountedQuantity, tenantId)).ToList();
    }

    protected override StockCount MapToEntity(CreateStockCountDto dto)
    {
        var count = new StockCount(Guid.NewGuid(), dto.Number, dto.WarehouseId, CurrentTenantId);
        count.SetNotes(dto.Notes);
        return count;
    }

    protected override StockCountDto MapToDto(StockCount entity)
    {
        return new StockCountDto
        {
            Id = entity.Id,
            Number = entity.Number,
            WarehouseId = entity.WarehouseId,
            Status = entity.Status,
            Notes = entity.Notes,
            TenantId = entity.TenantId,
            CreationTime = entity.CreationTime,
            ConcurrencyStamp = entity.ConcurrencyStamp,
            Lines = entity.Lines.Select(l => new StockCountLineDto
            {
                Id = l.Id,
                CountId = l.CountId,
                MaterialId = l.MaterialId,
                StorageLocationId = l.StorageLocationId,
                BookQuantity = l.BookQuantity,
                CountedQuantity = l.CountedQuantity,
                Difference = l.Difference,
                TenantId = l.TenantId
            }).ToList()
        };
    }

    protected override void MapToEntity(UpdateStockCountDto dto, StockCount entity)
    {
        entity.SetNotes(dto.Notes);
    }
}