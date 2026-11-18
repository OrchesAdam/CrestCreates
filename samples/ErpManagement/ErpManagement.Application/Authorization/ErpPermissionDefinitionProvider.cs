using CrestCreates.Authorization.Abstractions;
using ErpManagement.Domain.Shared.Permissions;

namespace ErpManagement.Application.Authorization;

public sealed class ErpPermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        var masterData = context.GetGroupOrNull(ErpPermissions.MasterDataGroup)
                         ?? context.AddGroup(ErpPermissions.MasterDataGroup, "ERP 基础资料");

        AddCrud(masterData, ErpPermissions.Materials.Default, "物料");
        AddCrud(masterData, ErpPermissions.MaterialCategories.Default, "物料分类");
        AddCrud(masterData, ErpPermissions.UnitsOfMeasure.Default, "计量单位");
        AddCrud(masterData, ErpPermissions.Warehouses.Default, "仓库");
        AddCrud(masterData, ErpPermissions.StorageLocations.Default, "库位");
        AddCrud(masterData, ErpPermissions.Suppliers.Default, "供应商");
        AddCrud(masterData, ErpPermissions.Customers.Default, "客户");
        AddCrud(masterData, ErpPermissions.Boms.Default, "BOM");
        masterData.AddPermission(ErpPermissions.Boms.Activate, "启用/停用 BOM");

        var sales = context.GetGroupOrNull(ErpPermissions.SalesGroup)
                    ?? context.AddGroup(ErpPermissions.SalesGroup, "ERP 销售");
        AddCrud(sales, ErpPermissions.SalesOrders.Default, "销售订单");
        AddCrud(sales, ErpPermissions.SalesShipments.Default, "销售发货单");
        sales.AddPermission(ErpPermissions.SalesOrders.Submit, "提交销售订单");
        sales.AddPermission(ErpPermissions.SalesOrders.Ship, "执行订单发货");
        sales.AddPermission(ErpPermissions.SalesOrders.Close, "关闭销售订单");
        sales.AddPermission(ErpPermissions.SalesShipments.Post, "发货过账");
        sales.AddPermission(ErpPermissions.SalesApproval.Search, "查看销售审批待办");
        sales.AddPermission(ErpPermissions.SalesApproval.Complete, "处理销售审批");
        masterData.AddPermission(ErpPermissions.Dashboard.Get, "查看 ERP 看板");

        var production = context.GetGroupOrNull(ErpPermissions.ProductionGroup)
                          ?? context.AddGroup(ErpPermissions.ProductionGroup, "ERP 生产");
        AddCrud(production, ErpPermissions.WorkOrders.Default, "工单");
        production.AddPermission(ErpPermissions.WorkOrders.Release, "释放工单");
        production.AddPermission(ErpPermissions.WorkOrders.Report, "生产报工");
        production.AddPermission(ErpPermissions.WorkOrders.Close, "关闭工单");

        var finance = context.GetGroupOrNull(ErpPermissions.FinanceGroup)
                      ?? context.AddGroup(ErpPermissions.FinanceGroup, "ERP 财务");
        finance.AddPermission(ErpPermissions.ArApLedger.Get, "查看应收应付");
        finance.AddPermission(ErpPermissions.ArApLedger.Search, "查询应收应付");
        finance.AddPermission(ErpPermissions.ArApLedger.Settle, "应收应付结算");
    }

    private static void AddCrud(PermissionGroupDefinition group, string prefix, string displayName)
    {
        group.AddPermission(prefix + ".Create", $"创建{displayName}");
        group.AddPermission(prefix + ".Get", $"查看{displayName}");
        group.AddPermission(prefix + ".Search", $"查询{displayName}列表");
        group.AddPermission(prefix + ".Update", $"更新{displayName}");
        group.AddPermission(prefix + ".Delete", $"删除{displayName}");
    }
}