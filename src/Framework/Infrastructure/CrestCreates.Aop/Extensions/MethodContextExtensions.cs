using System;
using CrestCreates.Aop.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Rougamo.Context;

namespace CrestCreates.Aop.Extensions;

/// <summary>
/// Interceptor access to the woven target's services. Resolution is by contract
/// (<see cref="IServiceProviderAccessor"/>), never by reflecting over the target type:
/// a woven class opts into the intercept chain by implementing the accessor.
/// </summary>
public static class MethodContextExtensions
{
    public static IServiceProvider? GetServiceProvider(this MethodContext context)
    {
        return (context.Target as IServiceProviderAccessor)?.ServiceProvider;
    }

    public static T? GetService<T>(this MethodContext context) where T : class
    {
        return context.GetServiceProvider()?.GetService<T>();
    }

    public static object? GetService(this MethodContext context, Type serviceType)
    {
        return context.GetServiceProvider()?.GetService(serviceType);
    }
}
