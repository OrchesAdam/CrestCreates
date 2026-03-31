using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using CrestCreates.Infrastructure.Modularity;

namespace CrestCreates.EventBus.Kafka
{
    public class KafkaEventBusModule : ModuleBase
    {
        private readonly IConfiguration _configuration;

        public KafkaEventBusModule(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public override void OnConfigureServices(IServiceCollection services)
        {
            var connectionString = _configuration.GetConnectionString("Kafka") ?? throw new InvalidOperationException("Kafka connection string not configured");
            
            services.AddSingleton<CrestCreates.EventBus.Abstract.IEventBus>(sp => 
                new KafkaEventBus(connectionString));
            
            services.AddScoped<CrestCreates.Domain.DomainEvents.IDomainEventPublisher, CrestCreates.EventBus.Local.DomainEventPublisher>();
        }
    }
}