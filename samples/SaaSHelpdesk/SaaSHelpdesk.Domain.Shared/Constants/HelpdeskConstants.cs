namespace SaaSHelpdesk.Domain.Shared.Constants;

public static class HelpdeskConstants
{
    // 工单
    public const int MaxTicketTitleLength = 200;
    public const int MaxTicketDescriptionLength = 4000;
    
    // 消息
    public const int MaxMessageContentLength = 10000;
    
    // 客户
    public const int MaxCustomerNameLength = 100;
    public const int MaxEmailLength = 256;
    
    // 分类
    public const int MaxCategoryNameLength = 50;
    
    // 知识库
    public const int MaxArticleTitleLength = 200;
    public const int MaxArticleContentLength = 50000;
    
    // 附件
    public const int MaxAttachmentFileNameLength = 255;
    public const int MaxFileSizeBytes = 10_485_760; // 10MB
    
    // SLA 默认值
    public const double DefaultSLAResponseHours = 8;
    public const double DefaultSLAResolutionHours = 24;
    
    // 默认 SLA 时间（分钟）
    public const int LowPriorityResponseMinutes = 240;
    public const int LowPriorityResolutionMinutes = 1440;
    public const int MediumPriorityResponseMinutes = 120;
    public const int MediumPriorityResolutionMinutes = 480;
    public const int HighPriorityResponseMinutes = 60;
    public const int HighPriorityResolutionMinutes = 240;
    public const int UrgentPriorityResponseMinutes = 30;
    public const int UrgentPriorityResolutionMinutes = 120;
}
