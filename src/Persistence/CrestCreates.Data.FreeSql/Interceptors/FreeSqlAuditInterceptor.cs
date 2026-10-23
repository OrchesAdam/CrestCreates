using System;
using FreeSql;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.DataFilter.Entities;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Data.Abstractions;

namespace CrestCreates.Data.FreeSql.Interceptors
{
    /// <summary>
    /// FreeSql 审计拦截器配置
    /// 自动填充审计字段
    /// </summary>
    public static class FreeSqlAuditInterceptor
    {
        /// <summary>
        /// 配置 FreeSql 审计拦截器（包含审计、软删除、多租户）
        /// </summary>
        public static void ConfigureAuditInterceptor(this IFreeSql freeSql, ICurrentUserProvider currentUserProvider, ICurrentTenant? currentTenant = null)
        {
            freeSql.Aop.AuditValue += (sender, e) =>
            {
                var now = DateTime.UtcNow;
                var currentUserId = currentUserProvider?.GetCurrentUserId();
                var tenantId = currentTenant?.Id;

                // 1. 处理审计实体
                if (e.Object is IAuditedEntity)
                {
                    switch (e.AuditValueType)
                    {
                        case global::FreeSql.Aop.AuditValueType.Insert:
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

                        case global::FreeSql.Aop.AuditValueType.Update:
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

                // 2. 处理软删除 - 当 IsDeleted 为 true 时，自动填充删除相关字段
                if (e.Object is ISoftDelete softDelete && e.AuditValueType == global::FreeSql.Aop.AuditValueType.Update)
                {
                    if (softDelete.IsDeleted)
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

                // 3. 处理多租户实体（插入时自动设置租户ID）
                if (!string.IsNullOrEmpty(tenantId))
                {
                    if (e.Object is IMultiTenant multiTenant)
                    {
                        switch (e.AuditValueType)
                        {
                            case global::FreeSql.Aop.AuditValueType.Insert:
                                // 插入时自动设置租户ID（如果未设置）
                                if (string.IsNullOrEmpty(multiTenant.TenantId) && e.Property.Name == nameof(IMultiTenant.TenantId))
                                {
                                    e.Value = tenantId;
                                }
                                break;

                            case global::FreeSql.Aop.AuditValueType.Update:
                                // 更新时验证租户边界
                                if (!string.IsNullOrEmpty(multiTenant.TenantId) &&
                                    !string.Equals(multiTenant.TenantId, tenantId, StringComparison.Ordinal))
                                {
                                    throw new InvalidOperationException(
                                        $"Cannot modify entity from tenant '{multiTenant.TenantId}' while current tenant is '{tenantId}'");
                                }
                                break;
                        }
                    }

                    if (e.Object is IMustHaveTenant mustHaveTenant)
                    {
                        switch (e.AuditValueType)
                        {
                            case global::FreeSql.Aop.AuditValueType.Insert:
                                // 插入时自动设置租户ID（如果未设置）
                                if (string.IsNullOrEmpty(mustHaveTenant.TenantId) && e.Property.Name == nameof(IMustHaveTenant.TenantId))
                                {
                                    e.Value = tenantId;
                                }
                                break;

                            case global::FreeSql.Aop.AuditValueType.Update:
                                // 更新时验证租户边界
                                if (!string.Equals(mustHaveTenant.TenantId, tenantId, StringComparison.Ordinal))
                                {
                                    throw new InvalidOperationException(
                                        $"Cannot modify entity from tenant '{mustHaveTenant.TenantId}' while current tenant is '{tenantId}'");
                                }
                                break;
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
        /// 配置多租户查询过滤器
        /// </summary>
        public static void ConfigureMultiTenantFilter(this IFreeSql freeSql, ICurrentTenant currentTenant)
        {
            throw new NotSupportedException(
                "FreeSql tenant filtering is resolved per operation by FreeSqlRepository. " +
                "Configuring a global filter here would capture a stale tenant; register the repository with ICurrentTenant instead.");
        }

        /// <summary>
        /// 配置软删除为逻辑删除
        /// </summary>
        public static void ConfigureSoftDelete(this IFreeSql freeSql)
        {
            throw new NotSupportedException(
                "FreeSql soft delete is executed as a conditional UPDATE by the repository command. " +
                "This hook is retired and must not be used as a silent no-op configuration.");
        }
    }
}