using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using CrestCreates.OrmProviders.EFCore.MultiTenancy;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.OrmProviders.EFCore.Interceptors
{
    /// <summary>
    /// 多租户保存拦截器
    /// 在保存实体时自动设置租户ID
    /// </summary>
    public class MultiTenancyInterceptor : SaveChangesInterceptor
    {
        private readonly ICurrentTenant _currentTenant;

        public MultiTenancyInterceptor(ICurrentTenant currentTenant)
        {
            _currentTenant = currentTenant ?? throw new ArgumentNullException(nameof(currentTenant));
        }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            SetTenantId(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SetTenantId(eventData.Context);
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void SetTenantId(DbContext context)
        {
            if (context == null || string.IsNullOrEmpty(_currentTenant?.Id))
            {
                return;
            }

            var entries = context.ChangeTracker.Entries<IMultiTenant>();

            foreach (var entry in entries)
            {
                if (entry.State == EntityState.Added)
                {
                    // 为新增实体设置租户ID
                    if (string.IsNullOrEmpty(entry.Entity.TenantId))
                    {
                        entry.Entity.TenantId = _currentTenant.Id;
                    }
                }
                else if (entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
                {
                    // 验证租户ID是否匹配（防止跨租户操作）
                    if (entry.Entity.TenantId != _currentTenant.Id)
                    {
                        throw new InvalidOperationException(
                            $"Cannot modify entity from tenant '{entry.Entity.TenantId}' " +
                            $"while current tenant is '{_currentTenant.Id}'");
                    }
                }
            }
        }
    }
}
