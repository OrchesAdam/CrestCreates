using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.AuditLog;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.DTOs;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.EntityFrameworkCore;
using IDbContextProvider = CrestCreates.DbContextProvider.Abstract;

namespace CrestCreates.OrmProviders.EFCore.Repositories;

/// <summary>
/// 审计日志仓储实现
/// </summary>
public class AuditLogRepository : EfCoreRepositoryBase<AuditLog, Guid>, IAuditLogRepository
{
    public AuditLogRepository(
        IDbContextProvider.IDataBaseContext dbContext,
        ICurrentTenant currentTenant)
        : base(dbContext, currentTenant, null)
    {
    }

    public async Task<PagedResult<AuditLog>> GetPagedListAsync(
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
        CancellationToken cancellationToken = default)
    {
        var query = GetQueryable();

        // 多租户过滤：宿主可查全局，租户只能查自己
        // ICurrentTenant.Id 为 null 或 "host" 表示宿主上下文
        if (!string.IsNullOrEmpty(CurrentTenant?.Id) && CurrentTenant.Id != "host")
        {
            query = query.Where(a => a.TenantId == CurrentTenant.Id);
        }

        // 时间范围过滤
        if (startTime.HasValue)
        {
            query = query.Where(a => a.ExecutionTime >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            query = query.Where(a => a.ExecutionTime <= endTime.Value);
        }

        // 用户ID精确匹配
        if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(a => a.UserId == userId);
        }

        // 用户名关键词匹配
        if (!string.IsNullOrWhiteSpace(userName))
        {
            query = query.Where(a => a.UserName != null && a.UserName.Contains(userName));
        }

        // 租户ID过滤（宿主可指定特定租户查询）
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(a => a.TenantId == tenantId);
        }

        // 状态过滤
        if (status.HasValue)
        {
            query = query.Where(a => a.Status == status.Value);
        }

        // HTTP方法过滤
        if (!string.IsNullOrWhiteSpace(httpMethod))
        {
            query = query.Where(a => a.HttpMethod == httpMethod);
        }

        // 关键词搜索：匹配 Url、ServiceName、MethodName
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(a =>
                (a.Url != null && a.Url.Contains(keyword)) ||
                (a.ServiceName != null && a.ServiceName.Contains(keyword)) ||
                (a.MethodName != null && a.MethodName.Contains(keyword)));
        }

        // 按 ExecutionTime 倒序
        query = query.OrderByDescending(a => a.ExecutionTime);

        // 数据库层分页
        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLog>(items, (int)totalCount, pageIndex, pageSize);
    }
}
