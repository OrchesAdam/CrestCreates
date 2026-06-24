using CrestCreates.MultiTenancy.Abstract;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.Data.EFCore.MultiTenancy
{
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
