using System;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestCreates.EventBus.Local;

/// <summary>
/// Composition entry for typed local event dispatch. Assembly-scoped registrations are activated
/// only through explicitly called generated <c>AddLocalEvents()</c> extensions (or manual
/// declarations here); there is no assembly scanning fallback.
/// </summary>
public static class LocalEventServiceCollectionExtensions
{
    /// <summary>Synchronous dispatch flavor (the default local event bus).</summary>
    public static IServiceCollection AddCrestLocalEventDispatch(
        this IServiceCollection services,
        Action<LocalEventDispatchRegistrationBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        AddCrestLocalEventDispatchCore(services, configure);

        services.TryAddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.TryAddScoped<CrestCreates.EventBus.Abstract.IEventBus, DefaultLocalEventBus>();
        services.TryAddScoped<IDomainEventPublisher, DomainEventPublisher>();
        return services;
    }

    internal static void AddCrestLocalEventDispatchCore(
        IServiceCollection services,
        Action<LocalEventDispatchRegistrationBuilder>? configure)
    {
        services.TryAddSingleton<ILocalEventDispatchRegistry>(provider => LocalEventDispatchRegistry.Freeze(
            provider.GetServices<ILocalEventRegistrationContributor>(),
            configure));

        services.Configure<LocalDeadLetterOptions>(_ => { });
        services.TryAddSingleton<IDeadLetterStore, InMemoryDeadLetterStore>();

        services.TryAddSingleton<ILocalEventFailureRecorder>(provider => new LocalEventDeadLetterRecorder(
            provider.GetService<IDeadLetterStore>(),
            provider.GetService<IEventRegistry>(),
            provider.GetService<IOptions<LocalDeadLetterOptions>>() ?? Options.Create(new LocalDeadLetterOptions()),
            provider.GetService<ILogger<LocalEventDeadLetterRecorder>>() ?? (ILogger)NullLogger.Instance));

        services.TryAddSingleton<ILocalDeadLetterReplayExecutor>(provider => new LocalDeadLetterReplayExecutor(
            provider.GetRequiredService<IDeadLetterStore>(),
            provider.GetRequiredService<ILocalEventDispatchRegistry>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetService<ILogger<LocalDeadLetterReplayExecutor>>()));

        services.TryAddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.TryAddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
    }
}
