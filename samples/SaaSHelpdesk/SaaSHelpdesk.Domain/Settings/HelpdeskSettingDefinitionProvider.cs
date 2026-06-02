using CrestCreates.Domain.Shared.Settings;
using CrestCreates.Domain.Settings;

namespace SaaSHelpdesk.Domain.Settings;

public class HelpdeskSettingDefinitionProvider : ISettingDefinitionProvider
{
    public void Define(SettingDefinitionContext context)
    {
        var ticketGroup = context.GetOrAddGroup("Ticket", "工单设置");
        ticketGroup.AddDefinition(
            "SaaSHelpdesk.AutoCloseTicketsDays",
            "工单自动关闭天数",
            "已解决工单在N天后自动关闭",
            "30",
            SettingValueType.Int,
            false,
            SettingScope.Tenant);

        var slaGroup = context.GetOrAddGroup("SLA", "SLA设置");
        slaGroup.AddDefinition(
            "SaaSHelpdesk.DefaultSLAResponseHours",
            "默认SLA响应时限(小时)",
            "工单首次响应的默认时限",
            "8",
            SettingValueType.Int,
            false,
            SettingScope.Tenant);
        slaGroup.AddDefinition(
            "SaaSHelpdesk.DefaultSLAResolutionHours",
            "默认SLA解决时限(小时)",
            "工单解决的默认时限",
            "24",
            SettingValueType.Int,
            false,
            SettingScope.Tenant);

        var knowledgeGroup = context.GetOrAddGroup("KnowledgeBase", "知识库设置");
        knowledgeGroup.AddDefinition(
            "SaaSHelpdesk.EnableKnowledgeBase",
            "启用知识库",
            "是否启用知识库功能",
            bool.TrueString.ToLowerInvariant(),
            SettingValueType.Bool,
            false,
            SettingScope.Tenant);

        var registrationGroup = context.GetOrAddGroup("Registration", "注册设置");
        registrationGroup.AddDefinition(
            "SaaSHelpdesk.AllowCustomerRegistration",
            "允许客户自助注册",
            "是否允许客户在不通过客服的情况下注册",
            bool.TrueString.ToLowerInvariant(),
            SettingValueType.Bool,
            false,
            SettingScope.Tenant);

        var attachmentGroup = context.GetOrAddGroup("Attachment", "附件设置");
        attachmentGroup.AddDefinition(
            "SaaSHelpdesk.MaxAttachmentSizeMB",
            "附件最大大小(MB)",
            "上传附件的大小上限",
            "10",
            SettingValueType.Int,
            false,
            SettingScope.Tenant);
    }
}
