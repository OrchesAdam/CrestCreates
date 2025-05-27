using System.Reflection;
using CrestCreates.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.WebAppExtensions;

public static class WebApplicationBuilderExtensions
{
    /// <summary>
    /// 自动注册当前程序集中标记的服务
    /// </summary>
    public static IHostApplicationBuilder AddAutoServices(this IHostApplicationBuilder builder)
    {
        builder.Services.AddServicesFromCallingAssembly();
        return builder;
    }

    /// <summary>
    /// 自动注册入口程序集中标记的服务
    /// </summary>
    public static IHostApplicationBuilder AddAutoServicesFromEntry(this IHostApplicationBuilder builder)
    {
        builder.Services.AddServicesFromEntryAssembly();
        return builder;
    }

    /// <summary>
    /// 自动注册指定程序集中标记的服务
    /// </summary>
    public static IHostApplicationBuilder AddAutoServicesFromAssembly(this IHostApplicationBuilder builder, Assembly assembly)
    {
        builder.Services.AddServicesFromAssembly(assembly);
        return builder;
    }

    /// <summary>
    /// 自动注册包含指定类型的程序集中标记的服务
    /// </summary>
    public static IHostApplicationBuilder AddAutoServicesFromAssemblyContaining<T>(this IHostApplicationBuilder builder)
    {
        builder.Services.AddServicesFromAssemblyContaining<T>();
        return builder;
    }

    /// <summary>
    /// 自动注册多个程序集中标记的服务
    /// </summary>
    public static IHostApplicationBuilder AddAutoServicesFromAssemblies(this IHostApplicationBuilder builder, params Assembly[] assemblies)
    {
        builder.Services.AddServicesFromAssemblies(assemblies);
        return builder;
    }
}