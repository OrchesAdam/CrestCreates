using System;
using CrestCreates.Application.Contracts.DTOs.Common;

namespace CrestCreates.Application.Contracts.DTOs.AuditLog;

/// <summary>
/// 审计日志分页查询请求 DTO
/// </summary>
public class AuditLogPagedRequestDto : PagedRequestDto
{
    /// <summary>
    /// 开始时间（ExecutionTime >= StartTime）
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// 结束时间（ExecutionTime <= EndTime）
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// 用户ID精确匹配
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// 用户名关键词匹配
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// 租户ID精确匹配
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// 状态过滤：0=成功，1=失败
    /// </summary>
    public int? Status { get; set; }

    /// <summary>
    /// HTTP方法过滤（GET/POST/PUT/DELETE等）
    /// </summary>
    public string? HttpMethod { get; set; }

    /// <summary>
    /// 关键词搜索（匹配 Url、ServiceName、MethodName）
    /// </summary>
    public string? Keyword { get; set; }
}
