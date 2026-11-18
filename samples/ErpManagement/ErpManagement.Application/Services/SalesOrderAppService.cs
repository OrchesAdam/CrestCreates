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
using System.Globalization;
using ErpManagement.Application.Approval;

namespace ErpManagement.Application.Services;

[CrestService]
[CapabilityCompatibilityProjection(
    ContractType = typeof(ISalesOrderAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/sales-orders",
    CapabilityIdPrefix = "compat.appservice.sales-orders")]
public class SalesOrderAppService(
    ISalesOrderRepository repository,
    IServiceProvider serviceProvider,
    ICurrentUser currentUser,
    IDataPermissionFilter dataPermissionFilter,
    IPermissionChecker permissionChecker,
    ICurrentTenant currentTenant,
    IFeatureChecker features,
    ISettingProvider settings,
    ICrestRepositoryBase<Customer, Guid> customers,
    ICrestRepositoryBase<Material, Guid> materials,
    ICrestRepositoryBase<Warehouse, Guid> warehouses,
    ICrestRepositoryBase<StorageLocation, Guid> locations,
    ErpSalesApprovalOrchestrator approval)
    : CrestAppServiceBase<SalesOrder, Guid, SalesOrderDto, CreateSalesOrderDto, UpdateSalesOrderDto>(
        repository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker), ISalesOrderAppService
{
    private Guid TenantId => currentTenant.GetErpTenantId();

    protected override async Task CheckPermissionAsync(string permissionName, CancellationToken cancellationToken = default)
    {
        await base.CheckPermissionAsync(permissionName, cancellationToken);
        await features.RequireAsync("Erp.Sales", cancellationToken);
    }

    [UnitOfWorkMo]
    public override async Task<SalesOrderDto> CreateAsync(CreateSalesOrderDto input, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(CreatePermissionName, cancellationToken);
        var tenantId = TenantId;
        var number = await DocumentNumberGenerator.GenerateAsync(await settings.GetOrNullAsync("Erp.SalesOrderPrefix", cancellationToken) ?? "SO",
            (n, ct) => repository.AnyAsync(entity => entity.TenantId == tenantId && entity.Number == n, ct), cancellationToken);
        await ValidateDraftAsync(input.CustomerId, input.Lines, cancellationToken);
        var entity = new SalesOrder(Guid.NewGuid(), number, input.CustomerId, TenantId);
        entity.SetNotes(input.Notes);
        entity.ReplaceLines(BuildLines(input.Lines, entity.Id));
        await SetCreationAuditPropertiesAsync(entity);
        return MapToDto(await repository.InsertAsync(entity, cancellationToken));
    }

    public override async Task<SalesOrderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ReadPermissionName, cancellationToken);
        var entity = await repository.GetWithLinesAsync(TenantId, id, cancellationToken);
        return entity is null ? null : MapToDto(entity);
    }

    public override async Task<IReadOnlyList<SalesOrderDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(SearchPermissionName, cancellationToken);
        return (await repository.GetAllWithLinesAsync(TenantId, cancellationToken)).Select(MapToDto).ToList();
    }

    [UnitOfWorkMo]
    public override async Task<SalesOrderDto> UpdateAsync(Guid id, UpdateSalesOrderDto input, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(UpdatePermissionName, cancellationToken);
        var entity = await LoadAsync(id, cancellationToken);
        entity.RequireDraft();
        if (!string.IsNullOrEmpty(input.ConcurrencyStamp) && input.ConcurrencyStamp != entity.ConcurrencyStamp)
            throw new CrestConcurrencyException(nameof(SalesOrder), id);
        await ValidateDraftAsync(input.CustomerId, input.Lines, cancellationToken);
        MapToEntity(input, entity);
        entity.ReplaceLines(BuildLines(input.Lines, id));
        return await SaveAsync(entity, cancellationToken);
    }

    [UnitOfWorkMo]
    public override async Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(DeletePermissionName, cancellationToken);
        var entity = await LoadAsync(id, cancellationToken);
        entity.RequireDraft();
        if (string.IsNullOrWhiteSpace(expectedStamp)) throw new CrestPreconditionRequiredException(nameof(SalesOrder), id);
        await repository.DeleteAsync(id, expectedStamp, cancellationToken);
    }

    private async Task<SalesOrder> LoadAsync(Guid id, CancellationToken ct)
        => await repository.GetWithLinesAsync(TenantId, id, ct)
            ?? throw new KeyNotFoundException($"SalesOrder {id} 不存在。");

    private async Task<SalesOrderDto> SaveAsync(SalesOrder entity, CancellationToken ct)
    {
        await SetModificationAuditPropertiesAsync(entity);
        return MapToDto(await repository.UpdateAsync(entity, ct));
    }

    [UnitOfWorkMo]
    public async Task<SalesOrderDto> SubmitAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ErpPermissions.SalesOrders.Submit, cancellationToken);
        var entity = await LoadAsync(id, cancellationToken);
        entity.Submit(CurrentUser.Id);
        var value = await settings.GetOrNullAsync("Erp.SalesOrderApprovalThreshold", cancellationToken) ?? "50000";
        if (!decimal.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var threshold) || threshold < 0)
            throw new CrestValidationException("销售审批阈值配置无效。");

        if (entity.TotalAmount <= threshold)
        {
            entity.Approve("system", null);
            return await SaveAsync(entity, cancellationToken);
        }

        var lease = await approval.StartAsync(entity, cancellationToken);
        try
        {
            entity.MarkPendingApproval(lease.WorkflowKey.InstanceId, lease.HumanTaskKey.InstanceId);
            return await SaveAsync(entity, cancellationToken);
        }
        catch
        {
            await approval.AbortAsync(lease, "ERP sales order correlation persistence failed.", CancellationToken.None);
            throw;
        }
    }

    [UnitOfWorkMo]
    public async Task<SalesOrderDto> CloseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ErpPermissions.SalesOrders.Close, cancellationToken);
        var entity = await LoadAsync(id, cancellationToken);
        entity.Close();
        return await SaveAsync(entity, cancellationToken);
    }

    private async Task ValidateDraftAsync(Guid customerId, List<SalesOrderLineInput> lines, CancellationToken ct)
    {
        var tenantId = TenantId;
        if (!await customers.AnyAsync(customer => customer.Id == customerId && customer.TenantId == tenantId, ct))
            throw new CrestValidationException("客户不存在。");
        if (lines is null) throw new CrestValidationException("缺少销售订单行。");
        foreach (var line in lines)
        {
            if (line.Quantity <= 0 || decimal.Round(line.Quantity, 4) != line.Quantity
                || line.UnitPrice < 0 || decimal.Round(line.UnitPrice, 2) != line.UnitPrice)
                throw new CrestValidationException("销售数量必须为正且最多四位小数，单价必须非负且最多两位小数。");
            if (!await materials.AnyAsync(material => material.Id == line.MaterialId && material.TenantId == tenantId, ct)
                || !await warehouses.AnyAsync(warehouse => warehouse.Id == line.WarehouseId && warehouse.TenantId == tenantId, ct)
                || !await locations.AnyAsync(location => location.Id == line.StorageLocationId
                    && location.WarehouseId == line.WarehouseId && location.TenantId == tenantId, ct))
                throw new CrestValidationException("销售订单行的物料、仓库或库位不属于当前租户，或库位不在所选仓库。");
        }
    }

    private List<SalesOrderLine> BuildLines(List<SalesOrderLineInput> lines, Guid orderId)
        => lines.Select(line => new SalesOrderLine(Guid.NewGuid(), orderId, line.MaterialId, line.Quantity,
            line.UnitPrice, line.Position, line.WarehouseId, line.StorageLocationId, TenantId)).ToList();

    protected override SalesOrder MapToEntity(CreateSalesOrderDto dto)
    {
        return new SalesOrder(Guid.NewGuid(), dto.Number, dto.CustomerId, TenantId);
    }

    protected override void MapToEntity(UpdateSalesOrderDto dto, SalesOrder entity)
    {
        entity.SetCustomer(dto.CustomerId);
        entity.SetNotes(dto.Notes);
    }

    protected override SalesOrderDto MapToDto(SalesOrder entity) => new()
    {
        Id = entity.Id,
        Number = entity.Number,
        CustomerId = entity.CustomerId,
        Status = entity.Status,
        TotalAmount = entity.TotalAmount,
        WorkflowInstanceId = entity.WorkflowInstanceId,
        HumanTaskInstanceId = entity.HumanTaskInstanceId,
        ApproverId = entity.ApproverId,
        ApprovalComment = entity.ApprovalComment,
        RequesterId = entity.RequesterId,
        ApprovalAccepted = entity.ApprovalAccepted,
        Notes = entity.Notes,
        TenantId = entity.TenantId,
        CreationTime = entity.CreationTime,
        CreatorId = entity.CreatorId,
        LastModificationTime = entity.LastModificationTime,
        LastModifierId = entity.LastModifierId,
        ConcurrencyStamp = entity.ConcurrencyStamp,
        Lines = entity.Lines.OrderBy(line => line.Position).Select(child => new SalesOrderLineDto
        {
            Id = child.Id,
            OrderId = child.OrderId,
            MaterialId = child.MaterialId,
            Quantity = child.Quantity,
            ShippedQuantity = child.ShippedQuantity,
            UnitPrice = child.UnitPrice,
            Amount = child.Amount,
            Position = child.Position,
            WarehouseId = child.WarehouseId,
            StorageLocationId = child.StorageLocationId,
            TenantId = child.TenantId,
            CreationTime = child.CreationTime,
            ConcurrencyStamp = child.ConcurrencyStamp,
        }).ToList(),
    };
}
