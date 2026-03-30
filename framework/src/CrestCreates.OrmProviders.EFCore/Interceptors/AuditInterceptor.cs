using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using CrestCreates.Domain.Shared.Entities.Auditing;

namespace CrestCreates.OrmProviders.EFCore.Interceptors
{
    /// <summary>
    /// EF Core 审计拦截器
    /// 自动填充审计字段（创建时间、修改时间、创建人、修改人等）
    /// </summary>
    public class AuditInterceptor : SaveChangesInterceptor
    {
        private readonly ICurrentUserProvider _currentUserProvider;

        public AuditInterceptor(ICurrentUserProvider currentUserProvider)
        {
            _currentUserProvider = currentUserProvider;
        }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            UpdateAuditFields(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            UpdateAuditFields(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void UpdateAuditFields(DbContext context)
        {
            if (context == null)
                return;

            var currentUserId = _currentUserProvider?.GetCurrentUserId();
            var now = DateTime.UtcNow;

            foreach (var entry in context.ChangeTracker.Entries())
            {
                // 处理审计实体
                if (entry.Entity is IAuditedEntity auditedEntity)
                {
                    switch (entry.State)
                    {
                        case EntityState.Added:
                            auditedEntity.CreationTime = now;
                            if (currentUserId.HasValue)
                                auditedEntity.CreatorId = currentUserId;
                            break;

                        case EntityState.Modified:
                            auditedEntity.LastModificationTime = now;
                            if (currentUserId.HasValue)
                                auditedEntity.LastModifierId = currentUserId;
                            break;
                    }
                }

                // 处理软删除实体
                if (entry.Entity is ISoftDelete softDeleteEntity && entry.State == EntityState.Deleted)
                {
                    // 将删除操作转换为软删除
                    entry.State = EntityState.Modified;
                    softDeleteEntity.IsDeleted = true;
                    softDeleteEntity.DeletionTime = now;
                    if (currentUserId.HasValue)
                        softDeleteEntity.DeleterId = currentUserId;
                }
            }
        }
    }

    /// <summary>
    /// 当前用户提供者接口
    /// </summary>
    public interface ICurrentUserProvider
    {
        Guid? GetCurrentUserId();
    }

    /// <summary>
    /// 默认当前用户提供者实现
    /// </summary>
    public class DefaultCurrentUserProvider : ICurrentUserProvider
    {
        public Guid? GetCurrentUserId()
        {
            // TODO: 从 HttpContext 或 ClaimsPrincipal 获取当前用户ID
            // 这里返回 null，实际项目中需要实现
            return null;
        }
    }
}
