using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Shared.DTOs;

namespace CrestCreates.Domain.Repositories;

/// <summary>
/// 审计日志仓储接口
/// </summary>
public interface IAuditLogRepository
{
    /// <summary>
    /// 分页查询审计日志（数据库层分页）
    /// </summary>
    Task<PagedResult<CrestCreates.Domain.AuditLog.AuditLog>> GetPagedListAsync(
        DateTime? startTime,
        DateTime? endTime,
        string? userId,
        string? userName,
        string? tenantId,
        int? status,
        string? httpMethod,
        string? keyword,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);
}
