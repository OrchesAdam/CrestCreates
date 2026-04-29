using Microsoft.EntityFrameworkCore;

namespace CrestCreates.OrmProviders.EFCore.MultiTenancy
{
    /// <summary>
    /// 租户 DbContext 工厂的运行时注册中心
    /// Source Generator 通过 [ModuleInitializer] 将 GeneratedTenantDbContextFactory 注册到此存储
    /// </summary>
    public static class TenantDbContextFactoryRegistryStore
    {
        private static volatile ITenantDbContextFactory? _factory;

        /// <summary>
        /// 注册编译时生成的 ITenantDbContextFactory 实现
        /// </summary>
        public static void Register(ITenantDbContextFactory factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _factory = factory;
        }

        /// <summary>
        /// 获取已注册的 ITenantDbContextFactory，未注册时返回 null
        /// </summary>
        public static ITenantDbContextFactory? GetFactory() => _factory;
    }
}
