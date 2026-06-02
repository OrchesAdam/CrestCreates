using CrestCreates.Domain.Shared.Features;
using CrestCreates.Domain.Features;

namespace SaaSHelpdesk.Domain.Features;

public class HelpdeskFeatureDefinitionProvider : IFeatureDefinitionProvider
{
    public void Define(FeatureDefinitionContext context)
    {
        var helpdeskGroup = context.GetOrAddGroup("SaaSHelpdesk", "工单系统");

        helpdeskGroup.AddDefinition(
            "SaaSHelpdesk.TicketManagement",
            "工单管理",
            "启用工单管理功能",
            bool.TrueString.ToLowerInvariant(),
            FeatureValueType.Bool,
            true,
            true,
            FeatureScope.Global | FeatureScope.Tenant);

        helpdeskGroup.AddDefinition(
            "SaaSHelpdesk.KnowledgeBase",
            "知识库",
            "启用知识库功能",
            bool.TrueString.ToLowerInvariant(),
            FeatureValueType.Bool,
            true,
            true,
            FeatureScope.Global | FeatureScope.Tenant);

        helpdeskGroup.AddDefinition(
            "SaaSHelpdesk.SLAManagement",
            "SLA管理",
            "启用SLA策略管理功能",
            bool.TrueString.ToLowerInvariant(),
            FeatureValueType.Bool,
            true,
            true,
            FeatureScope.Global | FeatureScope.Tenant);

        helpdeskGroup.AddDefinition(
            "SaaSHelpdesk.Reporting",
            "报表功能",
            "启用高级报表功能",
            bool.FalseString.ToLowerInvariant(),
            FeatureValueType.Bool,
            true,
            true,
            FeatureScope.Global | FeatureScope.Tenant);

        helpdeskGroup.AddDefinition(
            "SaaSHelpdesk.MaxTicketsPerMonth",
            "月工单上限",
            "每月最多处理的工单数量",
            "1000",
            FeatureValueType.Int,
            true,
            true,
            FeatureScope.Global | FeatureScope.Tenant);
    }
}
