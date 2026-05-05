using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.MultiTenancy
{
    public class CurrentTenant : ICurrentTenant
    {
        private readonly AsyncLocal<TenantContextHolder> _currentTenant = new AsyncLocal<TenantContextHolder>();
        private readonly IServiceProvider _serviceProvider;

        public CurrentTenant(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public ITenantInfo Tenant => _currentTenant.Value?.Tenant;
        public string Id => Tenant?.Id;

        public Task<IDisposable> ChangeAsync(string tenantId)
        {
            var oldTenant = Tenant;
            ITenantInfo newTenant = null;

            if (!string.IsNullOrEmpty(tenantId))
            {
                var tenantProvider = _serviceProvider.GetRequiredService<ITenantProvider>();
                newTenant = tenantProvider.GetTenantAsync(tenantId).GetAwaiter().GetResult();
            }

            _currentTenant.Value = new TenantContextHolder { Tenant = newTenant };

            return Task.FromResult<IDisposable>(new DisposeAction(() =>
            {
                _currentTenant.Value = new TenantContextHolder { Tenant = oldTenant };
            }));
        }

        public IDisposable Change(ITenantInfo tenant)
        {
            var oldTenant = Tenant;
            _currentTenant.Value = new TenantContextHolder { Tenant = tenant };

            return new DisposeAction(() =>
            {
                _currentTenant.Value = new TenantContextHolder { Tenant = oldTenant };
            });
        }

        public void SetTenantId(string tenantId)
        {
            if (string.IsNullOrEmpty(tenantId))
            {
                _currentTenant.Value = new TenantContextHolder { Tenant = null };
                return;
            }

            var tenantProvider = _serviceProvider.GetRequiredService<ITenantProvider>();
            var tenant = tenantProvider.GetTenantAsync(tenantId).GetAwaiter().GetResult();
            _currentTenant.Value = new TenantContextHolder { Tenant = tenant };
        }
        
        private class TenantContextHolder
        {
            public ITenantInfo Tenant { get; set; }
        }
        
        private class DisposeAction : IDisposable
        {
            private readonly Action _action;

            public DisposeAction(Action action)
            {
                _action = action;
            }

            public void Dispose()
            {
                _action();
            }
        }
    }
}
