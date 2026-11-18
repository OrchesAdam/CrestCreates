using System.Text.Json.Serialization;

namespace ErpManagement.Domain.Shared.Enums;

public enum MaterialType
{
    Purchased = 1,
    Manufactured = 2,
    Virtual = 3
}

public enum BomStatus
{
    Draft = 1,
    Active = 2,
    Disabled = 3
}
public enum RequisitionStatus { Draft = 1, PendingApproval = 2, Approved = 3, Rejected = 4, Converted = 5 }

public enum PurchaseOrderStatus { Draft = 1, Confirmed = 2, PartiallyReceived = 3, Received = 4, Closed = 5 }

public enum GoodsReceiptStatus { Draft = 1, Posted = 2 }

public enum StockMovementType { Receipt = 1, Issue = 2, TransferOut = 3, TransferIn = 4, CountGain = 5, CountLoss = 6 }

public enum StockTransferStatus { Draft = 1, Posted = 2 }

public enum StockCountStatus { Draft = 1, Posted = 2 }

[JsonConverter(typeof(JsonStringEnumConverter<SalesOrderStatus>))]
public enum SalesOrderStatus { Draft = 1, PendingApproval = 2, Approved = 3, Rejected = 4, PartiallyShipped = 5, Shipped = 6, Closed = 7 }

[JsonConverter(typeof(JsonStringEnumConverter<SalesShipmentStatus>))]
public enum SalesShipmentStatus { Draft = 1, Posted = 2 }

[JsonConverter(typeof(JsonStringEnumConverter<WorkOrderStatus>))]
public enum WorkOrderStatus { Planned = 1, Released = 2, InProgress = 3, Completed = 4, Closed = 5 }

[JsonConverter(typeof(JsonStringEnumConverter<ArApDirection>))]
public enum ArApDirection { Receivable = 1, Payable = 2 }

[JsonConverter(typeof(JsonStringEnumConverter<ArApStatus>))]
public enum ArApStatus { Open = 1, PartiallySettled = 2, Settled = 3 }
