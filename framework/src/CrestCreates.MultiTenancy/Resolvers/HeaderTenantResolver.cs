using System;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.MultiTenancy.Resolvers
{
    /// <summary>
    /// 从 HTTP Header 中解析租户ID
    /// 默认从 "X-Tenant-Id" 或 "TenantId" header 中读取
    /// </summary>
    public class HeaderTenantResolver : ITenantResolver
    {
        private readonly MultiTenancyOptions _options;
        private readonly ILogger<HeaderTenantResolver> _logger;

        public HeaderTenantResolver(
            IOptions<MultiTenancyOptions> options,
            ILogger<HeaderTenantResolver> logger)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger;
        }

        public Task<string> ResolveAsync(HttpContext httpContext)
        {
            if (httpContext?.Request?.Headers == null)
            {
                return Task.FromResult<string>(null);
            }

            // 尝试从配置的 header 名称中读取
            var headerName = _options.TenantHeaderName ?? "X-Tenant-Id";
            
            if (httpContext.Request.Headers.TryGetValue(headerName, out var tenantId))
            {
                var value = tenantId.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _logger.LogDebug("Tenant resolved from header '{HeaderName}': {TenantId}", 
                        headerName, value);
                    return Task.FromResult(value);
                }
            }

            // 后备：尝试常见的 header 名称
            foreach (var fallbackHeader in new[] { "X-Tenant-Id", "TenantId", "Tenant" })
            {
                if (httpContext.Request.Headers.TryGetValue(fallbackHeader, out var fallbackTenantId))
                {
                    var value = fallbackTenantId.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        _logger.LogDebug("Tenant resolved from fallback header '{HeaderName}': {TenantId}", 
                            fallbackHeader, value);
                        return Task.FromResult(value);
                    }
                }
            }

            return Task.FromResult<string>(null);
        }
    }
}
