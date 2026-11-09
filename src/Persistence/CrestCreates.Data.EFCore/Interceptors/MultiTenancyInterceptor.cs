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
                    EnsureRequiredOwnershipUnchanged(entry, nameof(IMustHaveTenant.TenantId), entityTenantId, currentTenantId);
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
                    EnsureOptionalOwnershipUnchanged(entry, nameof(IMultiTenant.TenantId), entityTenantId, currentTenantId);
                    break;
            }
        }

        /// <summary>
        /// Mandatory modifications/deletes keep the persisted tenant. The host may administer a row
        /// that names an explicit tenant, but only while that ownership stays unchanged; a tenant
        /// context remains limited to its own rows.
        /// </summary>
        private static void EnsureRequiredOwnershipUnchanged(
            Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
            string propertyName,
            string? entityTenantId,
            string? currentTenantId)
        {
            var originalTenantId = Normalize(entry.Property(propertyName).OriginalValue as string);

            if (entityTenantId is null)
            {
                throw new InvalidOperationException(
                    "Cannot write IMustHaveTenant entity without a persisted tenant id.");
            }

            if (!string.Equals(entityTenantId, originalTenantId, StringComparison.Ordinal))
            {
                throw OwnershipChanged(originalTenantId, entityTenantId);
            }

            if (currentTenantId is not null &&
                !string.Equals(entityTenantId, currentTenantId, StringComparison.Ordinal))
            {
                throw TenantMismatch(entityTenantId, currentTenantId);
            }
        }

        /// <summary>
        /// Optional modifications/deletes must keep the persisted ownership exactly, including the
        /// shared (empty) state. The host writes only shared rows; a tenant writes only its own.
        /// </summary>
        private static void EnsureOptionalOwnershipUnchanged(
            Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
            string propertyName,
            string? entityTenantId,
            string? currentTenantId)
        {
            var originalTenantId = Normalize(entry.Property(propertyName).OriginalValue as string);

            if (!string.Equals(entityTenantId, originalTenantId, StringComparison.Ordinal))
            {
                throw OwnershipChanged(originalTenantId, entityTenantId);
            }

            if (!string.Equals(entityTenantId, currentTenantId, StringComparison.Ordinal))
            {
                throw TenantMismatch(entityTenantId, currentTenantId);
            }
        }

        private static string? Normalize(string? tenantId)
            => string.IsNullOrEmpty(tenantId) ? null : tenantId;

        private static InvalidOperationException OwnershipChanged(string? originalTenantId, string? entityTenantId)
            => new(
                $"Cannot change tenant ownership from '{originalTenantId ?? "<shared>"}' to '{entityTenantId ?? "<shared>"}'.");

        private static InvalidOperationException TenantMismatch(string? entityTenantId, string? currentTenantId)
            => new(
                $"Cannot write entity from tenant '{entityTenantId ?? "<shared>"}' while current tenant is '{currentTenantId ?? "<host>"}'");
    }
}
