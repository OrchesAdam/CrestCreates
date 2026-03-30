using System;
using SqlSugar;
using CrestCreates.Domain.Shared.Entities.Auditing;

namespace CrestCreates.OrmProviders.SqlSugar.Interceptors
{
    /// <summary>
    /// SqlSugar 审计拦截器配置
    /// 自动填充审计字段
    /// </summary>
    public static class SqlSugarAuditInterceptor
    {
        /// <summary>
        /// 配置 SqlSugar 审计拦截器
        /// </summary>
        public static void ConfigureAuditInterceptor(this SqlSugarClient client, ICurrentUserProvider currentUserProvider)
        {
            // 插入前拦截（实体级别）
            client.Aop.DataExecuting = (oldValue, entityInfo) =>
            {
                if (entityInfo.EntityValue is IAuditedEntity auditedEntity)
                {
                    var now = DateTime.UtcNow;
                    var currentUserId = currentUserProvider?.GetCurrentUserId();

                    switch (entityInfo.OperationType)
                    {
                        case DataFilterType.InsertByObject:
                            // 插入操作
                            if (entityInfo.PropertyName == nameof(IAuditedEntity.CreationTime))
                            {
                                entityInfo.SetValue(now);
                            }
                            else if (entityInfo.PropertyName == nameof(IAuditedEntity.CreatorId) && currentUserId.HasValue)
                            {
                                entityInfo.SetValue(currentUserId.Value);
                            }
                            break;

                        case DataFilterType.UpdateByObject:
                            // 更新操作
                            if (entityInfo.PropertyName == nameof(IAuditedEntity.LastModificationTime))
                            {
                                entityInfo.SetValue(now);
                            }
                            else if (entityInfo.PropertyName == nameof(IAuditedEntity.LastModifierId) && currentUserId.HasValue)
                            {
                                entityInfo.SetValue(currentUserId.Value);
                            }
                            break;

                        case DataFilterType.DeleteByObject:
                            // 删除操作 - 如果是软删除实体，转换为更新操作
                            if (entityInfo.EntityValue is ISoftDelete softDelete)
                            {
                                if (entityInfo.PropertyName == nameof(ISoftDelete.IsDeleted))
                                {
                                    entityInfo.SetValue(true);
                                }
                                else if (entityInfo.PropertyName == nameof(ISoftDelete.DeletionTime))
                                {
                                    entityInfo.SetValue(now);
                                }
                                else if (entityInfo.PropertyName == nameof(ISoftDelete.DeleterId) && currentUserId.HasValue)
                                {
                                    entityInfo.SetValue(currentUserId.Value);
                                }
                            }
                            break;
                    }
                }
            };
        }

        /// <summary>
        /// 配置软删除过滤器
        /// </summary>
        public static void ConfigureSoftDeleteFilter(this SqlSugarClient client)
        {
            // 全局软删除过滤器
            client.QueryFilter.Add(new TableFilterItem<ISoftDelete>(it => it.IsDeleted == false));
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
            return null;
        }
    }
}
