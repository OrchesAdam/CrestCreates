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
using ErpManagement.Domain.Shared.Permissions;

namespace ErpManagement.Application.Services;

[CrestService]
[CapabilityCompatibilityProjection(
    ContractType = typeof(IPurchaseOrderAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/purchase-orders",
    CapabilityIdPrefix = "compat.appservice.purchase-orders")]
public class PurchaseOrderAppService
    : CrestAppServiceBase<PurchaseOrder, Guid, PurchaseOrderDto, CreatePurchaseOrderDto, UpdatePurchaseOrderDto>,
      IPurchaseOrderAppService
{
    private readonly IPurchaseOrderRepository _orderRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly ISettingProvider _settingProvider;

    public PurchaseOrderAppService(
        IPurchaseOrderRepository orderRepository,
        IServiceProvider serviceProvider,
        ICurrentUser currentUser,
        IDataPermissionFilter dataPermissionFilter,
        IPermissionChecker permissionChecker,
        ICurrentTenant currentTenant,
        ISettingProvider settingProvider)
        : base(orderRepository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
        _orderRepository = orderRepository;
        _currentTenant = currentTenant;
        _settingProvider = settingProvider;
    }

    private Guid CurrentTenantId => Guid.TryParse(_currentTenant.Id, out var tid) ? tid : Guid.Empty;

    [UnitOfWorkMo]
    public override async Task<PurchaseOrderDto> CreateAsync(CreatePurchaseOrderDto input, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(CreatePermissionName, cancellationToken);
        var prefix = await _settingProvider.GetOrNullAsync("Erp.PurchaseOrderPrefix", cancellationToken) ?? "PO";
        var number = await DocumentNumberGenerator.GenerateAsync(
            prefix, (n, ct) => _orderRepository.AnyAsync(o => o.Number == n, ct), cancellationToken);
        var order = new PurchaseOrder(Guid.NewGuid(), number, input.SupplierId, CurrentTenantId);
        order.SetRequisition(input.RequisitionId);
        order.SetNotes(input.Notes);
        order.ReplaceLines(BuildLines(input.Lines, order.Id, CurrentTenantId));
        await SetCreationAuditPropertiesAsync(order);
        var created = await _orderRepository.InsertAsync(order, cancellationToken);
        return MapToDto(created);
    }

    public override async Task<PurchaseOrderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ReadPermissionName, cancellationToken);
        var order = await _orderRepository.GetWithLinesAsync(id, cancellationToken);
        return order == null ? null : MapToDto(order);
    }

    public override async Task<IReadOnlyList<PurchaseOrderDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(SearchPermissionName, cancellationToken);
        var orders = await _orderRepository.GetAllWithLinesAsync(cancellationToken);
        return orders.Select(MapToDto).ToList();
    }

    [UnitOfWorkMo]
    public override async Task<PurchaseOrderDto> UpdateAsync(Guid id, UpdatePurchaseOrderDto input, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(UpdatePermissionName, cancellationToken);
        var order = await _orderRepository.GetWithLinesAsync(id, cancellationToken)
                    ?? throw new KeyNotFoundException($"采购单不存在: {id}");
        order.SetRequisition(input.RequisitionId);
        order.SetNotes(input.Notes);
        order.ReplaceLines(BuildLines(input.Lines, order.Id, CurrentTenantId));
        await SetModificationAuditPropertiesAsync(order);
        var updated = await _orderRepository.UpdateAsync(order, cancellationToken);
        return MapToDto(updated);
    }

    [UnitOfWorkMo]
    public async Task<PurchaseOrderDto> ConfirmAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ErpPermissions.PurchaseOrders.Confirm, cancellationToken);
        var order = await _orderRepository.GetWithLinesAsync(id, cancellationToken)
                    ?? throw new KeyNotFoundException($"采购单不存在: {id}");
        order.Confirm();
        await SetModificationAuditPropertiesAsync(order);
        var updated = await _orderRepository.UpdateAsync(order, cancellationToken);
        return MapToDto(updated);
    }

    [UnitOfWorkMo]
    public async Task<PurchaseOrderDto> CloseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ErpPermissions.PurchaseOrders.Close, cancellationToken);
        var order = await _orderRepository.GetWithLinesAsync(id, cancellationToken)
                    ?? throw new KeyNotFoundException($"采购单不存在: {id}");
        order.Close();
        await SetModificationAuditPropertiesAsync(order);
        var updated = await _orderRepository.UpdateAsync(order, cancellationToken);
        return MapToDto(updated);
    }

    private static List<PurchaseOrderLine> BuildLines(List<OrderLineInput> lines, Guid orderId, Guid tenantId)
    {
        return lines.Select(l => new PurchaseOrderLine(
            Guid.NewGuid(), orderId, l.MaterialId, l.Quantity, l.UnitPrice, l.Position,
            l.WarehouseId, l.StorageLocationId, tenantId)).ToList();
    }

    protected override PurchaseOrder MapToEntity(CreatePurchaseOrderDto dto)
    {
        var order = new PurchaseOrder(Guid.NewGuid(), dto.Number, dto.SupplierId, CurrentTenantId);
        order.SetRequisition(dto.RequisitionId);
        order.SetNotes(dto.Notes);
        return order;
    }

    protected override PurchaseOrderDto MapToDto(PurchaseOrder entity)
    {
        return new PurchaseOrderDto
        {
            Id = entity.Id,
            Number = entity.Number,
            RequisitionId = entity.RequisitionId,
            SupplierId = entity.SupplierId,
            Status = entity.Status,
            TotalAmount = entity.TotalAmount,
            Notes = entity.Notes,
            TenantId = entity.TenantId,
            CreationTime = entity.CreationTime,
            ConcurrencyStamp = entity.ConcurrencyStamp,
            Lines = entity.Lines.OrderBy(l => l.Position).Select(l => new PurchaseOrderLineDto
            {
                Id = l.Id,
                OrderId = l.OrderId,
                MaterialId = l.MaterialId,
                Quantity = l.Quantity,
                ReceivedQuantity = l.ReceivedQuantity,
                UnitPrice = l.UnitPrice,
                Amount = l.Amount,
                Position = l.Position,
                WarehouseId = l.WarehouseId,
                StorageLocationId = l.StorageLocationId,
                TenantId = l.TenantId
            }).ToList()
        };
    }

    protected override void MapToEntity(UpdatePurchaseOrderDto dto, PurchaseOrder entity)
    {
        entity.SetRequisition(dto.RequisitionId);
        entity.SetNotes(dto.Notes);
    }
}