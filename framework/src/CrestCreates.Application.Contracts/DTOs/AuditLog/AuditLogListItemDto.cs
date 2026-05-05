using System;

namespace CrestCreates.Application.Contracts.DTOs.AuditLog;

/// <summary>
/// 审计日志列表项 DTO
/// </summary>
public class AuditLogListItemDto
{
    /// <summary>
    /// 审计日志ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 执行时间点
    /// </summary>
    public DateTime ExecutionTime { get; set; }

    /// <summary>
    /// 执行时长（毫秒）
    /// </summary>
    public long Duration { get; set; }

    /// <summary>
    /// 用户ID
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// 租户ID
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// HTTP方法
    /// </summary>
    public string? HttpMethod { get; set; }

    /// <summary>
    /// 请求URL
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// 服务名称
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// 方法名称
    /// </summary>
    public string? MethodName { get; set; }

    /// <summary>
    /// 状态：0=成功，1=失败
    /// </summary>
    public int Status { get; set; }

    /// <summary>
    /// 客户端IP
    /// </summary>
    public string? ClientIpAddress { get; set; }

    /// <summary>
    /// 额外扩展数据（如 FeatureChanges）
    /// </summary>
    public System.Collections.Generic.Dictionary<string, object>? ExtraProperties { get; set; }
}
