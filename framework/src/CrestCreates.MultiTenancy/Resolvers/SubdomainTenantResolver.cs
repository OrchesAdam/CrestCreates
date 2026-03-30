using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.MultiTenancy.Resolvers
{
    /// <summary>
    /// 从子域名中解析租户ID
    /// 例如: tenant1.example.com -> tenant1
    /// </summary>
    public class SubdomainTenantResolver : ITenantResolver
    {
        private readonly MultiTenancyOptions _options;
        private readonly ILogger<SubdomainTenantResolver> _logger;

        public SubdomainTenantResolver(
            IOptions<MultiTenancyOptions> options,
            ILogger<SubdomainTenantResolver> logger)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger;
        }

        public Task<string> ResolveAsync(HttpContext httpContext)
        {
            if (httpContext?.Request?.Host == null)
            {
                return Task.FromResult<string>(null);
            }

            var host = httpContext.Request.Host.Host;
            
            if (string.IsNullOrEmpty(host))
            {
                return Task.FromResult<string>(null);
            }

            // 如果配置了根域名，从主机名中提取子域名
            if (!string.IsNullOrEmpty(_options.RootDomain))
            {
                var tenantId = ExtractSubdomain(host, _options.RootDomain);
                
                if (!string.IsNullOrEmpty(tenantId))
                {
                    _logger.LogDebug("Tenant resolved from subdomain: {TenantId} (Host: {Host})", 
                        tenantId, host);
                    return Task.FromResult(tenantId);
                }
            }

            // 如果没有配置根域名，尝试提取第一个子域名部分
            var parts = host.Split('.');
            if (parts.Length >= 3) // 例如: tenant.example.com
            {
                var subdomain = parts[0];
                
                // 排除常见的非租户子域名
                if (!IsReservedSubdomain(subdomain))
                {
                    _logger.LogDebug("Tenant resolved from first subdomain part: {TenantId} (Host: {Host})", 
                        subdomain, host);
                    return Task.FromResult(subdomain);
                }
            }

            return Task.FromResult<string>(null);
        }

        private string ExtractSubdomain(string host, string rootDomain)
        {
            if (host.EndsWith(rootDomain, StringComparison.OrdinalIgnoreCase))
            {
                var subdomain = host.Substring(0, host.Length - rootDomain.Length).TrimEnd('.');
                
                // 如果子域名为空或包含多个部分，返回第一个部分
                if (!string.IsNullOrEmpty(subdomain))
                {
                    var parts = subdomain.Split('.');
                    return parts[0];
                }
            }

            return null;
        }

        private bool IsReservedSubdomain(string subdomain)
        {
            // 保留的子域名列表（不作为租户ID）
            var reserved = new[]
            {
                "www", "api", "admin", "app", "cdn", "static",
                "mail", "smtp", "ftp", "dev", "test", "staging"
            };

            return reserved.Contains(subdomain, StringComparer.OrdinalIgnoreCase);
        }
    }
}
