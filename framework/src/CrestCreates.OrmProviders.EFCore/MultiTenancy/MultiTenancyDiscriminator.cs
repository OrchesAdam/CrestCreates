using System;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.OrmProviders.EFCore.MultiTenancy
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

    /// <summary>
    /// 多租户实体接口
    /// 实现此接口的实体会自动应用租户过滤器
    /// </summary>
    public interface IMultiTenant
    {
        /// <summary>
        /// 租户ID
        /// </summary>
        string TenantId { get; set; }
    }

    /// <summary>
    /// 多租户实体基类
    /// </summary>
    public abstract class MultiTenantEntity : IMultiTenant
    {
        /// <summary>
        /// 租户ID
        /// </summary>
        public virtual string TenantId { get; set; } = string.Empty;
    }

    /// <summary>
    /// 带主键的多租户实体基类
    /// </summary>
    public abstract class MultiTenantEntity<TKey> : MultiTenantEntity
    {
        public virtual TKey Id { get; set; }
    }

    /// <summary>
    /// 租户过滤器配置 — 编译时生成的 partial 类覆盖此 no-op 实现
    /// </summary>
    public static partial class TenantFilterConfiguration
    {
        /// <summary>
        /// 应用所有多租户查询过滤器
        /// 当 Source Generator 发现 [Entity] + IMultiTenant 类时，生成的方法替代此 no-op
        /// </summary>
        public static void ApplyAll(ModelBuilder modelBuilder, ICurrentTenant currentTenant)
        {
            // No IMultiTenant entities found — nothing to configure
        }
    }
}
