using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Entities.Auditing;
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

            foreach (var entry in context.ChangeTracker.Entries())
            {
                if (entry.Entity is IMultiTenant multiTenant)
                {
                    ApplyTenantState(entry.State, multiTenant);
                }
                else if (entry.Entity is IMustHaveTenant mustHaveTenant)
                {
                    ApplyTenantState(entry.State, mustHaveTenant);
                }
            }
        }

        private void ApplyTenantState(EntityState state, IMultiTenant entity)
        {
            switch (state)
            {
                case EntityState.Added:
                    if (string.IsNullOrEmpty(entity.TenantId))
                    {
                        entity.TenantId = _currentTenant.Id;
                    }
                    break;
                case EntityState.Modified:
                case EntityState.Deleted:
                    EnsureTenantMatch(entity.TenantId);
                    break;
            }
        }

        private void ApplyTenantState(EntityState state, IMustHaveTenant entity)
        {
            switch (state)
            {
                case EntityState.Added:
                    if (string.IsNullOrEmpty(entity.TenantId))
                    {
                        entity.TenantId = _currentTenant.Id;
                    }
                    break;
                case EntityState.Modified:
                case EntityState.Deleted:
                    EnsureTenantMatch(entity.TenantId);
                    break;
            }
        }

        private void EnsureTenantMatch(string entityTenantId)
        {
            if (!string.Equals(entityTenantId, _currentTenant.Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Cannot modify entity from tenant '{entityTenantId}' while current tenant is '{_currentTenant.Id}'");
            }
        }
    }
}
