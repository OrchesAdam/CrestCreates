using System;

namespace CrestCreates.Application.Contracts.DTOs.AuditLog;

/// <summary>
/// 清理审计日志请求 DTO
/// </summary>
public class CleanupAuditLogsRequestDto
{
    /// <summary>
    /// 保留天数，清理此天数之前的日志
    /// </summary>
    public int? RetentionDays { get; set; }

    /// <summary>
    /// 截止时间，清理此时间之前的日志（优先级高于 RetentionDays）
    /// </summary>
    public DateTime? BeforeTime { get; set; }
}

/// <summary>
/// 清理审计日志结果 DTO
/// </summary>
public class CleanupAuditLogsResultDto
{
    /// <summary>
    /// 删除的日志数量
    /// </summary>
    public int DeletedCount { get; set; }
}
