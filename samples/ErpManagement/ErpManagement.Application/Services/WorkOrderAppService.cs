using System.Text.Json;
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
using ErpManagement.Application.Contracts.Integration;
using ErpManagement.Application.Integration;
using ErpManagement.Application.Events;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Shared.Enums;
using ErpManagement.Domain.Shared.Permissions;
using ErpManagement.Domain.Inventory;
using ErpManagement.Domain.Repositories;

namespace ErpManagement.Application.Services;

[CrestService]
[CapabilityCompatibilityProjection(
    ContractType = typeof(IWorkOrderAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/work-orders",
    CapabilityIdPrefix = "compat.appservice.work-orders")]
public class WorkOrderAppService(
    IWorkOrderRepository repository,
    IServiceProvider serviceProvider,
    ICurrentUser currentUser,
    IDataPermissionFilter dataPermissionFilter,
    IPermissionChecker permissionChecker,
    ICurrentTenant currentTenant,
    IFeatureChecker features,
    IBomRepository boms,
    ISalesOrderRepository orders,
    ICrestRepositoryBase<Material, Guid> materials,
    ICrestRepositoryBase<Warehouse, Guid> warehouses,
    ICrestRepositoryBase<StorageLocation, Guid> locations,
    StockPostingManager stockPosting,
    ISettingProvider settings,
    ICurrentPrincipalAccessor principalAccessor,
    ErpIntegrationOutbox outbox)
    : CrestAppServiceBase<WorkOrder, Guid, WorkOrderDto, CreateWorkOrderDto, UpdateWorkOrderDto>(
        repository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker), IWorkOrderAppService
{
    private Guid TenantId => currentTenant.GetErpTenantId();

    protected override async Task CheckPermissionAsync(string permissionName, CancellationToken cancellationToken = default)
    {
        await base.CheckPermissionAsync(permissionName, cancellationToken);
        await features.RequireAsync("Erp.Production", cancellationToken);
    }

    [UnitOfWorkMo]
    public override async Task<WorkOrderDto> CreateAsync(CreateWorkOrderDto input, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(CreatePermissionName, cancellationToken);
        var tenantId = TenantId;
        var number = await DocumentNumberGenerator.GenerateAsync("WO",
            (n, ct) => repository.AnyAsync(entity => entity.TenantId == tenantId && entity.Number == n, ct), cancellationToken);
        await ValidatePlanAsync(input.MaterialId, input.SourceSalesOrderId, input.Quantity, cancellationToken);
        var entity = new WorkOrder(Guid.NewGuid(), number, TenantId);
        entity.Plan(input.MaterialId, input.Quantity, input.SourceSalesOrderId, input.Notes);
        await SetCreationAuditPropertiesAsync(entity);
        return MapToDto(await repository.InsertAsync(entity, cancellationToken));
    }

    public override async Task<WorkOrderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ReadPermissionName, cancellationToken);
        var entity = await repository.GetWithLinesAsync(TenantId, id, cancellationToken);
        return entity is null ? null : MapToDto(entity);
    }

    public override async Task<IReadOnlyList<WorkOrderDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(SearchPermissionName, cancellationToken);
        return (await repository.GetAllWithLinesAsync(TenantId, cancellationToken)).Select(MapToDto).ToList();
    }

    [UnitOfWorkMo]
    public override async Task<WorkOrderDto> UpdateAsync(Guid id, UpdateWorkOrderDto input, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(UpdatePermissionName, cancellationToken);
        var entity = await LoadAsync(id, cancellationToken);
        entity.RequirePlanned();
        if (!string.IsNullOrEmpty(input.ConcurrencyStamp) && input.ConcurrencyStamp != entity.ConcurrencyStamp)
            throw new CrestConcurrencyException(nameof(WorkOrder), id);
        await ValidatePlanAsync(input.MaterialId, input.SourceSalesOrderId, input.Quantity, cancellationToken);
        MapToEntity(input, entity);
        return await SaveAsync(entity, cancellationToken);
    }

    [UnitOfWorkMo]
    public override async Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(DeletePermissionName, cancellationToken);
        var entity = await LoadAsync(id, cancellationToken);
        entity.RequirePlanned();
        if (string.IsNullOrWhiteSpace(expectedStamp)) throw new CrestPreconditionRequiredException(nameof(WorkOrder), id);
        await repository.DeleteAsync(id, expectedStamp, cancellationToken);
    }

    private async Task<WorkOrder> LoadAsync(Guid id, CancellationToken ct)
        => await repository.GetWithLinesAsync(TenantId, id, ct)
            ?? throw new KeyNotFoundException($"WorkOrder {id} 不存在。");

    private async Task<WorkOrderDto> SaveAsync(WorkOrder entity, CancellationToken ct)
    {
        await SetModificationAuditPropertiesAsync(entity);
        return MapToDto(await repository.UpdateAsync(entity, ct));
    }

    [UnitOfWorkMo]
    public async Task<WorkOrderDto> ReleaseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ErpPermissions.WorkOrders.Release, cancellationToken);
        var order = await LoadAsync(id, cancellationToken);
        order.RequirePlanned();
        var tenantId = TenantId;
        var activeBoms = (await boms.GetListAsync(bom => bom.TenantId == tenantId && bom.MaterialId == order.MaterialId
            && bom.Status == BomStatus.Active, cancellationToken)).OrderByDescending(bom => bom.CreationTime).ThenBy(bom => bom.Id);
        var latest = activeBoms.FirstOrDefault() ?? throw new CrestValidationException("成品没有启用的 BOM。");
        var bom = await boms.GetWithLinesAsync(latest.Id, cancellationToken);
        if (bom is null || bom.TenantId != tenantId || bom.Status != BomStatus.Active || bom.Lines.Count == 0)
            throw new CrestValidationException("启用的 BOM 不可用或为空。");

        var issues = new List<StockMovementInput>();
        var requirements = new List<WorkOrderMaterial>();
        foreach (var component in bom.Lines.GroupBy(line => line.ComponentMaterialId))
        {
            var quantity = decimal.Round(component.Sum(line => line.Quantity) * order.Quantity, 4, MidpointRounding.AwayFromZero);
            if (quantity <= 0) throw new CrestValidationException("BOM 用量小于库存最小精度。");
            var location = await ResolveLocationAsync(component.Key, cancellationToken);
            requirements.Add(new WorkOrderMaterial(Guid.NewGuid(), order.Id, component.Key, quantity, tenantId));
            issues.Add(new StockMovementInput(component.Key, location.WarehouseId, location.Id, -quantity, StockMovementType.Issue));
        }

        var allowNegative = bool.TryParse(await settings.GetOrNullAsync("Erp.AllowNegativeStockGlobal", cancellationToken), out var allowed) && allowed;
        await stockPosting.PostAsync(tenantId, issues, allowNegative, "WorkOrder", id, cancellationToken);
        order.Release(requirements);
        var result = await SaveAsync(order, cancellationToken);
        await outbox.AppendAsync(new MesWorkOrderReleased(order.Id, order.MaterialId, order.Quantity,
            order.TenantId, DateTimeOffset.UtcNow), cancellationToken);
        return result;
    }

    [UnitOfWorkMo]
    public async Task<WorkOrderDto> ReportAsync(Guid id, WorkOrderReportInput input, CancellationToken cancellationToken = default)
    {
        var fromMes = principalAccessor.IsClient(currentTenant, ErpIntegrationClients.Mes);
        if (fromMes)
        {
            await features.RequireAsync("Erp.MesIntegration", cancellationToken);
            await features.RequireAsync("Erp.Production", cancellationToken);
        }
        else
        {
            await CheckPermissionAsync(ErpPermissions.WorkOrders.Report, cancellationToken);
        }

        if (input.OperationId == Guid.Empty)
            throw new CrestValidationException("报工需要非空的 operationId。");

        var tenantId = TenantId;
        var order = await repository.GetWithLinesForUpdateAsync(tenantId, id, cancellationToken)
            ?? throw new KeyNotFoundException($"WorkOrder {id} 不存在。");
        var source = fromMes ? "MES" : "Manual";
        var recorded = order.Reports.SingleOrDefault(report => report.TenantId == tenantId && report.OperationId == input.OperationId);
        var resultType = ErpOperationReplayJsonContext.Default.WorkOrderDto;
        if (recorded is not null)
        {
            if (recorded.ReportedQuantity != input.ReportedQuantity || recorded.ReportSource != source)
                throw new ErpOperationConflictException();
            var replay = JsonSerializer.Deserialize(recorded.GetOperationResult(), resultType)
                ?? throw new InvalidOperationException("The stored work order report result is empty.");
            return replay;
        }

        ValidateQuantity(input.ReportedQuantity);
        var report = order.AddReport(input.ReportedQuantity, source, input.OperationId);
        var location = await ResolveLocationAsync(order.MaterialId, cancellationToken);
        var allowNegative = bool.TryParse(await settings.GetOrNullAsync("Erp.AllowNegativeStockGlobal", cancellationToken), out var allowed) && allowed;
        await stockPosting.PostAsync(tenantId,
            [new StockMovementInput(order.MaterialId, location.WarehouseId, location.Id, input.ReportedQuantity, StockMovementType.Receipt)],
            allowNegative, "WorkOrder", id, cancellationToken, input.OperationId);
        var result = await SaveAsync(order, cancellationToken);
        report.RecordOperationResult(JsonSerializer.Serialize(result, resultType));
        if (order.Status == WorkOrderStatus.Completed)
            await outbox.AppendAsync(new MesWorkOrderCompleted(order.Id, order.Quantity, report.ReportedAt, order.TenantId), cancellationToken);
        return result;
    }

    [UnitOfWorkMo]
    public async Task<WorkOrderDto> CloseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ErpPermissions.WorkOrders.Close, cancellationToken);
        var order = await LoadAsync(id, cancellationToken);
        order.Close();
        return await SaveAsync(order, cancellationToken);
    }

    private async Task ValidatePlanAsync(Guid materialId, Guid? salesOrderId, decimal quantity, CancellationToken ct)
    {
        ValidateQuantity(quantity);
        var tenantId = TenantId;
        if (!await materials.AnyAsync(material => material.Id == materialId && material.TenantId == tenantId, ct))
            throw new CrestValidationException("工单成品物料不存在。");
        if (salesOrderId is Guid id && !await orders.AnyAsync(order => order.Id == id && order.TenantId == tenantId, ct))
            throw new CrestValidationException("工单来源销售订单不存在。");
    }

    private static void ValidateQuantity(decimal quantity)
    {
        if (quantity <= 0 || decimal.Round(quantity, 4) != quantity)
            throw new CrestValidationException("数量必须为正且最多四位小数。");
    }

    private async Task<StorageLocation> ResolveLocationAsync(Guid materialId, CancellationToken ct)
    {
        var tenantId = TenantId;
        var material = await materials.GetAsync(item => item.TenantId == tenantId && item.Id == materialId, ct);
        if (material?.DefaultWarehouseId is not Guid warehouseId
            || !await warehouses.AnyAsync(warehouse => warehouse.Id == warehouseId && warehouse.TenantId == tenantId, ct))
            throw new CrestValidationException($"物料 {materialId} 缺少有效的默认仓库。");
        var available = await locations.GetListAsync(location => location.WarehouseId == warehouseId && location.TenantId == tenantId, ct);
        return available.OrderBy(location => location.Code, StringComparer.Ordinal).ThenBy(location => location.Id).FirstOrDefault()
            ?? throw new CrestValidationException($"默认仓库 {warehouseId} 没有库位。");
    }

    protected override WorkOrder MapToEntity(CreateWorkOrderDto dto)
    {
        var entity = new WorkOrder(Guid.NewGuid(), dto.Number, TenantId);
        entity.Plan(dto.MaterialId, dto.Quantity, dto.SourceSalesOrderId, dto.Notes);
        return entity;
    }

    protected override void MapToEntity(UpdateWorkOrderDto dto, WorkOrder entity)
    {
        entity.Plan(dto.MaterialId, dto.Quantity, dto.SourceSalesOrderId, dto.Notes);
    }

    protected override WorkOrderDto MapToDto(WorkOrder entity) => new()
    {
        Id = entity.Id,
        Number = entity.Number,
        MaterialId = entity.MaterialId,
        Quantity = entity.Quantity,
        Status = entity.Status,
        SourceSalesOrderId = entity.SourceSalesOrderId,
        Notes = entity.Notes,
        TenantId = entity.TenantId,
        CreationTime = entity.CreationTime,
        CreatorId = entity.CreatorId,
        LastModificationTime = entity.LastModificationTime,
        LastModifierId = entity.LastModifierId,
        ConcurrencyStamp = entity.ConcurrencyStamp,
        Materials = entity.Materials.Select(child => new WorkOrderMaterialDto
        {
            Id = child.Id,
            WorkOrderId = child.WorkOrderId,
            MaterialId = child.MaterialId,
            RequiredQuantity = child.RequiredQuantity,
            IssuedQuantity = child.IssuedQuantity,
            TenantId = child.TenantId,
            CreationTime = child.CreationTime,
            ConcurrencyStamp = child.ConcurrencyStamp,
        }).ToList(),
        Reports = entity.Reports.OrderBy(report => report.ReportedAt).Select(child => new WorkOrderReportDto
        {
            Id = child.Id,
            WorkOrderId = child.WorkOrderId,
            ReportedQuantity = child.ReportedQuantity,
            ReportSource = child.ReportSource,
            ReportedAt = child.ReportedAt,
            TenantId = child.TenantId,
            CreationTime = child.CreationTime,
            ConcurrencyStamp = child.ConcurrencyStamp,
        }).ToList(),
    };
}
