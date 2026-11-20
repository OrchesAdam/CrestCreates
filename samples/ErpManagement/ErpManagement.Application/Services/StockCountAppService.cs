using CrestCreates.Aop.Interceptors;
using CrestCreates.Application.Services;
using CrestCreates.Authorization;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Repositories;
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
    private readonly ICrestRepositoryBase<StockBalance, Guid> _balances;
    private readonly ICurrentTenant _currentTenant;
    private readonly ISettingProvider _settingProvider;
    private readonly StockPostingManager _stockPostingManager;

    public StockCountAppService(
        IStockCountRepository countRepository,
        ICrestRepositoryBase<StockBalance, Guid> balances,
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
        _balances = balances;
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
        await SetLinesAsync(count, input.Lines, cancellationToken);
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

    protected override bool RequiresConcurrencyStampOnUpdate => true;

    [UnitOfWorkMo]
    public async Task<StockCountDto> UpdateAsync(Guid id, UpdateStockCountDto input, string? expectedStamp = null, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(UpdatePermissionName, cancellationToken);
        var count = await _countRepository.GetWithLinesAsync(id, cancellationToken)
                    ?? throw new KeyNotFoundException($"盘点单不存在: {id}");
        if (string.IsNullOrWhiteSpace(expectedStamp))
            throw new CrestCreates.Domain.Exceptions.CrestPreconditionRequiredException(nameof(StockCount), id);
        if (count.ConcurrencyStamp != expectedStamp)
            throw new CrestCreates.Domain.Exceptions.CrestConcurrencyException(nameof(StockCount), id);
        var originalStamp = count.ConcurrencyStamp;
        count.SetNotes(input.Notes);
        await SetLinesAsync(count, input.Lines, cancellationToken);
        count.ConcurrencyStamp = originalStamp;
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

        if (count.Lines.Select(l => (l.MaterialId, l.StorageLocationId)).Distinct().Count() != count.Lines.Count)
            throw new InvalidOperationException("盘点单不能包含重复的物料和库位组合。");

        // Apply the discrepancy captured by the count while preserving movements after its snapshot.
        var movements = count.Lines
            .Where(l => l.Difference != 0)
            .Select(l => new StockMovementInput(
                l.MaterialId, count.WarehouseId, l.StorageLocationId, l.Difference,
                l.Difference > 0 ? StockMovementType.CountGain : StockMovementType.CountLoss))
            .ToList();

        await _stockPostingManager.PostAsync(
            count.TenantId, movements, allowNegativeStock, nameof(StockCount), count.Id, cancellationToken);

        count.Post();
        await SetModificationAuditPropertiesAsync(count);
        var updated = await _countRepository.UpdateAsync(count, cancellationToken);
        return MapToDto(updated);
    }

    private async Task SetLinesAsync(StockCount count, List<CountLineInput> inputs, CancellationToken cancellationToken)
    {
        if (count.Status != StockCountStatus.Draft)
            throw new InvalidOperationException("只有草稿盘点单可以编辑。");
        if (inputs.Select(l => (l.MaterialId, l.StorageLocationId)).Distinct().Count() != inputs.Count)
            throw new ArgumentException("盘点单不能包含重复的物料和库位组合。", nameof(inputs));
        if (inputs.Any(l => l.CountedQuantity < 0))
            throw new ArgumentException("实盘数量不能为负数。", nameof(inputs));

        var existing = count.Lines.GroupBy(l => (l.MaterialId, l.StorageLocationId))
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());
        var newMaterialIds = inputs.Where(l => !existing.ContainsKey((l.MaterialId, l.StorageLocationId)))
            .Select(l => l.MaterialId).Distinct().ToArray();
        var snapshot = newMaterialIds.Length == 0
            ? new Dictionary<(Guid, Guid), decimal>()
            : (await _balances.GetListAsync(b => b.TenantId == count.TenantId && b.WarehouseId == count.WarehouseId
                && newMaterialIds.Contains(b.MaterialId), cancellationToken))
                .ToDictionary(b => (b.MaterialId, b.StorageLocationId), b => b.Quantity);

        var lines = new List<StockCountLine>(inputs.Count);
        foreach (var input in inputs)
        {
            var key = (input.MaterialId, input.StorageLocationId);
            if (existing.TryGetValue(key, out var line))
            {
                if (line.CountedQuantity != input.CountedQuantity)
                {
                    line.SetCountedQuantity(input.CountedQuantity);
                    await SetModificationAuditPropertiesAsync(line);
                }
            }
            else
            {
                line = new StockCountLine(Guid.NewGuid(), count.Id, input.MaterialId, input.StorageLocationId,
                    snapshot.GetValueOrDefault(key), input.CountedQuantity, count.TenantId);
                await SetCreationAuditPropertiesAsync(line);
            }
            lines.Add(line);
        }
        var desiredIds = new HashSet<Guid>(lines.Select(l => l.Id));
        foreach (var stale in count.Lines.Where(l => !desiredIds.Contains(l.Id)).ToList())
            count.Lines.Remove(stale);
        foreach (var line in lines)
            if (!count.Lines.Contains(line))
                count.Lines.Add(line);
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