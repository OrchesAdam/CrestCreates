using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CrestCreates.MultiTenancy.Middleware
{
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
            var resolutionResult = await tenantResolver.ResolveAsync(context);

            if (resolutionResult.IsResolved && !string.IsNullOrEmpty(resolutionResult.TenantId))
            {
                _logger.LogDebug("Tenant resolved: {TenantId} by {ResolvedBy}", resolutionResult.TenantId, resolutionResult.ResolvedBy);

                using (await currentTenant.ChangeAsync(resolutionResult.TenantId))
                {
                    if (currentTenant.Tenant == null)
                    {
                        _logger.LogWarning("Tenant is unavailable: {TenantId}", resolutionResult.TenantId);
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(new
                        {
                            Code = StatusCodes.Status403Forbidden,
                            Message = "租户不存在或已停用",
                            TraceId = context.TraceIdentifier
                        }));
                        return;
                    }

                    await _next(context);
                }
            }
            else if (resolutionResult.Error != null)
            {
                _logger.LogWarning("Tenant resolution failed: {ErrorCode} - {ErrorMessage}", resolutionResult.Error.Code, resolutionResult.Error.Message);

                if (resolutionResult.Error.Code == "TENANT_INACTIVE")
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        Code = StatusCodes.Status403Forbidden,
                        Message = "租户已停用",
                        TraceId = context.TraceIdentifier
                    }));
                    return;
                }

                await _next(context);
            }
            else
            {
                _logger.LogDebug("No tenant resolved, continuing without tenant context");
                await _next(context);
            }
        }
    }

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

        public async Task<TenantResolutionResult> ResolveAsync(HttpContext httpContext)
        {
            foreach (var resolver in _resolvers)
            {
                var result = await resolver.ResolveAsync(httpContext);
                if (result.IsResolved)
                {
                    _logger.LogDebug("Tenant resolved by {ResolverType}: {TenantId}",
                        resolver.GetType().Name, result.TenantId);
                    return result;
                }
            }

            return TenantResolutionResult.NotResolved("Composite");
        }
    }

    public class TenantBoundaryMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<TenantBoundaryMiddleware> _logger;

        public TenantBoundaryMiddleware(
            RequestDelegate next,
            ILogger<TenantBoundaryMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(
            HttpContext context,
            ICurrentTenant currentTenant,
            ICurrentUser currentUser)
        {
            if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentTenant.Id))
            {
                await _next(context);
                return;
            }

            if (currentUser.IsSuperAdmin)
            {
                await _next(context);
                return;
            }

            if (string.IsNullOrWhiteSpace(currentUser.TenantId) ||
                !string.Equals(currentUser.TenantId, currentTenant.Id, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Tenant boundary violation. User tenant: {UserTenantId}, current tenant: {CurrentTenantId}",
                    currentUser.TenantId,
                    currentTenant.Id);

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    Code = StatusCodes.Status403Forbidden,
                    Message = "当前用户无权访问该租户上下文",
                    TraceId = context.TraceIdentifier
                }));
                return;
            }

            await _next(context);
        }
    }
}
