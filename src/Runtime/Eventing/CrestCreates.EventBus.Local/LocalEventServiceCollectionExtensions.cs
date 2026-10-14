using System;
using System.Linq;
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
        // Manual declarations are accumulated on a shared instance so an earlier parameterless
        // AddCrestLocalEventDispatch() call can never cause a later Declare<T>() to be dropped.
        var configuration = services
            .Where(descriptor => descriptor.ServiceType == typeof(LocalEventDispatchConfiguration))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<LocalEventDispatchConfiguration>()
            .FirstOrDefault();

        if (configuration is null)
        {
            configuration = new LocalEventDispatchConfiguration();
            services.AddSingleton(configuration);
        }

        if (configure is not null)
        {
            configuration.Add(configure);
        }

        services.TryAddSingleton<ILocalEventDispatchRegistry>(provider => LocalEventDispatchRegistry.Freeze(
            provider.GetServices<ILocalEventRegistrationContributor>(),
            provider.GetRequiredService<LocalEventDispatchConfiguration>().Callbacks,
            provider.GetServices<LocalEventJsonCodecRegistration>().ToList(),
            provider.GetService<IServiceProviderIsService>(),
            provider.GetService<ILoggerFactory>()?.CreateLogger("CrestCreates.EventBus.Local.LocalEventDispatchRegistry")));

        services.Configure<LocalDeadLetterOptions>(_ => { });
        services.TryAddSingleton<IDeadLetterStore, InMemoryDeadLetterStore>();

        // The dead-letter store can be scoped (EF Core). Recorder and executor therefore resolve it
        // from an explicit execution scope per operation and never capture it from the root provider.
        services.TryAddSingleton<ILocalEventFailureRecorder>(provider => new LocalEventDeadLetterRecorder(
            provider.GetService<IServiceScopeFactory>(),
            provider.GetService<IOptions<LocalDeadLetterOptions>>() ?? Options.Create(new LocalDeadLetterOptions()),
            provider.GetService<ILogger<LocalEventDeadLetterRecorder>>() ?? (ILogger)NullLogger.Instance));

        services.TryAddSingleton<ILocalDeadLetterReplayExecutor>(provider => new LocalDeadLetterReplayExecutor(
            provider.GetRequiredService<ILocalEventDispatchRegistry>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetService<ILogger<LocalDeadLetterReplayExecutor>>()));

        services.TryAddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.TryAddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
    }
}
