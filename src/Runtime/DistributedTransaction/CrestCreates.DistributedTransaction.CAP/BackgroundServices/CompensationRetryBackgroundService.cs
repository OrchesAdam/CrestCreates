// framework/src/CrestCreates.DistributedTransaction.CAP/BackgroundServices/CompensationRetryBackgroundService.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.CAP.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.DistributedTransaction.CAP.BackgroundServices
{
    /// <summary>
    /// 补偿重试后台服务：每个处理周期经**唯一 UoW 内核**建立持久化边界
    /// （scoped 内核 + 事务型窗口，flush 统一落库），补偿器的 Executing/Completed/Retrying/Failed
    /// 状态与重试计数不会随 scope 释放丢失。
    /// </summary>
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
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

            // 唯一内核承载持久化边界：处理周期内的仓储写入由 UoW flush/commit 统一落库。
            await manager.ExecuteAsync<object?>(
                async _ =>
                {
                    var compensator = scope.ServiceProvider.GetRequiredService<ITransactionCompensator>();
                    await compensator.ProcessRetryingCompensationsAsync().ConfigureAwait(false);
                    return null;
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
