using CrestCreates.Domain.Shared.Attributes;

namespace SaaSHelpdesk.Domain.Shared.Enums;

public enum TicketStatus
{
    [EnumDisplay(Name = "待处理")]
    Open = 1,

    [EnumDisplay(Name = "处理中")]
    InProgress = 2,

    [EnumDisplay(Name = "等待客户回复")]
    WaitingForCustomer = 3,

    [EnumDisplay(Name = "等待第三方")]
    WaitingForThirdParty = 4,

    [EnumDisplay(Name = "已解决")]
    Resolved = 5,

    [EnumDisplay(Name = "已关闭")]
    Closed = 6,
}

public enum TicketPriority
{
    [EnumDisplay(Name = "低")]
    Low = 1,

    [EnumDisplay(Name = "中")]
    Medium = 2,

    [EnumDisplay(Name = "高")]
    High = 3,

    [EnumDisplay(Name = "紧急")]
    Urgent = 4,
}

public enum TicketType
{
    [EnumDisplay(Name = "咨询")]
    Question = 1,

    [EnumDisplay(Name = "故障")]
    Incident = 2,

    [EnumDisplay(Name = "问题")]
    Problem = 3,

    [EnumDisplay(Name = "功能请求")]
    FeatureRequest = 4,

    [EnumDisplay(Name = "任务")]
    Task = 5,
}

public enum MessageSenderType
{
    [EnumDisplay(Name = "客服")]
    Agent = 1,

    [EnumDisplay(Name = "客户")]
    Customer = 2,

    [EnumDisplay(Name = "系统")]
    System = 3,
}

public enum HistoryChangeType
{
    [EnumDisplay(Name = "状态变更")]
    StatusChanged = 1,

    [EnumDisplay(Name = "优先级变更")]
    PriorityChanged = 2,

    [EnumDisplay(Name = "分配")]
    Assigned = 3,

    [EnumDisplay(Name = "重新分配")]
    Reassigned = 4,

    [EnumDisplay(Name = "添加回复")]
    MessageAdded = 5,

    [EnumDisplay(Name = "添加附件")]
    AttachmentAdded = 6,

    [EnumDisplay(Name = "升级")]
    Escalated = 7,

    [EnumDisplay(Name = "SLA违规")]
    SLAViolated = 8,

    [EnumDisplay(Name = "内部备注")]
    Note = 9,

    [EnumDisplay(Name = "自定义")]
    Custom = 10,
}
