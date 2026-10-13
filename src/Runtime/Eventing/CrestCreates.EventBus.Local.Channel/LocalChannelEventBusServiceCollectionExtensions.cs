using System;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.EventBus.Local.Channel;

/// <summary>
/// Channel-flavored composition: the shared dispatch core plus the bounded channel queue,
/// background consumer and dead-letter background retry service.
/// </summary>
public static class LocalChannelEventBusServiceCollectionExtensions
{
    public static IServiceCollection AddCrestLocalChannelEventDispatch(
        this IServiceCollection services,
        Action<LocalEventDispatchRegistrationBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        LocalEventServiceCollectionExtensions.AddCrestLocalEventDispatchCore(services, configure);

        services.TryAddSingleton<LocalEventBusOptions>();
        services.TryAddSingleton<ChannelLocalEventQueue>();

        services.AddScoped<ILocalEventBus, BackgroundChannelLocalEventBus>();
        services.AddScoped<CrestCreates.EventBus.Abstract.IEventBus, BackgroundChannelLocalEventBus>();
        services.AddScoped<CrestCreates.Domain.DomainEvents.IDomainEventPublisher, DomainEventPublisher>();

        services.AddHostedService<BackgroundChannelLocalEventBusConsumer>();
        services.AddHostedService<LocalDeadLetterBackgroundService>();
        return services;
    }
}
