using System;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using CrestCreates.MultiTenancy.Abstract;

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
        /// </summary>
        public static void ConfigureTenantDiscriminator(
            this ModelBuilder modelBuilder,
            ICurrentTenant currentTenant,
            string tenantIdPropertyName = "TenantId")
        {
            if (modelBuilder == null) throw new ArgumentNullException(nameof(modelBuilder));
            if (currentTenant == null) throw new ArgumentNullException(nameof(currentTenant));

            // 遍历所有实体类型
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                // 检查实体是否实现了 IMultiTenant 接口
                if (typeof(IMultiTenant).IsAssignableFrom(entityType.ClrType))
                {
                    // 为实体添加全局查询过滤器
                    var method = typeof(MultiTenancyDiscriminatorExtensions)
                        .GetMethod(nameof(SetTenantFilter), 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                        ?.MakeGenericMethod(entityType.ClrType);

                    method?.Invoke(null, new object[] { modelBuilder, currentTenant, tenantIdPropertyName });
                }
            }
        }

        private static void SetTenantFilter<TEntity>(
            ModelBuilder modelBuilder,
            ICurrentTenant currentTenant,
            string tenantIdPropertyName)
            where TEntity : class, IMultiTenant
        {
            // 创建查询过滤器表达式: e => e.TenantId == currentTenant.Id
            var parameter = Expression.Parameter(typeof(TEntity), "e");
            
            // 获取 TenantId 属性
            var tenantIdProperty = Expression.Property(parameter, tenantIdPropertyName);
            
            // 获取当前租户ID
            var currentTenantId = Expression.Constant(currentTenant.Id, typeof(string));
            
            // 创建比较表达式
            var comparison = Expression.Equal(tenantIdProperty, currentTenantId);
            
            // 创建 lambda 表达式
            var lambda = Expression.Lambda<Func<TEntity, bool>>(comparison, parameter);
            
            // 应用查询过滤器
            modelBuilder.Entity<TEntity>().HasQueryFilter(lambda);

            // 配置 TenantId 索引
            modelBuilder.Entity<TEntity>()
                .HasIndex(tenantIdPropertyName)
                .HasDatabaseName($"IX_{typeof(TEntity).Name}_{tenantIdPropertyName}");
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
        public virtual string TenantId { get; set; }
    }

    /// <summary>
    /// 带主键的多租户实体基类
    /// </summary>
    public abstract class MultiTenantEntity<TKey> : MultiTenantEntity
    {
        public virtual TKey Id { get; set; }
    }
}
