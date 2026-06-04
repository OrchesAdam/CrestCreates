using System;
using SqlSugar;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.DataFilter.Entities;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Data.Abstractions;

namespace CrestCreates.Data.SqlSugar.Interceptors
{
    /// <summary>
    /// SqlSugar 审计拦截器配置
    /// 自动填充审计字段
    /// </summary>
    public static class SqlSugarAuditInterceptor
    {
        /// <summary>
        /// 配置 SqlSugar 审计拦截器（包含审计、软删除、多租户）
        /// </summary>
        public static void ConfigureAuditInterceptor(this SqlSugarClient client, ICurrentUserProvider currentUserProvider, ICurrentTenant? currentTenant = null)
        {
            // 插入前拦截（实体级别）
            client.Aop.DataExecuting = (oldValue, entityInfo) =>
            {
                var now = DateTime.UtcNow;
                var currentUserId = currentUserProvider?.GetCurrentUserId();
                var tenantId = currentTenant?.Id;

                // 1. 处理审计实体
                if (entityInfo.EntityValue is IAuditedEntity auditedEntity)
                {
                    switch (entityInfo.OperationType)
                    {
                        case DataFilterType.InsertByObject:
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

                // 2. 处理多租户实体（插入时自动设置租户ID）
                if (!string.IsNullOrEmpty(tenantId))
                {
                    if (entityInfo.EntityValue is IMultiTenant multiTenant)
                    {
                        switch (entityInfo.OperationType)
                        {
                            case DataFilterType.InsertByObject:
                                if (string.IsNullOrEmpty(multiTenant.TenantId) && entityInfo.PropertyName == nameof(IMultiTenant.TenantId))
                                {
                                    entityInfo.SetValue(tenantId);
                                }
                                break;

                            case DataFilterType.UpdateByObject:
                            case DataFilterType.DeleteByObject:
                                if (!string.IsNullOrEmpty(multiTenant.TenantId) &&
                                    !string.Equals(multiTenant.TenantId, tenantId, StringComparison.Ordinal))
                                {
                                    throw new InvalidOperationException(
                                        $"Cannot modify entity from tenant '{multiTenant.TenantId}' while current tenant is '{tenantId}'");
                                }
                                break;
                        }
                    }

                    if (entityInfo.EntityValue is IMustHaveTenant mustHaveTenant)
                    {
                        switch (entityInfo.OperationType)
                        {
                            case DataFilterType.InsertByObject:
                                if (string.IsNullOrEmpty(mustHaveTenant.TenantId) && entityInfo.PropertyName == nameof(IMustHaveTenant.TenantId))
                                {
                                    entityInfo.SetValue(tenantId);
                                }
                                break;

                            case DataFilterType.UpdateByObject:
                            case DataFilterType.DeleteByObject:
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
        }

        /// <summary>
        /// 配置软删除过滤器
        /// </summary>
        public static void ConfigureSoftDeleteFilter(this SqlSugarClient client)
        {
            // 全局软删除过滤器
            client.QueryFilter.Add(new TableFilterItem<ISoftDelete>(it => it.IsDeleted == false));
        }

        /// <summary>
        /// 配置多租户过滤器
        /// </summary>
        public static void ConfigureMultiTenantFilter(this SqlSugarClient client, ICurrentTenant currentTenant)
        {
            if (currentTenant == null || string.IsNullOrEmpty(currentTenant.Id))
            {
                return;
            }

            var tenantId = currentTenant.Id;

            // 多租户过滤器 - IMultiTenant (nullable TenantId)
            client.QueryFilter.Add(new TableFilterItem<IMultiTenant>(it => it.TenantId == null || it.TenantId == tenantId));

            // 多租户过滤器 - IMustHaveTenant (required TenantId)
            client.QueryFilter.Add(new TableFilterItem<IMustHaveTenant>(it => it.TenantId == tenantId));
        }
    }
}