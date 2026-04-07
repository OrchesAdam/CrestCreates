using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.CAP.Abstractions;
using CrestCreates.DistributedTransaction.CAP.Implementations;
using CrestCreates.EventBus.Abstract;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.DistributedTransaction.CAP.Modules
{
    public class DistributedTransactionCapModule : ModuleBase
    {
        public override void OnConfigureServices(IServiceCollection services)
        {
            services.AddScoped<IDistributedTransactionManager, DistributedTransactionManager>();
            services.AddScoped<ITransactionLogger, TransactionLogger>();
            services.AddScoped<ITransactionCompensator, DefaultTransactionCompensator>();
            services.AddSingleton<ICapTopicNameProvider, DefaultCapTopicNameProvider>();
            services.AddScoped<IDistributedEventPublisher, CapDistributedEventPublisher>();
            services.AddScoped<IEventBus, CapEventBus>();
        }
    }
}
