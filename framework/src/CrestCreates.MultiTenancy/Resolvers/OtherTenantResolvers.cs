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
    /// 从查询字符串中解析租户ID
    /// 例如: https://example.com/api/products?tenantId=tenant1
    /// </summary>
    public class QueryStringTenantResolver : ITenantResolver
    {
        private readonly MultiTenancyOptions _options;
        private readonly ILogger<QueryStringTenantResolver> _logger;

        public QueryStringTenantResolver(
            IOptions<MultiTenancyOptions> options,
            ILogger<QueryStringTenantResolver> logger)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger;
        }

        public Task<string> ResolveAsync(HttpContext httpContext)
        {
            if (httpContext?.Request?.Query == null)
            {
                return Task.FromResult<string>(null);
            }

            // 从配置的查询参数名称中读取
            var queryParamName = _options.TenantQueryStringKey ?? "tenantId";
            
            if (httpContext.Request.Query.TryGetValue(queryParamName, out var tenantId))
            {
                var value = tenantId.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _logger.LogDebug("Tenant resolved from query string '{ParamName}': {TenantId}", 
                        queryParamName, value);
                    return Task.FromResult(value);
                }
            }

            return Task.FromResult<string>(null);
        }
    }

    /// <summary>
    /// 从 Cookie 中解析租户ID
    /// </summary>
    public class CookieTenantResolver : ITenantResolver
    {
        private readonly MultiTenancyOptions _options;
        private readonly ILogger<CookieTenantResolver> _logger;

        public CookieTenantResolver(
            IOptions<MultiTenancyOptions> options,
            ILogger<CookieTenantResolver> logger)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger;
        }

        public Task<string> ResolveAsync(HttpContext httpContext)
        {
            if (httpContext?.Request?.Cookies == null)
            {
                return Task.FromResult<string>(null);
            }

            // 从配置的 cookie 名称中读取
            var cookieName = _options.TenantCookieName ?? "TenantId";
            
            if (httpContext.Request.Cookies.TryGetValue(cookieName, out var tenantId))
            {
                if (!string.IsNullOrWhiteSpace(tenantId))
                {
                    _logger.LogDebug("Tenant resolved from cookie '{CookieName}': {TenantId}", 
                        cookieName, tenantId);
                    return Task.FromResult(tenantId);
                }
            }

            return Task.FromResult<string>(null);
        }
    }

    /// <summary>
    /// 从路由数据中解析租户ID
    /// 例如: /api/{tenantId}/products
    /// </summary>
    public class RouteTenantResolver : ITenantResolver
    {
        private readonly MultiTenancyOptions _options;
        private readonly ILogger<RouteTenantResolver> _logger;

        public RouteTenantResolver(
            IOptions<MultiTenancyOptions> options,
            ILogger<RouteTenantResolver> logger)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger;
        }

        public Task<string> ResolveAsync(HttpContext httpContext)
        {
            if (httpContext?.Request?.Path == null)
            {
                return Task.FromResult<string>(null);
            }

            // 从路由值中读取租户ID
            var routeKey = _options.TenantRouteKey ?? "tenantId";
            
            if (httpContext.Request.Query.TryGetValue(routeKey, out var tenantId))
            {
                var value = tenantId.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _logger.LogDebug("Tenant resolved from route data '{RouteKey}': {TenantId}", 
                        routeKey, value);
                    return Task.FromResult(value);
                }
            }

            return Task.FromResult<string>(null);
        }
    }
}
