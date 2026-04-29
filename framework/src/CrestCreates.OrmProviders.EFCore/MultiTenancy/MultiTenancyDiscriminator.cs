using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
        private static readonly ConcurrentDictionary<string, ApplyAllDelegate> ApplyAllDelegates = new(StringComparer.Ordinal);

        public delegate void ApplyAllDelegate(ModelBuilder modelBuilder, ICurrentTenant currentTenant);

        /// <summary>
        /// 注册编译时生成的 ApplyAll 实现
        /// </summary>
        public static void Register(ApplyAllDelegate applyAll)
        {
            ArgumentNullException.ThrowIfNull(applyAll);
            ApplyAllDelegates.TryAdd(applyAll.Method.DeclaringType?.AssemblyQualifiedName ?? applyAll.Method.Name, applyAll);
        }

        /// <summary>
        /// 已注册的 ApplyAll 委托数量
        /// </summary>
        public static int Count => ApplyAllDelegates.Count;

        /// <summary>
        /// 是否存在已注册的编译期生成过滤器
        /// </summary>
        public static bool HasRegistrations => Count > 0;

        /// <summary>
        /// 获取所有已注册的 ApplyAll 委托
        /// </summary>
        public static IReadOnlyCollection<ApplyAllDelegate> GetApplyAllDelegates()
        {
            return ApplyAllDelegates.Values.ToArray();
        }

        internal static void Clear()
        {
            ApplyAllDelegates.Clear();
        }

        public static InvalidOperationException CreateMissingGeneratedFiltersException()
        {
            return new InvalidOperationException(
                "Tenant discriminator 未找到编译期生成的过滤器注册，当前主链只支持生成链。请确认包含多租户实体的 DbContext 项目引用了 CrestCreates.OrmProviders.EFCore 且 Source Generator 已运行。");
        }
    }

    /// <summary>
    /// 租户过滤器配置
    /// 优先使用 Source Generator 通过 TenantFilterRegistryStore 注册的编译时实现
    /// 未注册时抛出异常，避免静默绕过租户隔离
    /// </summary>
    public static class TenantFilterConfiguration
    {
        /// <summary>
        /// 应用所有多租户查询过滤器
        /// </summary>
        public static void ApplyAll(ModelBuilder modelBuilder, ICurrentTenant currentTenant)
        {
            if (!TenantFilterRegistryStore.HasRegistrations)
            {
                throw TenantFilterRegistryStore.CreateMissingGeneratedFiltersException();
            }

            foreach (var applyAll in TenantFilterRegistryStore.GetApplyAllDelegates())
            {
                applyAll(modelBuilder, currentTenant);
            }
        }
    }
}
