using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Infrastructure.EventBus;
using CrestCreates.Infrastructure.EventBus.Local;
using CrestCreates.Infrastructure.EventBus.Distributed;
using CrestCreates.Infrastructure.EventBus.EventStore;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class EventBusExtensions
    {
        public static IServiceCollection AddEventBus(this IServiceCollection services)
        {
            services.AddScoped<IEventBus, LocalEventBus>();
            services.AddScoped<IDomainEventPublisher, DomainEventPublisher>();
            services.AddSingleton<IEventStoreSerializer, EventStoreJsonSerializer>();

            return services;
        }

        public static IServiceCollection AddRabbitMqEventBus(this IServiceCollection services, string connectionString)
        {
            services.AddSingleton<IEventBus>(sp => new RabbitMqEventBus(connectionString));
            services.AddScoped<IDomainEventPublisher, DomainEventPublisher>();
            services.AddSingleton<IEventStoreSerializer, EventStoreJsonSerializer>();

            return services;
        }
    }
}