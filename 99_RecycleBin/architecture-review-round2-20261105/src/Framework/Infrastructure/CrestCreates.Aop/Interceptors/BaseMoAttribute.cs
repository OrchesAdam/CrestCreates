using System;
using CrestCreates.Aop.Abstractions;
using CrestCreates.Aop.Abstractions.Interfaces;
using CrestCreates.Aop.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rougamo;
using Rougamo.Context;

namespace CrestCreates.Aop.Interceptors;

public abstract class BaseMoAttribute : MoAttribute, IInterceptorOrder
{
    public virtual int Order => InterceptorOrders.Audit;

    protected IServiceProvider? GetServiceProvider(MethodContext context)
    {
        return context.GetServiceProvider();
    }

    protected T? GetService<T>(MethodContext context) where T : class
    {
        return context.GetServiceProvider()?.GetService<T>();
    }

    protected T? GetService<T>(IServiceProvider? serviceProvider) where T : class
    {
        return serviceProvider?.GetService<T>();
    }

    protected T GetRequiredService<T>(MethodContext context) where T : class
    {
        var service = context.GetServiceProvider()?.GetService<T>();
        return service ?? throw new InvalidOperationException($"服务 {typeof(T).Name} 未注册");
    }

    protected T GetRequiredService<T>(IServiceProvider? serviceProvider) where T : class
    {
        var service = serviceProvider?.GetService<T>();
        return service ?? throw new InvalidOperationException($"服务 {typeof(T).Name} 未注册");
    }

    protected TOptions? GetOptions<TOptions>(MethodContext context) where TOptions : class
    {
        var options = GetService<IOptions<TOptions>>(context);
        return options?.Value;
    }
}
