using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.Logging;

namespace CrestCreates.MultiTenancy.Providers
{
    /// <summary>
    /// 基于内存的租户提供者
    /// 适用于开发和测试环境
    /// </summary>
    public class InMemoryTenantProvider : ITenantProvider
    {
        private readonly ConcurrentDictionary<string, ITenantInfo> _tenants;
        private readonly ILogger<InMemoryTenantProvider> _logger;

        public InMemoryTenantProvider(ILogger<InMemoryTenantProvider> logger)
        {
            _tenants = new ConcurrentDictionary<string, ITenantInfo>(StringComparer.OrdinalIgnoreCase);
            _logger = logger;
        }

        public Task<ITenantInfo> GetTenantAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(tenantId))
            {
                return Task.FromResult<ITenantInfo>(null);
            }

            if (_tenants.TryGetValue(tenantId, out var tenant))
            {
                _logger.LogDebug("Tenant found: {TenantId}", tenantId);
                return Task.FromResult(tenant);
            }

            _logger.LogWarning("Tenant not found: {TenantId}", tenantId);
            return Task.FromResult<ITenantInfo>(null);
        }

        /// <summary>
        /// 添加租户
        /// </summary>
        public void AddTenant(ITenantInfo tenant)
        {
            if (tenant == null) throw new ArgumentNullException(nameof(tenant));
            if (string.IsNullOrEmpty(tenant.Id)) throw new ArgumentException("Tenant ID cannot be empty", nameof(tenant));

            _tenants[tenant.Id] = tenant;
            _logger.LogInformation("Tenant added: {TenantId} - {TenantName}", tenant.Id, tenant.Name);
        }

        /// <summary>
        /// 批量添加租户
        /// </summary>
        public void AddTenants(IEnumerable<ITenantInfo> tenants)
        {
            if (tenants == null) throw new ArgumentNullException(nameof(tenants));

            foreach (var tenant in tenants)
            {
                AddTenant(tenant);
            }
        }

        /// <summary>
        /// 移除租户
        /// </summary>
        public bool RemoveTenant(string tenantId)
        {
            if (_tenants.TryRemove(tenantId, out _))
            {
                _logger.LogInformation("Tenant removed: {TenantId}", tenantId);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 清空所有租户
        /// </summary>
        public void Clear()
        {
            _tenants.Clear();
            _logger.LogInformation("All tenants cleared");
        }

        /// <summary>
        /// 获取所有租户
        /// </summary>
        public IEnumerable<ITenantInfo> GetAllTenants()
        {
            return _tenants.Values;
        }
    }
}
