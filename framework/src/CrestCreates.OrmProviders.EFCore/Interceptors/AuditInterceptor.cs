using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.Authorization.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.OrmProviders.EFCore.Interceptors
{
    /// <summary>
    /// EF Core 审计拦截器
    /// 自动填充审计字段（创建时间、修改时间、创建人、修改人等）
    /// </summary>
    public class AuditInterceptor : SaveChangesInterceptor
    {
        private readonly ICurrentUser _currentUser;

        public AuditInterceptor(ICurrentUser currentUser)
        {
            _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
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

            var currentUserId = Guid.TryParse(_currentUser.Id, out var parsedUserId) ? parsedUserId : (Guid?)null;
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
}
