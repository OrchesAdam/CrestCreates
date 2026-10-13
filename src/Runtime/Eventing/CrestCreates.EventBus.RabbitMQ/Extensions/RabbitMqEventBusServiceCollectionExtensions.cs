using System;
using System.Linq;
using System.Text.Json.Serialization;
using CrestCreates.Event;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.RabbitMQ.Composition;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Consuming;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Publishing;
using CrestCreates.EventBus.RabbitMQ.Registration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrestCreates.EventBus.RabbitMQ.Extensions;

/// <summary>
/// Single assembly entry for the RabbitMQ event bus. Modules, configuration-section hosts and legacy
/// overloads all forward here; there is exactly one lifetime/JSON configuration path. Composition
/// ordering (event kernel build → binding freeze → topology → consume) is enforced by the composition
/// service plus the consumer readiness gate, not by hosted-service registration order.
/// </summary>
public static class RabbitMqEventBusServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMqEventBus(
        this IServiceCollection services,
        Action<RabbitMqOptions>? configure = null,
        Action<RabbitMqEventBusBuilder>? builder = null)
    {
        if (configure is not null)
        {
            var snapshot = new RabbitMqOptions();
            configure(snapshot);
            snapshot.Validate();
        }

        return AddRabbitMqEventBusCore(
            services,
            configure is null ? null : collection => collection.Configure(configure),
            builder,
            contextFactory: null);
    }

    public static IServiceCollection AddRabbitMqEventBus(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<RabbitMqEventBusBuilder>? builder = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Eager bind + validate for fast, readable feedback; the runtime values still come from the
        // options pipeline and are validated again by the connection pool and composition service.
        var snapshot = new RabbitMqOptions();
        configuration.Bind(snapshot);
        snapshot.Validate();

        return AddRabbitMqEventBusCore(
            services,
            collection => collection.Configure<RabbitMqOptions>(configuration),
            builder,
            contextFactory: null);
    }

    /// <summary>
    /// Legacy typed-context overload: the context participates in the same freeze as an explicit typed
    /// codec source, validated per binding. It is a transition adapter; prefer the [CrestEvent] +
    /// JsonContracts toolchain pipeline.
    /// </summary>
    public static IServiceCollection AddRabbitMqEventBus<TContext>(
        this IServiceCollection services,
        Action<RabbitMqOptions>? configure = null,
        Action<RabbitMqEventBusBuilder>? builder = null)
        where TContext : JsonSerializerContext, new()
        => AddRabbitMqEventBusCore(
            services,
            configure is null ? null : collection => collection.Configure(configure),
            builder,
            static () => new TContext());

    /// <summary>Legacy context-instance overload (see the typed-context overload for the migration note).</summary>
    public static IServiceCollection AddRabbitMqEventBus(
        this IServiceCollection services,
        JsonSerializerContext jsonContext,
        Action<RabbitMqOptions>? configure = null,
        Action<RabbitMqEventBusBuilder>? builder = null)
    {
        ArgumentNullException.ThrowIfNull(jsonContext);
        return AddRabbitMqEventBusCore(
            services,
            configure is null ? null : collection => collection.Configure(configure),
            builder,
            () => jsonContext);
    }

    private static IServiceCollection AddRabbitMqEventBusCore(
        IServiceCollection services,
        Action<IServiceCollection>? applyOptions,
        Action<RabbitMqEventBusBuilder>? builder,
        Func<JsonSerializerContext>? contextFactory)
    {
        ArgumentNullException.ThrowIfNull(services);

        applyOptions?.Invoke(services);

        // Existing event kernel mainline (TryAdd-based): registry, validators, resolver, publish-time
        // validator — no hidden constructor DI gaps.
        services.AddEventKernel();

        var configuration = services
            .Where(descriptor => descriptor.ServiceType == typeof(RabbitMqCompositionConfiguration))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<RabbitMqCompositionConfiguration>()
            .FirstOrDefault();

        if (configuration is null)
        {
            configuration = new RabbitMqCompositionConfiguration();
            services.AddSingleton(configuration);
        }

        if (contextFactory is not null)
        {
            configuration.ContextCodecs.Add(contextFactory());
        }

        builder?.Invoke(new RabbitMqEventBusBuilder(configuration));

        services.TryAddSingleton<RabbitMqConnectionPool>();
        services.TryAddSingleton<RabbitMqPublisher>();
        services.TryAddSingleton<RabbitMqEventBus>();
        services.TryAddSingleton<IEventBus>(provider => provider.GetRequiredService<RabbitMqEventBus>());
        services.TryAddSingleton<RabbitMqCompositionReadiness>();

        services.TryAddSingleton<IRabbitMqBindingRegistry>(provider => RabbitMqBindingFreeze.Freeze(
            new RabbitMqFreezeInput(
                provider.GetRequiredService<EventRegistry>(),
                provider.GetServices<IRabbitMqRegistrationContributor>().ToList(),
                provider.GetRequiredService<RabbitMqCompositionConfiguration>(),
                provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value)));

        // Registration order puts the composition service first; the consumer additionally awaits the
        // readiness gate, so startup sequencing holds even if a host reorders IHostedService entries.
        services.AddHostedService<RabbitMqCompositionHostedService>();
        services.AddHostedService<RabbitMqConsumer>();

        return services;
    }
}
