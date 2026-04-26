// framework/src/CrestCreates.DistributedTransaction.CAP/BackgroundServices/CompensationRetryBackgroundService.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.CAP.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.DistributedTransaction.CAP.BackgroundServices
{
    public class CompensationRetryBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly DistributedTransactionCapOptions _options;
        private readonly ILogger<CompensationRetryBackgroundService> _logger;

        public CompensationRetryBackgroundService(
            IServiceProvider serviceProvider,
            IOptions<DistributedTransactionCapOptions> options,
            ILogger<CompensationRetryBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Compensation retry background service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessRetryingCompensationsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing retrying compensations");
                }

                await Task.Delay(
                    TimeSpan.FromSeconds(_options.CompensationRetryIntervalSeconds),
                    stoppingToken);
            }
        }

        private async Task ProcessRetryingCompensationsAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var compensator = scope.ServiceProvider
                .GetRequiredService<ITransactionCompensator>();

            await compensator.ProcessRetryingCompensationsAsync();
        }
    }
}
