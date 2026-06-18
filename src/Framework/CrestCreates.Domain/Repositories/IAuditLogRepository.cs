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

    /// <summary>
    /// 删除指定时间之前的审计日志（多租户边界：宿主可删全局，租户只能删自己）
    /// </summary>
    /// <param name="beforeTime">截止时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>删除的日志数量</returns>
    Task<int> DeleteOlderThanAsync(DateTime beforeTime, CancellationToken cancellationToken = default);
}
