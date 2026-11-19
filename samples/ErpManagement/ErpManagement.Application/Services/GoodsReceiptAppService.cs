using CrestCreates.Aop.Interceptors;
using CrestCreates.Application.Services;
using CrestCreates.Authorization;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Settings;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.DataFilter;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.MultiTenancy.Abstract;
using ErpManagement.Application.Contracts.DTOs;
using ErpManagement.Application.Contracts.Events;
using ErpManagement.Application.Contracts.Interfaces;
using ErpManagement.Application.Events;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Inventory;
using ErpManagement.Domain.Shared.Enums;
using ErpManagement.Domain.Shared.Permissions;
using Microsoft.Extensions.Logging;

namespace ErpManagement.Application.Services;

[CrestService]
[CapabilityCompatibilityProjection(
    ContractType = typeof(IGoodsReceiptAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/goods-receipts",
    CapabilityIdPrefix = "compat.appservice.goods-receipts")]
public class GoodsReceiptAppService
    : CrestAppServiceBase<GoodsReceipt, Guid, GoodsReceiptDto, CreateGoodsReceiptDto, UpdateGoodsReceiptDto>,
      IGoodsReceiptAppService
{
    private readonly IGoodsReceiptRepository _receiptRepository;
    private readonly IPurchaseOrderRepository _orderRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly ISettingProvider _settingProvider;
    private readonly StockPostingManager _stockPostingManager;
    private readonly StockPostedEventPublisher _stockPostedPublisher;
    private readonly ILocalEventBus _localEventBus;
    private readonly IFeatureChecker _features;
    private readonly IArApLedgerRepository _ledgers;
    private readonly ILogger<GoodsReceiptAppService> _logger;
    private readonly IErpDeliveryStatusReader _deliveryStatus;

    public GoodsReceiptAppService(
        IGoodsReceiptRepository receiptRepository,
        IPurchaseOrderRepository orderRepository,
        IServiceProvider serviceProvider,
        ICurrentUser currentUser,
        IDataPermissionFilter dataPermissionFilter,
        IPermissionChecker permissionChecker,
        ICurrentTenant currentTenant,
        ISettingProvider settingProvider,
        StockPostingManager stockPostingManager,
        StockPostedEventPublisher stockPostedPublisher,
        ILocalEventBus localEventBus,
        IFeatureChecker features,
        IArApLedgerRepository ledgers,
        ILogger<GoodsReceiptAppService> logger,
        IErpDeliveryStatusReader deliveryStatus)
        : base(receiptRepository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
        _receiptRepository = receiptRepository;
        _orderRepository = orderRepository;
        _currentTenant = currentTenant;
        _settingProvider = settingProvider;
        _stockPostingManager = stockPostingManager;
        _stockPostedPublisher = stockPostedPublisher;
        _localEventBus = localEventBus;
        _features = features;
        _ledgers = ledgers;
        _logger = logger;
        _deliveryStatus = deliveryStatus;
    }

    private Guid CurrentTenantId => Guid.TryParse(_currentTenant.Id, out var tid) ? tid : Guid.Empty;

    [UnitOfWorkMo]
    public override async Task<GoodsReceiptDto> CreateAsync(CreateGoodsReceiptDto input, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(CreatePermissionName, cancellationToken);
        var number = await DocumentNumberGenerator.GenerateAsync(
            "GR", (n, ct) => _receiptRepository.AnyAsync(r => r.Number == n, ct), cancellationToken);
        var receipt = new GoodsReceipt(
            Guid.NewGuid(), number, input.PurchaseOrderId, input.SupplierId, input.WarehouseId, CurrentTenantId);
        receipt.SetNotes(input.Notes);
        receipt.ReplaceLines(BuildLines(input.Lines, receipt.Id, CurrentTenantId));
        await SetCreationAuditPropertiesAsync(receipt);
        var created = await _receiptRepository.InsertAsync(receipt, cancellationToken);
        return MapToDto(created);
    }

    public override async Task<GoodsReceiptDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ReadPermissionName, cancellationToken);
        var receipt = await _receiptRepository.GetWithLinesAsync(id, cancellationToken);
        return receipt == null ? null : MapToDto(receipt);
    }

    public override async Task<IReadOnlyList<GoodsReceiptDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(SearchPermissionName, cancellationToken);
        var receipts = await _receiptRepository.GetAllWithLinesAsync(cancellationToken);
        return receipts.Select(MapToDto).ToList();
    }

    [UnitOfWorkMo]
    public override async Task<GoodsReceiptDto> UpdateAsync(Guid id, UpdateGoodsReceiptDto input, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(UpdatePermissionName, cancellationToken);
        var receipt = await _receiptRepository.GetWithLinesAsync(id, cancellationToken)
                      ?? throw new KeyNotFoundException($"收货单不存在: {id}");
        receipt.SetNotes(input.Notes);
        receipt.ReplaceLines(BuildLines(input.Lines, receipt.Id, CurrentTenantId));
        await SetModificationAuditPropertiesAsync(receipt);
        var updated = await _receiptRepository.UpdateAsync(receipt, cancellationToken);
        return MapToDto(updated);
    }

    public async Task<GoodsReceiptDto> PostAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var posted = await PostCoreAsync(id, cancellationToken);
        foreach (var movement in posted.Movements)
        {
            try
            {
                await _localEventBus.PublishAsync(new StockChanged
                {
                    MaterialId = movement.MaterialId,
                    WarehouseId = movement.WarehouseId,
                    StorageLocationId = movement.StorageLocationId,
                    NewQuantity = movement.NewQuantity,
                    MovementType = movement.MovementType.ToString(),
                    SourceDocId = posted.Receipt.Id
                }, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Local StockChanged notification failed for posted receipt {ReceiptId}.", id);
            }
        }

        return MapToDto(posted.Receipt);
    }

    [UnitOfWorkMo]
    private async Task<(GoodsReceipt Receipt, IReadOnlyList<StockMovementResult> Movements)> PostCoreAsync(
        Guid id, CancellationToken cancellationToken)
    {
        await CheckPermissionAsync(ErpPermissions.GoodsReceipts.Post, cancellationToken);
        var receipt = await _receiptRepository.GetWithLinesAsync(id, cancellationToken)
                      ?? throw new KeyNotFoundException($"收货单不存在: {id}");
        if (receipt.Status == GoodsReceiptStatus.Posted)
        {
            return (receipt, []);
        }

        if (receipt.Status != GoodsReceiptStatus.Draft)
        {
            throw new InvalidOperationException("只有草稿收货单可以过账。");
        }

        var order = await _orderRepository.GetWithLinesAsync(receipt.PurchaseOrderId, cancellationToken)
                    ?? throw new KeyNotFoundException($"采购单不存在: {receipt.PurchaseOrderId}");
        if (order.Status is not (PurchaseOrderStatus.Confirmed or PurchaseOrderStatus.PartiallyReceived))
        {
            throw new InvalidOperationException("采购单当前状态不可收货。");
        }

        var orderLineIds = order.Lines.Select(l => l.Id).ToHashSet();
        if (receipt.Lines.Any(l => !orderLineIds.Contains(l.PurchaseOrderLineId)))
        {
            throw new InvalidOperationException("收货行引用了不属于该采购单的采购行。");
        }

        var allowNegativeValue = await _settingProvider.GetOrNullAsync("Erp.AllowNegativeStockGlobal", cancellationToken);
        var allowNegativeStock = bool.TryParse(allowNegativeValue, out var parsed) && parsed;

        var movements = receipt.Lines.Select(l => new StockMovementInput(
            l.MaterialId, receipt.WarehouseId, l.StorageLocationId, l.Quantity, StockMovementType.Receipt)).ToList();
        var results = await _stockPostingManager.PostAsync(
            CurrentTenantId, movements, allowNegativeStock, "GoodsReceipt", receipt.Id, cancellationToken);

        order.RecordReceipt(receipt.Lines.ToDictionary(l => l.PurchaseOrderLineId, l => l.Quantity));
        await SetModificationAuditPropertiesAsync(order);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        receipt.Post(DateTimeOffset.UtcNow);
        await SetModificationAuditPropertiesAsync(receipt);
        await _receiptRepository.UpdateAsync(receipt, cancellationToken);

        if (await _features.IsEnabledAsync("Erp.Finance", cancellationToken))
        {
            var ledger = new ArApLedger(Guid.NewGuid(), ArApDirection.Payable, "GoodsReceipt", receipt.Id,
                receipt.SupplierId, receipt.Lines.Sum(line => line.Quantity * line.UnitPrice), receipt.TenantId);
            await SetCreationAuditPropertiesAsync(ledger);
            await _ledgers.InsertAsync(ledger, cancellationToken);
        }

        await _stockPostedPublisher.PublishAsync(receipt, order, cancellationToken);
        return (receipt, results);
    }

    public async Task<GoodsReceiptDeliveryStatusDto> GetDeliveryStatusAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ReadPermissionName, cancellationToken);
        if (!await _receiptRepository.AnyAsync(x => x.Id == id, cancellationToken))
            throw new KeyNotFoundException($"收货单不存在: {id}");
        var messageId = $"erp.stock-posted:{id}";
        var status = await _deliveryStatus.GetAsync(messageId, ErpStockPostedInboxConsumer.ConsumerIdValue, cancellationToken);
        return new GoodsReceiptDeliveryStatusDto
        {
            MessageId = messageId,
            Status = status?.Status ?? "NotAppended",
            AttemptCount = status?.AttemptCount ?? 0,
            Consumed = status?.Consumed ?? false,
            FailureCode = status?.FailureCode
        };
    }

    private static List<GoodsReceiptLine> BuildLines(List<ReceiptLineInput> lines, Guid receiptId, Guid tenantId)
    {
        return lines.Select(l => new GoodsReceiptLine(
            Guid.NewGuid(), receiptId, l.PurchaseOrderLineId, l.MaterialId, l.StorageLocationId,
            l.Quantity, l.UnitPrice, tenantId)).ToList();
    }

    protected override GoodsReceipt MapToEntity(CreateGoodsReceiptDto dto)
    {
        var receipt = new GoodsReceipt(
            Guid.NewGuid(), dto.Number, dto.PurchaseOrderId, dto.SupplierId, dto.WarehouseId, CurrentTenantId);
        receipt.SetNotes(dto.Notes);
        return receipt;
    }

    protected override GoodsReceiptDto MapToDto(GoodsReceipt entity)
    {
        return new GoodsReceiptDto
        {
            Id = entity.Id,
            Number = entity.Number,
            PurchaseOrderId = entity.PurchaseOrderId,
            SupplierId = entity.SupplierId,
            WarehouseId = entity.WarehouseId,
            Status = entity.Status,
            PostedAt = entity.PostedAt,
            Notes = entity.Notes,
            TenantId = entity.TenantId,
            CreationTime = entity.CreationTime,
            ConcurrencyStamp = entity.ConcurrencyStamp,
            Lines = entity.Lines.Select(l => new GoodsReceiptLineDto
            {
                Id = l.Id,
                ReceiptId = l.ReceiptId,
                PurchaseOrderLineId = l.PurchaseOrderLineId,
                MaterialId = l.MaterialId,
                StorageLocationId = l.StorageLocationId,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                TenantId = l.TenantId
            }).ToList()
        };
    }

    protected override void MapToEntity(UpdateGoodsReceiptDto dto, GoodsReceipt entity)
    {
        entity.SetNotes(dto.Notes);
    }
}