using System;
using FreeSql;
using CrestCreates.Domain.Shared.Entities.Auditing;

namespace CrestCreates.OrmProviders.FreeSqlProvider.Interceptors
{
    /// <summary>
    /// FreeSql 审计拦截器配置
    /// 自动填充审计字段
    /// </summary>
    public static class FreeSqlAuditInterceptor
    {
        // 用于跟踪当前是否正在处理软删除
        private static readonly System.Threading.AsyncLocal<bool> _isDeletingContext = new System.Threading.AsyncLocal<bool>();

        /// <summary>
        /// 配置 FreeSql 审计拦截器
        /// </summary>
        public static void ConfigureAuditInterceptor(this IFreeSql freeSql, ICurrentUserProvider currentUserProvider)
        {
            // 插入前拦截
            freeSql.Aop.AuditValue += (sender, e) =>
            {
                var now = DateTime.UtcNow;
                var currentUserId = currentUserProvider?.GetCurrentUserId();

                // 处理审计实体
                if (e.Object is IAuditedEntity)
                {
                    switch (e.AuditValueType)
                    {
                        case FreeSql.Aop.AuditValueType.Insert:
                            // 插入操作 - 设置创建信息
                            if (e.Property.Name == nameof(IAuditedEntity.CreationTime))
                            {
                                e.Value = now;
                            }
                            else if (e.Property.Name == nameof(IAuditedEntity.CreatorId) && currentUserId.HasValue)
                            {
                                e.Value = currentUserId.Value;
                            }
                            break;

                        case FreeSql.Aop.AuditValueType.Update:
                            // 更新操作 - 设置修改信息
                            if (e.Property.Name == nameof(IAuditedEntity.LastModificationTime))
                            {
                                e.Value = now;
                            }
                            else if (e.Property.Name == nameof(IAuditedEntity.LastModifierId) && currentUserId.HasValue)
                            {
                                e.Value = currentUserId.Value;
                            }
                            break;
                    }
                }

                // 处理软删除 - 当 IsDeleted 被设置为 true 时，自动填充删除相关字段
                if (e.Object is ISoftDelete && e.AuditValueType == FreeSql.Aop.AuditValueType.Update)
                {
                    // 检测到 IsDeleted 属性被设置为 true，标记正在删除
                    if (e.Property.Name == nameof(ISoftDelete.IsDeleted) && e.Value is bool isDeleted && isDeleted)
                    {
                        _isDeletingContext.Value = true;
                    }

                    // 如果正在删除，自动填充删除时间和删除人
                    if (_isDeletingContext.Value)
                    {
                        if (e.Property.Name == nameof(ISoftDelete.DeletionTime))
                        {
                            e.Value = now;
                        }
                        else if (e.Property.Name == nameof(ISoftDelete.DeleterId) && currentUserId.HasValue)
                        {
                            e.Value = currentUserId.Value;
                        }
                    }
                }
            };

            // 配置软删除过滤器
            ConfigureSoftDeleteFilter(freeSql);
        }

        /// <summary>
        /// 配置软删除过滤器
        /// </summary>
        public static void ConfigureSoftDeleteFilter(this IFreeSql freeSql)
        {
            // 全局软删除过滤器
            freeSql.GlobalFilter.Apply<ISoftDelete>("SoftDelete", entity => entity.IsDeleted == false);
        }

        /// <summary>
        /// 配置软删除为逻辑删除
        /// </summary>
        public static void ConfigureSoftDelete(this IFreeSql freeSql)
        {
            // 配置删除行为
            freeSql.Aop.CurdBefore += (sender, e) =>
            {
                if (e.CurdType == FreeSql.Aop.CurdType.Delete && e.EntityType != null)
                {
                    // 检查实体是否实现了软删除接口
                    if (typeof(ISoftDelete).IsAssignableFrom(e.EntityType))
                    {
                        // todo 将删除操作转换为更新操作
                        // 注意：这需要在具体的仓储实现中处理
                        // 这里仅作标记
                    }
                }
            };
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
