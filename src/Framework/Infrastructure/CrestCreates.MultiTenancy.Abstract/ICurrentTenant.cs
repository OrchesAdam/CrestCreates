using System;

namespace CrestCreates.MultiTenancy.Abstract
{
    public interface ICurrentTenant
    {
        ITenantInfo Tenant { get; }

        string Id { get; }

        /// <summary>
        /// Switch to a tenant resolved from the database. Used at request entry
        /// (middleware) where the tenant must be validated as existing and active.
        /// </summary>
        Task<IDisposable> ChangeAsync(string tenantId);

        /// <summary>
        /// Switch to an already-resolved tenant without querying the database.
        /// Used in internal flows (tenant initialization) where the caller
        /// already holds a valid <see cref="ITenantInfo"/> and the tenant may
        /// not yet be visible to other database scopes.
        /// </summary>
        IDisposable Change(ITenantInfo tenant);

        void SetTenantId(string tenantId);
    }
}
