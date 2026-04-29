using System;
using System.Collections.Generic;
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
    /// 租户过滤器配置的运行时注册中心
    /// Source Generator 通过 [ModuleInitializer] 将生成的 ApplyAll 委托注册到此存储
    /// </summary>
    public static class TenantFilterRegistryStore
    {
        private static volatile ApplyAllDelegate? _applyAll;

        public delegate void ApplyAllDelegate(ModelBuilder modelBuilder, ICurrentTenant currentTenant);

        /// <summary>
        /// 注册编译时生成的 ApplyAll 实现
        /// </summary>
        public static void Register(ApplyAllDelegate applyAll)
        {
            ArgumentNullException.ThrowIfNull(applyAll);
            _applyAll = applyAll;
        }

        /// <summary>
        /// 获取已注册的 ApplyAll 委托，未注册时返回 null
        /// </summary>
        public static ApplyAllDelegate? GetApplyAll() => _applyAll;
    }

    /// <summary>
    /// 租户过滤器配置
    /// 优先使用 Source Generator 通过 TenantFilterRegistryStore 注册的编译时实现
    /// 未注册时为 no-op（无 IMultiTenant 实体或生成器未运行）
    /// </summary>
    public static class TenantFilterConfiguration
    {
        /// <summary>
        /// 应用所有多租户查询过滤器
        /// </summary>
        public static void ApplyAll(ModelBuilder modelBuilder, ICurrentTenant currentTenant)
        {
            TenantFilterRegistryStore.GetApplyAll()?.Invoke(modelBuilder, currentTenant);
        }
    }
}
