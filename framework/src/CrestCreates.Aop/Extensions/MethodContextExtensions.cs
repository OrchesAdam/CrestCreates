using System;
using Microsoft.Extensions.DependencyInjection;
using Rougamo.Context;

namespace CrestCreates.Aop.Extensions;

public static class MethodContextExtensions
{
    public static IServiceProvider? GetServiceProvider(this MethodContext context)
    {
        if (context.Target == null) return null;

        var target = context.Target;
        var serviceProviderProperty = target.GetType().GetProperty("ServiceProvider");
        if (serviceProviderProperty != null)
        {
            return serviceProviderProperty.GetValue(target) as IServiceProvider;
        }

        var servicesProperty = target.GetType().GetProperty("Services");
        if (servicesProperty != null)
        {
            return servicesProperty.GetValue(target) as IServiceProvider;
        }

        var httpContextAccessorProperty = target.GetType().GetProperty("HttpContextAccessor");
        if (httpContextAccessorProperty != null)
        {
            var httpContextAccessor = httpContextAccessorProperty.GetValue(target);
            if (httpContextAccessor != null)
            {
                var httpContextProperty = httpContextAccessor.GetType().GetProperty("HttpContext");
                var httpContext = httpContextProperty?.GetValue(httpContextAccessor);
                if (httpContext != null)
                {
                    var requestServicesProperty = httpContext.GetType().GetProperty("RequestServices");
                    return requestServicesProperty?.GetValue(httpContext) as IServiceProvider;
                }
            }
        }

        return null;
    }

    public static T? GetService<T>(this MethodContext context) where T : class
    {
        var serviceProvider = context.GetServiceProvider();
        return serviceProvider?.GetService<T>();
    }

    public static object? GetService(this MethodContext context, Type serviceType)
    {
        var serviceProvider = context.GetServiceProvider();
        return serviceProvider?.GetService(serviceType);
    }
}
