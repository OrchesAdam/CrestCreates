using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Infrastructure.Caching.Advanced
{
    public class CacheWarmerService : BackgroundService
    {
        private readonly IEnumerable<ICacheWarmer> _warmers;
        private readonly ILogger<CacheWarmerService> _logger;

        public CacheWarmerService(
            IEnumerable<ICacheWarmer> warmers,
            ILogger<CacheWarmerService> logger)
        {
            _warmers = warmers ?? throw new ArgumentNullException(nameof(warmers));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting cache warmers...");

            var sortedWarmers = _warmers.OrderBy(w => w.Priority).ToList();

            foreach (var warmer in sortedWarmers)
            {
                try
                {
                    _logger.LogInformation("Executing cache warmer: {WarmerName}", warmer.Name);
                    await warmer.WarmUpAsync(stoppingToken);
                    _logger.LogInformation("Cache warmer completed: {WarmerName}", warmer.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Cache warmer failed: {WarmerName}", warmer.Name);
                }
            }

            _logger.LogInformation("All cache warmers completed");
        }
    }
}
