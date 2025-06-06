using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Domain.MultiTenancy;

namespace CrestCreates.Infrastructure.MultiTenancy
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
        
        public IDisposable Change(string tenantId)
        {
            var oldTenant = Tenant;
            ITenantInfo newTenant = null;
            
            if (!string.IsNullOrEmpty(tenantId))
            {
                var tenantProvider = _serviceProvider.GetRequiredService<ITenantProvider>();
                newTenant = tenantProvider.GetTenantAsync(tenantId).GetAwaiter().GetResult();
            }
            
            _currentTenant.Value = new TenantContextHolder { Tenant = newTenant };

            return new DisposeAction(() =>
            {
                _currentTenant.Value = new TenantContextHolder { Tenant = oldTenant };
            });
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

    public interface ICurrentTenant
    {
        ITenantInfo Tenant { get; }
        string Id { get; }
        IDisposable Change(string tenantId);
    }
}
