using CrestCreates.Domain.Shared.Settings;
using CrestCreates.Domain.Settings;

namespace ErpManagement.Domain.Settings;

public class ErpSettingDefinitionProvider : ISettingDefinitionProvider
{
    public void Define(SettingDefinitionContext context)
    {
        var approvalGroup = context.GetOrAddGroup("ErpApproval", "审批设置");
        approvalGroup.AddDefinition(
            "Erp.PurchaseRequisitionApprovalThreshold",
            "请购审批金额阈值",
            "请购总金额超过该值时必须走人工审批，否则自动批准",
            "10000",
            SettingValueType.Int,
            false,
            SettingScope.Tenant);
        approvalGroup.AddDefinition(
            "Erp.SalesOrderApprovalThreshold",
            "销售订单审批金额阈值",
            "销售订单总金额超过该值时必须走人工审批，否则自动批准",
            "50000",
            SettingValueType.Int,
            false,
            SettingScope.Tenant);

        var inventoryGroup = context.GetOrAddGroup("ErpInventory", "库存设置");
        inventoryGroup.AddDefinition(
            "Erp.LowStockAlertEnabled",
            "启用低库存预警",
            "定时作业扫描低于安全库存的物料并生成预警",
            bool.TrueString.ToLowerInvariant(),
            SettingValueType.Bool,
            false,
            SettingScope.Tenant);
        inventoryGroup.AddDefinition(
            "Erp.AllowNegativeStockGlobal",
            "全局允许负库存",
            "关闭时所有仓库过账前校验现存，仓库级开关只能在此基础上放宽",
            bool.FalseString.ToLowerInvariant(),
            SettingValueType.Bool,
            false,
            SettingScope.Tenant);

        var numberGroup = context.GetOrAddGroup("ErpNumbering", "单据编号");
        numberGroup.AddDefinition(
            "Erp.PurchaseOrderPrefix",
            "采购单号前缀",
            "采购单自动编号前缀",
            "PO",
            SettingValueType.String,
            false,
            SettingScope.Tenant);
        numberGroup.AddDefinition(
            "Erp.SalesOrderPrefix",
            "销售单号前缀",
            "销售单自动编号前缀",
            "SO",
            SettingValueType.String,
            false,
            SettingScope.Tenant);
    }
}