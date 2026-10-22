using System;
using System.Threading.Tasks;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Data.EFCore.MultiTenancy
{
    /// <summary>
    /// Host-side tenant context used only to apply generated model filters when no scoped tenant
    /// object exists. Its Id is null, so required-tenant filters see no rows and optional filters
    /// are restricted to shared null rows.
    /// </summary>
    internal sealed class HostCurrentTenant : ICurrentTenant
    {
        public static readonly HostCurrentTenant Instance = new();

        private HostCurrentTenant()
        {
        }

        public ITenantInfo? Tenant => null;

        public string? Id => null;

        public Task<IDisposable> ChangeAsync(string tenantId) => Task.FromResult<IDisposable>(NoopDisposable.Instance);

        public IDisposable Change(ITenantInfo? tenant) => NoopDisposable.Instance;

        public void SetTenantId(string tenantId)
        {
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
