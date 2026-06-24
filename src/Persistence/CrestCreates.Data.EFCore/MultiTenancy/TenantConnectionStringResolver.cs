using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Data.EFCore.MultiTenancy
{
    public class TenantConnectionStringResolver : ITenantConnectionStringResolver
    {
        private readonly ICurrentTenant _currentTenant;

        public TenantConnectionStringResolver(ICurrentTenant currentTenant)
        {
            _currentTenant = currentTenant;
        }

        public string Resolve()
        {
            if (_currentTenant?.Tenant == null)
            {
                throw new InvalidOperationException("No tenant is available in the current context.");
            }

            return _currentTenant.Tenant.ConnectionString
                ?? throw new InvalidOperationException($"Tenant '{_currentTenant.Tenant.Name}' has no connection string configured.");
        }
    }
}
