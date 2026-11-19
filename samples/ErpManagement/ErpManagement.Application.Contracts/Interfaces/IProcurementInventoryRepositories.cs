using CrestCreates.Domain.Repositories;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Shared.Enums;

namespace ErpManagement.Application.Contracts.Interfaces;

public interface IPurchaseRequisitionRepository : ICrestRepositoryBase<PurchaseRequisition, Guid>
{
    Task<PurchaseRequisition?> GetWithLinesAsync(Guid id, CancellationToken ct = default);
    Task<List<PurchaseRequisition>> GetAllWithLinesAsync(CancellationToken ct = default);
}

public interface IPurchaseOrderRepository : ICrestRepositoryBase<PurchaseOrder, Guid>
{
    Task<PurchaseOrder?> GetWithLinesAsync(Guid id, CancellationToken ct = default);
    Task<List<PurchaseOrder>> GetAllWithLinesAsync(CancellationToken ct = default);
}

public interface IGoodsReceiptRepository : ICrestRepositoryBase<GoodsReceipt, Guid>
{
    Task<GoodsReceipt?> GetWithLinesAsync(Guid id, CancellationToken ct = default);
    Task<GoodsReceipt?> GetForPostingAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<GoodsReceiptStatus?> LockAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<List<GoodsReceipt>> GetAllWithLinesAsync(CancellationToken ct = default);
}

public interface IStockTransferRepository : ICrestRepositoryBase<StockTransfer, Guid>
{
    Task<StockTransfer?> GetWithLinesAsync(Guid id, CancellationToken ct = default);
    Task<List<StockTransfer>> GetAllWithLinesAsync(CancellationToken ct = default);
}

public interface IStockCountRepository : ICrestRepositoryBase<StockCount, Guid>
{
    Task<StockCount?> GetWithLinesAsync(Guid id, CancellationToken ct = default);
    Task<List<StockCount>> GetAllWithLinesAsync(CancellationToken ct = default);
}