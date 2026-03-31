using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using CrestCreates.Infrastructure.Modularity;

namespace CrestCreates.EventBus.RabbitMQ
{
    public class RabbitMqEventBusModule : ModuleBase
    {
        private readonly IConfiguration _configuration;

        public RabbitMqEventBusModule(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public override void OnConfigureServices(IServiceCollection services)
        {
            var connectionString = _configuration.GetConnectionString("RabbitMQ") ?? throw new InvalidOperationException("RabbitMQ connection string not configured");
            
            services.AddSingleton<CrestCreates.EventBus.Abstract.IEventBus>(sp => 
                new RabbitMqEventBus(connectionString));
            
            services.AddScoped<CrestCreates.Domain.DomainEvents.IDomainEventPublisher, CrestCreates.EventBus.Local.DomainEventPublisher>();
        }
    }
}