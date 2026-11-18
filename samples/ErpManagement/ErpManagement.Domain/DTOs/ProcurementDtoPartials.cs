namespace ErpManagement.Application.Contracts.DTOs;

public partial class PurchaseRequisitionDto { public List<PurchaseRequisitionLineDto> Lines { get; set; } = new(); }
public partial class CreatePurchaseRequisitionDto { public List<RequisitionLineInput> Lines { get; set; } = new(); }
public partial class UpdatePurchaseRequisitionDto { public List<RequisitionLineInput> Lines { get; set; } = new(); }
public class RequisitionLineInput { public Guid MaterialId { get; set; } public decimal Quantity { get; set; } public decimal UnitPrice { get; set; } public int Position { get; set; } }

public partial class PurchaseOrderDto { public List<PurchaseOrderLineDto> Lines { get; set; } = new(); }
public partial class CreatePurchaseOrderDto { public List<OrderLineInput> Lines { get; set; } = new(); }
public partial class UpdatePurchaseOrderDto { public List<OrderLineInput> Lines { get; set; } = new(); }
public class OrderLineInput { public Guid MaterialId { get; set; } public decimal Quantity { get; set; } public decimal UnitPrice { get; set; } public int Position { get; set; } public Guid WarehouseId { get; set; } public Guid StorageLocationId { get; set; } }

public partial class GoodsReceiptDto { public List<GoodsReceiptLineDto> Lines { get; set; } = new(); }
public partial class CreateGoodsReceiptDto { public List<ReceiptLineInput> Lines { get; set; } = new(); }
public partial class UpdateGoodsReceiptDto { public List<ReceiptLineInput> Lines { get; set; } = new(); }
public class ReceiptLineInput { public Guid PurchaseOrderLineId { get; set; } public Guid MaterialId { get; set; } public Guid StorageLocationId { get; set; } public decimal Quantity { get; set; } public decimal UnitPrice { get; set; } }
