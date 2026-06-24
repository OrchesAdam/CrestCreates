using System;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.Data.EFCore.MultiTenancy
{
    /// <summary>
    /// 多租户鉴别器模式扩展
    /// 为 EF Core 添加全局查询过滤器，自动过滤租户数据
    /// </summary>
    public static class MultiTenancyDiscriminatorExtensions
    {
        /// <summary>
        /// 配置多租户鉴别器模式的全局查询过滤器
        /// 使用编译时生成的 TenantFilterConfiguration 替代运行时反射
        /// </summary>
        public static void ConfigureTenantDiscriminator(
            this ModelBuilder modelBuilder,
            ICurrentTenant currentTenant,
            string tenantIdPropertyName = "TenantId")
        {
            if (modelBuilder == null) throw new ArgumentNullException(nameof(modelBuilder));
            if (currentTenant == null) throw new ArgumentNullException(nameof(currentTenant));

            TenantFilterConfiguration.ApplyAll(modelBuilder, currentTenant);
        }

        /// <summary>
        /// 为支持多租户的实体自动设置租户ID
        /// </summary>
        public static void SetTenantId<TEntity>(
            this TEntity entity,
            ICurrentTenant currentTenant)
            where TEntity : class, IMultiTenant
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (currentTenant == null) throw new ArgumentNullException(nameof(currentTenant));

            if (string.IsNullOrEmpty(entity.TenantId))
            {
                entity.TenantId = currentTenant.Id;
            }
        }
    }
}
