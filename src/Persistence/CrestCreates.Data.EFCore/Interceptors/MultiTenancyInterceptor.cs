using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Entities.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using CrestCreates.Data.EFCore.MultiTenancy;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Data.EFCore.Interceptors
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
            SetTenantId(eventData.Context!);
            return base.SavingChanges(eventData, result);
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SetTenantId(eventData.Context!);
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void SetTenantId(DbContext context)
        {
            if (context == null)
            {
                return;
            }

            var currentTenantId = Normalize(_currentTenant?.Id);
            foreach (var entry in context.ChangeTracker.Entries())
            {
                if (entry.Entity is IMustHaveTenant mustHaveTenant)
                {
                    ApplyRequiredTenantState(entry, mustHaveTenant, currentTenantId);
                }
                else if (entry.Entity is IMultiTenant multiTenant)
                {
                    ApplyOptionalTenantState(entry, multiTenant, currentTenantId);
                }
            }
        }

        /// <summary>
        /// Mandatory tenant: every write must name one tenant. A tenant context may only write its
        /// own rows; the host may write an explicitly named tenant row (bootstrap/administration) but
        /// never an empty one.
        /// </summary>
        private void ApplyRequiredTenantState(
            Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
            IMustHaveTenant entity,
            string? currentTenantId)
        {
            var entityTenantId = Normalize(entity.TenantId);
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entityTenantId is null)
                    {
                        if (currentTenantId is null)
                        {
                            throw new InvalidOperationException(
                                "Cannot write IMustHaveTenant entity without a tenant: no tenant in current context.");
                        }

                        entity.TenantId = currentTenantId;
                    }
                    else if (currentTenantId is not null &&
                             !string.Equals(entityTenantId, currentTenantId, StringComparison.Ordinal))
                    {
                        throw TenantMismatch(entityTenantId, currentTenantId);
                    }
                    break;
                case EntityState.Modified:
                case EntityState.Deleted:
                    EnsureOwnershipUnchanged(entry, nameof(IMustHaveTenant.TenantId), entityTenantId, currentTenantId, required: true);
                    break;
            }
        }

        /// <summary>
        /// Optional tenant: empty means a host-shared row. A tenant context may only write its own
        /// rows or stamp an unassigned insert; the host owns only shared rows.
        /// </summary>
        private void ApplyOptionalTenantState(
            Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
            IMultiTenant entity,
            string? currentTenantId)
        {
            var entityTenantId = Normalize(entity.TenantId);
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entityTenantId is null)
                    {
                        if (currentTenantId is not null)
                        {
                            entity.TenantId = currentTenantId;
                        }
                    }
                    else if (!string.Equals(entityTenantId, currentTenantId, StringComparison.Ordinal))
                    {
                        throw TenantMismatch(entityTenantId, currentTenantId);
                    }
                    break;
                case EntityState.Modified:
                case EntityState.Deleted:
                    EnsureOwnershipUnchanged(entry, nameof(IMultiTenant.TenantId), entityTenantId, currentTenantId, required: false);
                    break;
            }
        }

        private static void EnsureOwnershipUnchanged(
            Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
            string propertyName,
            string? entityTenantId,
            string? currentTenantId,
            bool required)
        {
            var originalTenantId = Normalize(entry.Property(propertyName).OriginalValue as string);

            if (required && entityTenantId is null)
            {
                throw new InvalidOperationException(
                    "Cannot write IMustHaveTenant entity without a persisted tenant id.");
            }

            if (originalTenantId is not null &&
                !string.Equals(entityTenantId, originalTenantId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Cannot change tenant ownership from '{originalTenantId}' to '{entityTenantId}'.");
            }

            if (!string.Equals(entityTenantId, currentTenantId, StringComparison.Ordinal))
            {
                throw TenantMismatch(entityTenantId, currentTenantId);
            }
        }

        private static string? Normalize(string? tenantId)
            => string.IsNullOrEmpty(tenantId) ? null : tenantId;

        private static InvalidOperationException TenantMismatch(string? entityTenantId, string? currentTenantId)
            => new(
                $"Cannot write entity from tenant '{entityTenantId ?? "<shared>"}' while current tenant is '{currentTenantId ?? "<host>"}'");
    }
}
