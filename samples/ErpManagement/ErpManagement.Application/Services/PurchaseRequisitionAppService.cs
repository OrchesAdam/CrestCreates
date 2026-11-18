using CrestCreates.Aop.Interceptors;
using CrestCreates.Application.Services;
using CrestCreates.Authorization;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Settings;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.DataFilter;
using CrestCreates.MultiTenancy.Abstract;
using ErpManagement.Application.Approval;
using ErpManagement.Application.Contracts.DTOs;
using ErpManagement.Application.Contracts.Interfaces;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Shared.Enums;
using ErpManagement.Domain.Shared.Permissions;

namespace ErpManagement.Application.Services;

[CrestService]
[CapabilityCompatibilityProjection(
    ContractType = typeof(IPurchaseRequisitionAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/purchase-requisitions",
    CapabilityIdPrefix = "compat.appservice.purchase-requisitions")]
public class PurchaseRequisitionAppService
    : CrestAppServiceBase<PurchaseRequisition, Guid, PurchaseRequisitionDto, CreatePurchaseRequisitionDto, UpdatePurchaseRequisitionDto>,
      IPurchaseRequisitionAppService
{
    private const string ApprovalThresholdSetting = "Erp.PurchaseRequisitionApprovalThreshold";
    private const int DefaultApprovalThreshold = 10000;

    private readonly IPurchaseRequisitionRepository _requisitionRepository;
    private readonly IPurchaseOrderRepository _orderRepository;
    private readonly ICrestRepositoryBase<Material, Guid> _materialRepository;
    private readonly ICrestRepositoryBase<StorageLocation, Guid> _storageLocationRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly ISettingProvider _settingProvider;
    private readonly ErpRequisitionApprovalOrchestrator _approvalOrchestrator;

    public PurchaseRequisitionAppService(
        IPurchaseRequisitionRepository requisitionRepository,
        IPurchaseOrderRepository orderRepository,
        ICrestRepositoryBase<Material, Guid> materialRepository,
        ICrestRepositoryBase<StorageLocation, Guid> storageLocationRepository,
        IServiceProvider serviceProvider,
        ICurrentUser currentUser,
        IDataPermissionFilter dataPermissionFilter,
        IPermissionChecker permissionChecker,
        ICurrentTenant currentTenant,
        ISettingProvider settingProvider,
        ErpRequisitionApprovalOrchestrator approvalOrchestrator)
        : base(requisitionRepository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
        _requisitionRepository = requisitionRepository;
        _orderRepository = orderRepository;
        _materialRepository = materialRepository;
        _storageLocationRepository = storageLocationRepository;
        _currentTenant = currentTenant;
        _settingProvider = settingProvider;
        _approvalOrchestrator = approvalOrchestrator;
    }

    private Guid CurrentTenantId => Guid.TryParse(_currentTenant.Id, out var tid) ? tid : Guid.Empty;

    [UnitOfWorkMo]
    public override async Task<PurchaseRequisitionDto> CreateAsync(CreatePurchaseRequisitionDto input, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(CreatePermissionName, cancellationToken);
        var requesterId = !string.IsNullOrWhiteSpace(CurrentUser.Id) ? CurrentUser.Id : input.RequesterId;
        var number = await DocumentNumberGenerator.GenerateAsync(
            "PR", (n, ct) => _requisitionRepository.AnyAsync(r => r.Number == n, ct), cancellationToken);
        var requisition = new PurchaseRequisition(Guid.NewGuid(), number, requesterId, CurrentTenantId);
        requisition.SetSupplier(input.SupplierId);
        requisition.SetNotes(input.Notes);
        requisition.ReplaceLines(BuildLines(input.Lines, requisition.Id, CurrentTenantId));
        await SetCreationAuditPropertiesAsync(requisition);
        var created = await _requisitionRepository.InsertAsync(requisition, cancellationToken);
        return MapToDto(created);
    }

    public override async Task<PurchaseRequisitionDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ReadPermissionName, cancellationToken);
        var requisition = await _requisitionRepository.GetWithLinesAsync(id, cancellationToken);
        return requisition == null ? null : MapToDto(requisition);
    }

    public override async Task<IReadOnlyList<PurchaseRequisitionDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(SearchPermissionName, cancellationToken);
        var requisitions = await _requisitionRepository.GetAllWithLinesAsync(cancellationToken);
        return requisitions.Select(MapToDto).ToList();
    }

    [UnitOfWorkMo]
    public override async Task<PurchaseRequisitionDto> UpdateAsync(Guid id, UpdatePurchaseRequisitionDto input, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(UpdatePermissionName, cancellationToken);
        var requisition = await _requisitionRepository.GetWithLinesAsync(id, cancellationToken)
                          ?? throw new KeyNotFoundException($"请购单不存在: {id}");
        requisition.SetSupplier(input.SupplierId);
        requisition.SetNotes(input.Notes);
        requisition.ReplaceLines(BuildLines(input.Lines, requisition.Id, CurrentTenantId));
        await SetModificationAuditPropertiesAsync(requisition);
        var updated = await _requisitionRepository.UpdateAsync(requisition, cancellationToken);
        return MapToDto(updated);
    }

    [UnitOfWorkMo]
    public async Task<PurchaseRequisitionDto> SubmitAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ErpPermissions.PurchaseRequisitions.Submit, cancellationToken);
        var requisition = await _requisitionRepository.GetWithLinesAsync(id, cancellationToken)
                          ?? throw new KeyNotFoundException($"请购单不存在: {id}");

        var thresholdValue = await _settingProvider.GetOrNullAsync(ApprovalThresholdSetting, cancellationToken);
        var threshold = int.TryParse(thresholdValue, out var parsed) ? parsed : DefaultApprovalThreshold;

        requisition.Submit();
        if (requisition.TotalAmount <= threshold)
        {
            requisition.Approve("system", "自动批准");
            await SetModificationAuditPropertiesAsync(requisition);
            var approved = await _requisitionRepository.UpdateAsync(requisition, cancellationToken);
            return MapToDto(approved);
        }

        var lease = await _approvalOrchestrator.StartAsync(requisition, cancellationToken);
        requisition.MarkPendingApproval(lease.WorkflowKey.InstanceId, lease.HumanTaskKey.InstanceId);
        await SetModificationAuditPropertiesAsync(requisition);
        try
        {
            var updated = await _requisitionRepository.UpdateAsync(requisition, cancellationToken);
            return MapToDto(updated);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The workflow already suspended on a HumanTask; compensate so no orphan task remains.
            await _approvalOrchestrator.AbortAsync(
                lease, "ERP requisition correlation persistence failed.", CancellationToken.None);
            throw;
        }
    }

    [UnitOfWorkMo]
    public async Task<PurchaseOrderDto> ConvertAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ErpPermissions.PurchaseRequisitions.Convert, cancellationToken);
        var requisition = await _requisitionRepository.GetWithLinesAsync(id, cancellationToken)
                          ?? throw new KeyNotFoundException($"请购单不存在: {id}");
        if (requisition.Status != RequisitionStatus.Approved)
        {
            throw new InvalidOperationException("只有已批准的请购单可以转采购单。");
        }

        var supplierId = requisition.SupplierId
                         ?? throw new InvalidOperationException("请购单未指定供应商，无法转采购单。");
        var prefix = await _settingProvider.GetOrNullAsync("Erp.PurchaseOrderPrefix", cancellationToken) ?? "PO";
        var number = await DocumentNumberGenerator.GenerateAsync(
            prefix, (n, ct) => _orderRepository.AnyAsync(o => o.Number == n, ct), cancellationToken);

        var orderId = Guid.NewGuid();
        var tenantId = CurrentTenantId;
        var order = new PurchaseOrder(orderId, number, supplierId, tenantId);
        order.SetRequisition(requisition.Id);
        order.SetNotes(requisition.Notes);

        var lines = new List<PurchaseOrderLine>();
        foreach (var requisitionLine in requisition.Lines.OrderBy(l => l.Position))
        {
            var material = await _materialRepository.GetAsync(requisitionLine.MaterialId, cancellationToken)
                           ?? throw new KeyNotFoundException($"物料不存在: {requisitionLine.MaterialId}");
            var warehouseId = material.DefaultWarehouseId
                              ?? throw new InvalidOperationException($"物料 '{material.Code}' 未设置默认仓库，无法转采购单。");
            var storageLocation = _storageLocationRepository.GetQueryable()
                                      .FirstOrDefault(l => l.WarehouseId == warehouseId)
                                  ?? throw new InvalidOperationException($"仓库 '{warehouseId}' 没有库位，无法转采购单。");
            lines.Add(new PurchaseOrderLine(
                Guid.NewGuid(), orderId, requisitionLine.MaterialId, requisitionLine.Quantity,
                requisitionLine.UnitPrice, requisitionLine.Position, warehouseId, storageLocation.Id, tenantId));
        }

        order.ReplaceLines(lines);
        await SetCreationAuditPropertiesAsync(order);
        var created = await _orderRepository.InsertAsync(order, cancellationToken);

        requisition.MarkConverted(orderId);
        await SetModificationAuditPropertiesAsync(requisition);
        await _requisitionRepository.UpdateAsync(requisition, cancellationToken);

        return new PurchaseOrderDto
        {
            Id = created.Id,
            Number = created.Number,
            RequisitionId = created.RequisitionId,
            SupplierId = created.SupplierId,
            Status = created.Status,
            TotalAmount = created.TotalAmount,
            Notes = created.Notes,
            TenantId = created.TenantId,
            CreationTime = created.CreationTime,
            ConcurrencyStamp = created.ConcurrencyStamp,
            Lines = created.Lines.OrderBy(l => l.Position).Select(l => new PurchaseOrderLineDto
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

    private static List<PurchaseRequisitionLine> BuildLines(List<RequisitionLineInput> lines, Guid requisitionId, Guid tenantId)
    {
        return lines.Select(l => new PurchaseRequisitionLine(
            Guid.NewGuid(), requisitionId, l.MaterialId, l.Quantity, l.UnitPrice, l.Position, tenantId)).ToList();
    }

    protected override PurchaseRequisition MapToEntity(CreatePurchaseRequisitionDto dto)
    {
        var requisition = new PurchaseRequisition(
            Guid.NewGuid(), dto.Number, dto.RequesterId, CurrentTenantId);
        requisition.SetSupplier(dto.SupplierId);
        requisition.SetNotes(dto.Notes);
        return requisition;
    }

    protected override PurchaseRequisitionDto MapToDto(PurchaseRequisition entity)
    {
        return new PurchaseRequisitionDto
        {
            Id = entity.Id,
            Number = entity.Number,
            RequesterId = entity.RequesterId,
            SupplierId = entity.SupplierId,
            TotalAmount = entity.TotalAmount,
            Status = entity.Status,
            WorkflowInstanceId = entity.WorkflowInstanceId,
            HumanTaskInstanceId = entity.HumanTaskInstanceId,
            ApproverId = entity.ApproverId,
            ApprovalComment = entity.ApprovalComment,
            ConvertedOrderId = entity.ConvertedOrderId,
            Notes = entity.Notes,
            TenantId = entity.TenantId,
            CreationTime = entity.CreationTime,
            ConcurrencyStamp = entity.ConcurrencyStamp,
            Lines = entity.Lines.OrderBy(l => l.Position).Select(l => new PurchaseRequisitionLineDto
            {
                Id = l.Id,
                RequisitionId = l.RequisitionId,
                MaterialId = l.MaterialId,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                Amount = l.Amount,
                Position = l.Position,
                TenantId = l.TenantId
            }).ToList()
        };
    }

    protected override void MapToEntity(UpdatePurchaseRequisitionDto dto, PurchaseRequisition entity)
    {
        entity.SetSupplier(dto.SupplierId);
        entity.SetNotes(dto.Notes);
    }
}