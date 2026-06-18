using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CrestCreates.MultiTenancy.Providers
{
    /// <summary>
    /// 基于配置文件的租户提供者
    /// 从 appsettings.json 或其他配置源读取租户信息
    /// </summary>
    public class ConfigurationTenantProvider : ITenantProvider
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<ConfigurationTenantProvider> _logger;

        public ConfigurationTenantProvider(
            IConfiguration configuration,
            ILogger<ConfigurationTenantProvider> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger;
        }

        public Task<ITenantInfo> GetTenantAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(tenantId))
            {
                return Task.FromResult<ITenantInfo>(null);
            }

            // 从配置中读取租户信息
            // 配置格式示例:
            // {
            //   "Tenants": [
            //     {
            //       "Id": "tenant1",
            //       "Name": "Tenant 1",
            //       "ConnectionString": "Server=...;Database=Tenant1Db;..."
            //     }
            //   ]
            // }

            var tenantsSection = _configuration.GetSection("Tenants");
            var tenants = tenantsSection.Get<TenantConfiguration[]>();

            if (tenants != null)
            {
                foreach (var tenantConfig in tenants)
                {
                    if (string.Equals(tenantConfig.Id, tenantId, StringComparison.OrdinalIgnoreCase))
                    {
                        var tenant = new TenantInfo(
                            tenantConfig.Id,
                            tenantConfig.Name,
                            tenantConfig.ConnectionString);

                        _logger.LogDebug("Tenant found in configuration: {TenantId}", tenantId);
                        return Task.FromResult<ITenantInfo>(tenant);
                    }
                }
            }

            _logger.LogWarning("Tenant not found in configuration: {TenantId}", tenantId);
            return Task.FromResult<ITenantInfo>(null);
        }

        private class TenantConfiguration
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string ConnectionString { get; set; }
        }
    }
}
