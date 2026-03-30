using System;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CrestCreates.MultiTenancy.Middleware
{
    /// <summary>
    /// 多租户识别中间件
    /// 从 HTTP 请求中提取租户标识并设置当前租户上下文
    /// </summary>
    public class MultiTenancyMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<MultiTenancyMiddleware> _logger;

        public MultiTenancyMiddleware(
            RequestDelegate next,
            ILogger<MultiTenancyMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(
            HttpContext context,
            ICurrentTenant currentTenant,
            ITenantResolver tenantResolver)
        {
            var tenantId = await tenantResolver.ResolveAsync(context);

            if (!string.IsNullOrEmpty(tenantId))
            {
                _logger.LogDebug("Tenant resolved: {TenantId}", tenantId);
                
                using (currentTenant.Change(tenantId))
                {
                    await _next(context);
                }
            }
            else
            {
                _logger.LogDebug("No tenant resolved, continuing without tenant context");
                await _next(context);
            }
        }
    }

    /// <summary>
    /// 复合租户解析器
    /// 支持多种解析策略,按优先级依次尝试
    /// </summary>
    public class CompositeTenantResolver : ITenantResolver
    {
        private readonly ITenantResolver[] _resolvers;
        private readonly ILogger<CompositeTenantResolver> _logger;

        public CompositeTenantResolver(
            ITenantResolver[] resolvers,
            ILogger<CompositeTenantResolver> logger)
        {
            _resolvers = resolvers ?? throw new ArgumentNullException(nameof(resolvers));
            _logger = logger;
        }

        public async Task<string> ResolveAsync(HttpContext httpContext)
        {
            foreach (var resolver in _resolvers)
            {
                var tenantId = await resolver.ResolveAsync(httpContext);
                if (!string.IsNullOrEmpty(tenantId))
                {
                    _logger.LogDebug("Tenant resolved by {ResolverType}: {TenantId}", 
                        resolver.GetType().Name, tenantId);
                    return tenantId;
                }
            }

            return null;
        }
    }
}
