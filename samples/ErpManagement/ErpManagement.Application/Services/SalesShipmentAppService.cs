using CrestCreates.Aop.Interceptors;
using CrestCreates.Application.Services;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Settings;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.DataFilter;
using CrestCreates.Domain.Shared.Exceptions;
using CrestCreates.MultiTenancy.Abstract;
using ErpManagement.Application.Contracts.DTOs;
using ErpManagement.Application.Contracts.Interfaces;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Shared.Enums;
using ErpManagement.Domain.Shared.Permissions;
using CrestCreates.EventBus.Abstractions;
using ErpManagement.Application.Contracts.Events;
using ErpManagement.Domain.Inventory;

namespace ErpManagement.Application.Services;

[CrestService]
[CapabilityCompatibilityProjection(
    ContractType = typeof(ISalesShipmentAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/sales-shipments",
    CapabilityIdPrefix = "compat.appservice.sales-shipments")]
public class SalesShipmentAppService(
    ISalesShipmentRepository repository,
    IServiceProvider serviceProvider,
    ICurrentUser currentUser,
    IDataPermissionFilter dataPermissionFilter,
    IPermissionChecker permissionChecker,
    ICurrentTenant currentTenant,
    IFeatureChecker features,
    ISalesOrderRepository orders,
    IArApLedgerRepository ledgers,
    StockPostingManager stockPosting,
    ISettingProvider settings,
    ILocalEventBus localEvents)
    : CrestAppServiceBase<SalesShipment, Guid, SalesShipmentDto, CreateSalesShipmentDto, UpdateSalesShipmentDto>(
        repository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker), ISalesShipmentAppService
{
    private Guid TenantId => currentTenant.GetErpTenantId();

    protected override async Task CheckPermissionAsync(string permissionName, CancellationToken cancellationToken = default)
    {
        await base.CheckPermissionAsync(permissionName, cancellationToken);
        await features.RequireAsync("Erp.Sales", cancellationToken);
    }

    [UnitOfWorkMo]
    public override async Task<SalesShipmentDto> CreateAsync(CreateSalesShipmentDto input, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(CreatePermissionName, cancellationToken);
        var tenantId = TenantId;
        var number = await DocumentNumberGenerator.GenerateAsync("SS",
            (n, ct) => repository.AnyAsync(entity => entity.TenantId == tenantId && entity.Number == n, ct), cancellationToken);
        var order = await LoadOrderAsync(input.SalesOrderId, cancellationToken);
        var entity = new SalesShipment(Guid.NewGuid(), number, order.Id, order.CustomerId, input.WarehouseId, TenantId);
        entity.SetDraftDetails(order, input.WarehouseId, input.Notes);
        entity.ReplaceLines(BuildLines(entity, order, input.Lines));
        await SetCreationAuditPropertiesAsync(entity);
        return MapToDto(await repository.InsertAsync(entity, cancellationToken));
    }

    public override async Task<SalesShipmentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ReadPermissionName, cancellationToken);
        var entity = await repository.GetWithLinesAsync(TenantId, id, cancellationToken);
        return entity is null ? null : MapToDto(entity);
    }

    public override async Task<IReadOnlyList<SalesShipmentDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(SearchPermissionName, cancellationToken);
        return (await repository.GetAllWithLinesAsync(TenantId, cancellationToken)).Select(MapToDto).ToList();
    }

    [UnitOfWorkMo]
    public override async Task<SalesShipmentDto> UpdateAsync(Guid id, UpdateSalesShipmentDto input, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(UpdatePermissionName, cancellationToken);
        var entity = await LoadAsync(id, cancellationToken);
        entity.RequireDraft();
        if (!string.IsNullOrEmpty(input.ConcurrencyStamp) && input.ConcurrencyStamp != entity.ConcurrencyStamp)
            throw new CrestConcurrencyException(nameof(SalesShipment), id);
        var order = await LoadOrderAsync(input.SalesOrderId, cancellationToken);
        entity.SetDraftDetails(order, input.WarehouseId, input.Notes);
        entity.ReplaceLines(BuildLines(entity, order, input.Lines));
        return await SaveAsync(entity, cancellationToken);
    }

    [UnitOfWorkMo]
    public override async Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(DeletePermissionName, cancellationToken);
        var entity = await LoadAsync(id, cancellationToken);
        entity.RequireDraft();
        if (string.IsNullOrWhiteSpace(expectedStamp)) throw new CrestPreconditionRequiredException(nameof(SalesShipment), id);
        await repository.DeleteAsync(id, expectedStamp, cancellationToken);
    }

    private async Task<SalesShipment> LoadAsync(Guid id, CancellationToken ct)
        => await repository.GetWithLinesAsync(TenantId, id, ct)
            ?? throw new KeyNotFoundException($"SalesShipment {id} 不存在。");

    private async Task<SalesShipmentDto> SaveAsync(SalesShipment entity, CancellationToken ct)
    {
        await SetModificationAuditPropertiesAsync(entity);
        return MapToDto(await repository.UpdateAsync(entity, ct));
    }

    public async Task<SalesShipmentDto> PostAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var (shipment, results) = await PostCoreAsync(id, cancellationToken);
        foreach (var movement in results)
        {
            await localEvents.PublishAsync(new StockChanged
            {
                MaterialId = movement.MaterialId,
                WarehouseId = movement.WarehouseId,
                StorageLocationId = movement.StorageLocationId,
                NewQuantity = movement.NewQuantity,
                MovementType = movement.MovementType.ToString(),
                SourceDocId = id
            }, cancellationToken);
        }
        return shipment;
    }

    [UnitOfWorkMo]
    private async Task<(SalesShipmentDto Shipment, IReadOnlyList<StockMovementResult> Movements)> PostCoreAsync(
        Guid id, CancellationToken cancellationToken)
    {
        await CheckPermissionAsync(ErpPermissions.SalesShipments.Post, cancellationToken);
        await CheckPermissionAsync(ErpPermissions.SalesOrders.Ship, cancellationToken);
        var shipment = await LoadAsync(id, cancellationToken);
        shipment.RequireDraft();
        var order = await LoadOrderAsync(shipment.SalesOrderId, cancellationToken);
        var quantities = shipment.Lines.GroupBy(line => line.SalesOrderLineId)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));
        order.RecordShipment(quantities);

        var allowNegative = bool.TryParse(await settings.GetOrNullAsync("Erp.AllowNegativeStockGlobal", cancellationToken), out var allowed) && allowed;
        var movements = shipment.Lines.GroupBy(line => (line.MaterialId, line.StorageLocationId))
            .Select(group => new StockMovementInput(group.Key.MaterialId, shipment.WarehouseId,
                group.Key.StorageLocationId, -group.Sum(line => line.Quantity), StockMovementType.Issue)).ToList();
        var results = await stockPosting.PostAsync(TenantId, movements, allowNegative,
            "SalesShipment", shipment.Id, cancellationToken);
        await SetModificationAuditPropertiesAsync(order);
        await orders.UpdateAsync(order, cancellationToken);

        if (await features.IsEnabledAsync("Erp.Finance", cancellationToken))
        {
            var ledger = new ArApLedger(Guid.NewGuid(), ArApDirection.Receivable, "SalesShipment", shipment.Id,
                shipment.CustomerId, shipment.Lines.Sum(line => line.Quantity * line.UnitPrice), TenantId);
            await SetCreationAuditPropertiesAsync(ledger);
            await ledgers.InsertAsync(ledger, cancellationToken);
        }

        shipment.Post(DateTimeOffset.UtcNow);
        return (await SaveAsync(shipment, cancellationToken), results);
    }

    private async Task<SalesOrder> LoadOrderAsync(Guid orderId, CancellationToken ct)
        => await orders.GetWithLinesAsync(TenantId, orderId, ct)
            ?? throw new KeyNotFoundException($"销售订单 {orderId} 不存在。");

    private List<SalesShipmentLine> BuildLines(SalesShipment shipment, SalesOrder order, List<SalesShipmentLineInput> lines)
    {
        if (order.Status is not (SalesOrderStatus.Approved or SalesOrderStatus.PartiallyShipped))
            throw new CrestValidationException("销售订单当前不可发货。");
        if (lines is null || lines.Count == 0) throw new CrestValidationException("发货单必须包含至少一行。");
        if (lines.Select(line => line.SalesOrderLineId).Distinct().Count() != lines.Count)
            throw new CrestValidationException("同一订单行只能在发货单中出现一次。");

        var byId = order.Lines.ToDictionary(line => line.Id);
        var result = new List<SalesShipmentLine>(lines.Count);
        foreach (var input in lines)
        {
            if (!byId.TryGetValue(input.SalesOrderLineId, out var line) || line.WarehouseId != shipment.WarehouseId)
                throw new CrestValidationException("发货行必须引用该销售订单及所选仓库的订单行。");
            if (input.Quantity <= 0 || decimal.Round(input.Quantity, 4) != input.Quantity
                || input.Quantity > line.Quantity - line.ShippedQuantity)
                throw new CrestValidationException("发货数量无效或超过待发数量。");
            result.Add(new SalesShipmentLine(Guid.NewGuid(), shipment.Id, line.Id, line.MaterialId,
                line.StorageLocationId, input.Quantity, line.UnitPrice, TenantId));
        }
        return result;
    }

    protected override SalesShipment MapToEntity(CreateSalesShipmentDto dto)
    {
        throw new NotSupportedException("A shipment requires validated sales order lines.");
    }

    protected override void MapToEntity(UpdateSalesShipmentDto dto, SalesShipment entity)
    {
        throw new NotSupportedException("A shipment requires validated sales order lines.");
    }

    protected override SalesShipmentDto MapToDto(SalesShipment entity) => new()
    {
        Id = entity.Id,
        Number = entity.Number,
        SalesOrderId = entity.SalesOrderId,
        CustomerId = entity.CustomerId,
        WarehouseId = entity.WarehouseId,
        Status = entity.Status,
        PostedAt = entity.PostedAt,
        Notes = entity.Notes,
        TenantId = entity.TenantId,
        CreationTime = entity.CreationTime,
        CreatorId = entity.CreatorId,
        LastModificationTime = entity.LastModificationTime,
        LastModifierId = entity.LastModifierId,
        ConcurrencyStamp = entity.ConcurrencyStamp,
        Lines = entity.Lines.Select(child => new SalesShipmentLineDto
        {
            Id = child.Id,
            ShipmentId = child.ShipmentId,
            SalesOrderLineId = child.SalesOrderLineId,
            MaterialId = child.MaterialId,
            StorageLocationId = child.StorageLocationId,
            Quantity = child.Quantity,
            UnitPrice = child.UnitPrice,
            TenantId = child.TenantId,
            CreationTime = child.CreationTime,
            ConcurrencyStamp = child.ConcurrencyStamp,
        }).ToList(),
    };
}
