using System;
using System.Threading.Tasks;
using CrestCreates.Aop.Abstractions;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rougamo;
using Rougamo.Context;

namespace CrestCreates.MultiTenancy.Interceptors;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class MultiTenantMoAttribute : AsyncMoAttribute
{
    public int Order => InterceptorOrders.MultiTenant;

    public override async ValueTask OnEntryAsync(MethodContext context)
    {
        try
        {
            var currentUser = context.GetService<ICurrentUser>();
            var currentTenant = context.GetService<ICurrentTenant>();
            var httpContext = context.GetService<IHttpContextAccessor>()?.HttpContext;
            var tenantResolver = context.GetService<ITenantResolver>();

            if (currentTenant != null)
            {
                string? tenantId = null;

                if (!string.IsNullOrEmpty(currentUser?.TenantId))
                {
                    tenantId = currentUser.TenantId;
                }
                else if (httpContext != null && tenantResolver != null)
                {
                    var result = await tenantResolver.ResolveAsync(httpContext);
                    if (result.IsResolved)
                    {
                        tenantId = result.TenantId;
                    }
                }

                if (!string.IsNullOrEmpty(tenantId))
                {
                    currentTenant.SetTenantId(tenantId);
                }
            }
        }
        catch (Exception exception)
        {
            var logger = context.GetService<ILogger<MultiTenantMoAttribute>>();
            logger?.LogWarning(exception, "多租户处理失败");
        }
    }

    public override ValueTask OnSuccessAsync(MethodContext context)
    {
        return ValueTask.CompletedTask;
    }

    public override ValueTask OnExceptionAsync(MethodContext context)
    {
        return ValueTask.CompletedTask;
    }
}
