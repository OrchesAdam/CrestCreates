namespace ErpManagement.Domain.Shared.Permissions;

public static class ErpPermissions
{
    public const string MasterDataGroup = "ErpMasterData";
    public const string ProcurementGroup = "ErpProcurement";
    public const string InventoryGroup = "ErpInventory";
    public const string SalesGroup = "ErpSales";
    public const string ProductionGroup = "ErpProduction";
    public const string FinanceGroup = "ErpFinance";
    public const string IntegrationGroup = "ErpIntegration";

    public static class Materials
    {
        public const string Default = "Material";
        public const string Create = Default + ".Create";
        public const string Get = Default + ".Get";
        public const string Search = Default + ".Search";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    public static class MaterialCategories
    {
        public const string Default = "MaterialCategory";
        public const string Create = Default + ".Create";
        public const string Get = Default + ".Get";
        public const string Search = Default + ".Search";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    public static class UnitsOfMeasure
    {
        public const string Default = "UnitOfMeasure";
        public const string Create = Default + ".Create";
        public const string Get = Default + ".Get";
        public const string Search = Default + ".Search";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    public static class Warehouses
    {
        public const string Default = "Warehouse";
        public const string Create = Default + ".Create";
        public const string Get = Default + ".Get";
        public const string Search = Default + ".Search";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    public static class StorageLocations
    {
        public const string Default = "StorageLocation";
        public const string Create = Default + ".Create";
        public const string Get = Default + ".Get";
        public const string Search = Default + ".Search";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    public static class Suppliers
    {
        public const string Default = "Supplier";
        public const string Create = Default + ".Create";
        public const string Get = Default + ".Get";
        public const string Search = Default + ".Search";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    public static class Customers
    {
        public const string Default = "Customer";
        public const string Create = Default + ".Create";
        public const string Get = Default + ".Get";
        public const string Search = Default + ".Search";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    public static class Boms
    {
        public const string Default = "Bom";
        public const string Create = Default + ".Create";
        public const string Get = Default + ".Get";
        public const string Search = Default + ".Search";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string Activate = Default + ".Activate";
    }

    public static class PurchaseRequisitions
    {
        public const string Default = "PurchaseRequisition";
        public const string Create = Default + ".Create";
        public const string Get = Default + ".Get";
        public const string Search = Default + ".Search";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string Submit = Default + ".Submit";
        public const string Convert = Default + ".Convert";
    }

    public static class PurchaseOrders
    {
        public const string Default = "PurchaseOrder";
        public const string Create = Default + ".Create";
        public const string Get = Default + ".Get";
        public const string Search = Default + ".Search";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string Confirm = Default + ".Confirm";
        public const string Close = Default + ".Close";
    }

    public static class GoodsReceipts
    {
        public const string Default = "GoodsReceipt";
        public const string Create = Default + ".Create";
        public const string Get = Default + ".Get";
        public const string Search = Default + ".Search";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string Post = Default + ".Post";
    }

    public static class StockBalances
    {
        public const string Default = "StockBalance";
        public const string Get = Default + ".Get";
        public const string Search = Default + ".Search";
    }

    public static class StockTransactions
    {
        public const string Default = "StockTransaction";
        public const string Get = Default + ".Get";
        public const string Search = Default + ".Search";
    }

    public static class StockTransfers
    {
        public const string Default = "StockTransfer";
        public const string Create = Default + ".Create";
        public const string Get = Default + ".Get";
        public const string Search = Default + ".Search";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string Post = Default + ".Post";
    }

    public static class StockCounts
    {
        public const string Default = "StockCount";
        public const string Create = Default + ".Create";
        public const string Get = Default + ".Get";
        public const string Search = Default + ".Search";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string Post = Default + ".Post";
    }

    public static class HumanTasks
    {
        public const string Default = "HumanTask";
        public const string Get = Default + ".Get";
        public const string Search = Default + ".Search";
        public const string Complete = Default + ".Complete";
    }

    public static class SalesOrders
    {
        public const string Default = "SalesOrder";
        public const string Create = Default + ".Create";
        public const string Get = Default + ".Get";
        public const string Search = Default + ".Search";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string Submit = Default + ".Submit";
        public const string Ship = Default + ".Ship";
        public const string Close = Default + ".Close";
    }

    public static class SalesShipments
    {
        public const string Default = "SalesShipment";
        public const string Create = Default + ".Create";
        public const string Get = Default + ".Get";
        public const string Search = Default + ".Search";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string Post = Default + ".Post";
    }

    public static class WorkOrders
    {
        public const string Default = "WorkOrder";
        public const string Create = Default + ".Create";
        public const string Get = Default + ".Get";
        public const string Search = Default + ".Search";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string Release = Default + ".Release";
        public const string Report = Default + ".Report";
        public const string Close = Default + ".Close";
    }

    public static class ArApLedger
    {
        public const string Default = "ArApLedger";
        public const string Get = Default + ".Get";
        public const string Search = Default + ".Search";
        public const string Settle = Default + ".Settle";
    }

    public static class SalesApproval
    {
        public const string Default = "SalesApproval";
        public const string Search = Default + ".Search";
        public const string Complete = Default + ".Complete";
    }

    public static class Dashboard
    {
        public const string Default = "Dashboard";
        public const string Get = Default + ".Get";
    }
}
