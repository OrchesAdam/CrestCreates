namespace ErpManagement.Application.Contracts.DTOs;

public partial class StockTransferDto { public List<StockTransferLineDto> Lines { get; set; } = new(); }
public partial class CreateStockTransferDto { public List<TransferLineInput> Lines { get; set; } = new(); }
public partial class UpdateStockTransferDto { public List<TransferLineInput> Lines { get; set; } = new(); }
public class TransferLineInput { public Guid MaterialId { get; set; } public Guid FromWarehouseId { get; set; } public Guid FromStorageLocationId { get; set; } public Guid ToWarehouseId { get; set; } public Guid ToStorageLocationId { get; set; } public decimal Quantity { get; set; } }

public partial class StockCountDto { public List<StockCountLineDto> Lines { get; set; } = new(); }
public partial class CreateStockCountDto { public List<CountLineInput> Lines { get; set; } = new(); }
public partial class UpdateStockCountDto : CrestCreates.Domain.Shared.Entities.Auditing.IHasConcurrencyStampInput
{
    public List<CountLineInput> Lines { get; set; } = new();
}
public class CountLineInput
{
    public Guid MaterialId { get; set; }
    public Guid StorageLocationId { get; set; }
    // Accepted for older clients. The service always supplies its own book quantity snapshot.
    public decimal BookQuantity { get; set; }
    public decimal CountedQuantity { get; set; }
}
