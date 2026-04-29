using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.OrmProviders.EFCore.MultiTenancy
{
    /// <summary>
    /// 租户 DbContext 工厂的运行时注册中心
    /// Source Generator 通过 [ModuleInitializer] 将 GeneratedTenantDbContextFactory 注册到此存储
    /// </summary>
    public static class TenantDbContextFactoryRegistryStore
    {
        private static readonly ConcurrentDictionary<string, ITenantDbContextFactory> Factories = new(StringComparer.Ordinal);

        /// <summary>
        /// 注册编译时生成的 ITenantDbContextFactory 实现
        /// </summary>
        public static void Register(ITenantDbContextFactory factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            Factories.TryAdd(factory.GetType().AssemblyQualifiedName ?? factory.GetType().FullName ?? factory.GetType().Name, factory);
        }

        /// <summary>
        /// 获取所有已注册的 ITenantDbContextFactory
        /// </summary>
        public static IReadOnlyCollection<ITenantDbContextFactory> GetFactories()
        {
            return Factories.Values.ToArray();
        }

        public static ITenantDbContextFactory BuildRequiredFactory()
        {
            var factories = GetFactories();
            if (factories.Count == 0)
            {
                throw CreateMissingGeneratedFactoryException();
            }

            return factories.Count == 1
                ? factories.First()
                : new CompositeTenantDbContextFactory(factories);
        }

        public static InvalidOperationException CreateMissingGeneratedFactoryException()
        {
            return new InvalidOperationException(
                "Tenant DbContext factory 未找到编译期生成的实现，当前主链只支持生成链。请确认包含 DbContext 的项目引用了 CrestCreates.OrmProviders.EFCore 且 Source Generator 已运行。");
        }
    }

    internal sealed class CompositeTenantDbContextFactory : ITenantDbContextFactory
    {
        private readonly IReadOnlyCollection<ITenantDbContextFactory> _factories;

        public CompositeTenantDbContextFactory(IReadOnlyCollection<ITenantDbContextFactory> factories)
        {
            _factories = factories;
        }

        public TDbContext Create<TDbContext>(DbContextOptions<TDbContext> options)
            where TDbContext : DbContext
        {
            foreach (var factory in _factories)
            {
                try
                {
                    return factory.Create(options);
                }
                catch (InvalidOperationException ex) when (ex.Message.StartsWith("No factory registered for ", StringComparison.Ordinal))
                {
                }
            }

            throw new InvalidOperationException(
                $"No generated tenant DbContext factory registered for {typeof(TDbContext).Name}.");
        }
    }
}
