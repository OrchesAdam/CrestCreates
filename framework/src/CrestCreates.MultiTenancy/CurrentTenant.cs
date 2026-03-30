using System;
using System.Threading;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.MultiTenancy
{
    /// <summary>
    /// 当前租户上下文实现
    /// 使用 AsyncLocal 存储当前租户信息,支持异步流程
    /// </summary>
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
}
