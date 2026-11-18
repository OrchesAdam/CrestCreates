namespace ErpManagement.Application.Contracts.DTOs;

public partial class SalesOrderDto { public List<SalesOrderLineDto> Lines { get; set; } = new(); }
public partial class CreateSalesOrderDto { public List<SalesOrderLineInput> Lines { get; set; } = new(); }
public partial class UpdateSalesOrderDto { public List<SalesOrderLineInput> Lines { get; set; } = new(); }
public class SalesOrderLineInput { public Guid MaterialId { get; set; } public decimal Quantity { get; set; } public decimal UnitPrice { get; set; } public int Position { get; set; } public Guid WarehouseId { get; set; } public Guid StorageLocationId { get; set; } }

public partial class SalesShipmentDto { public List<SalesShipmentLineDto> Lines { get; set; } = new(); }
public partial class CreateSalesShipmentDto { public List<SalesShipmentLineInput> Lines { get; set; } = new(); }
public partial class UpdateSalesShipmentDto { public List<SalesShipmentLineInput> Lines { get; set; } = new(); }
public class SalesShipmentLineInput { public Guid SalesOrderLineId { get; set; } public decimal Quantity { get; set; } }
