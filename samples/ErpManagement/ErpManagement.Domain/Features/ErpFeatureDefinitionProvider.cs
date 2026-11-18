using CrestCreates.Domain.Shared.Features;
using CrestCreates.Domain.Features;

namespace ErpManagement.Domain.Features;

public class ErpFeatureDefinitionProvider : IFeatureDefinitionProvider
{
    public void Define(FeatureDefinitionContext context)
    {
        var group = context.GetOrAddGroup("Erp", "ERP 域开关");

        group.AddDefinition(
            "Erp.Procurement",
            "采购管理",
            "启用请购/采购单/收货",
            bool.TrueString.ToLowerInvariant(),
            FeatureValueType.Bool,
            true,
            true,
            FeatureScope.Global | FeatureScope.Tenant);

        group.AddDefinition(
            "Erp.Sales",
            "销售管理",
            "启用销售订单/发货",
            bool.TrueString.ToLowerInvariant(),
            FeatureValueType.Bool,
            true,
            true,
            FeatureScope.Global | FeatureScope.Tenant);

        group.AddDefinition(
            "Erp.Production",
            "生产管理",
            "启用生产工单与 BOM 展开",
            bool.TrueString.ToLowerInvariant(),
            FeatureValueType.Bool,
            true,
            true,
            FeatureScope.Global | FeatureScope.Tenant);

        group.AddDefinition(
            "Erp.Finance",
            "应收应付",
            "启用应收应付台账-lite",
            bool.TrueString.ToLowerInvariant(),
            FeatureValueType.Bool,
            true,
            true,
            FeatureScope.Global | FeatureScope.Tenant);

        group.AddDefinition(
            "Erp.MesIntegration",
            "MES 集成",
            "启用 MES 出站事件与入站 adapter",
            bool.FalseString.ToLowerInvariant(),
            FeatureValueType.Bool,
            true,
            true,
            FeatureScope.Global | FeatureScope.Tenant);

        group.AddDefinition(
            "Erp.CrmIntegration",
            "CRM 集成",
            "启用 CRM 出站事件与入站 adapter",
            bool.FalseString.ToLowerInvariant(),
            FeatureValueType.Bool,
            true,
            true,
            FeatureScope.Global | FeatureScope.Tenant);
    }
}