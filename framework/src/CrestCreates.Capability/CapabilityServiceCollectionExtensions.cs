using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Capability;

public static class CapabilityServiceCollectionExtensions
{
    public static IServiceCollection AddCapabilityPipeline(
        this IServiceCollection services,
        Action<CapabilityPipelineBuilder>? configure = null)
    {
        var builder = new CapabilityPipelineBuilder();

        builder.Use<AuthorizationMiddleware>();
        builder.Use<ValidationMiddleware>();
        builder.Use<IdempotencyMiddleware>();
        builder.Use<EventPublishingMiddleware>();

        configure?.Invoke(builder);

        services.TryAddSingleton(builder);
        services.TryAddSingleton<CapabilityHandlerResolver>();
        services.TryAddSingleton<ICapabilityHandlerResolver>(sp => sp.GetRequiredService<CapabilityHandlerResolver>());
        services.TryAddSingleton<ICapabilityPipeline, CapabilityPipeline>();
        services.TryAddTransient<AuthorizationMiddleware>();
        services.TryAddTransient<ValidationMiddleware>();
        services.TryAddTransient<IdempotencyMiddleware>();
        services.TryAddTransient<EventPublishingMiddleware>();

        return services;
    }

    public static IServiceCollection AddCapabilityHandler<THandler>(
        this IServiceCollection services,
        string capabilityName)
        where THandler : class, ICapabilityHandler
    {
        services.TryAddTransient<THandler>();
        services.AddTransient<ICapabilityHandlerInvoker>(sp =>
        {
            var handler = sp.GetRequiredService<THandler>();
            var handlerType = typeof(THandler);

            var handlerInterface = handlerType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType
                    && i.GetGenericTypeDefinition() == typeof(ICapabilityHandler<,>));

            if (handlerInterface != null)
            {
                var method = handlerInterface.GetMethod("ExecuteAsync")!;
                return new TypedHandlerInvoker(handler, method);
            }

            throw new InvalidOperationException(
                $"Handler type '{handlerType.Name}' must implement ICapabilityHandler<TInput, TOutput>.");
        });

        return services;
    }
}

internal sealed class TypedHandlerInvoker : ICapabilityHandlerInvoker
{
    private readonly object _handler;
    private readonly System.Reflection.MethodInfo _method;

    public TypedHandlerInvoker(object handler, System.Reflection.MethodInfo method)
    {
        _handler = handler;
        _method = method;
    }

    public async Task<object?> InvokeAsync(object? input, CancellationToken ct)
    {
        var task = (Task)_method.Invoke(_handler, new[] { input, ct })!;
        await task.ConfigureAwait(false);

        var resultProp = task.GetType().GetProperty("Result");
        return resultProp?.GetValue(task);
    }
}
