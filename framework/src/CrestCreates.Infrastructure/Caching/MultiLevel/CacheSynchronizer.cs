using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CrestCreates.Infrastructure.Caching.MultiLevel
{
    public class CacheSynchronizer : BackgroundService
    {
        private readonly IConnectionMultiplexer _redisConnection;
        private readonly IServiceProvider _serviceProvider;
        private readonly MultiLevelCacheOptions _options;
        private readonly ILogger<CacheSynchronizer> _logger;
        private ISubscriber? _subscriber;

        public CacheSynchronizer(
            IConnectionMultiplexer redisConnection,
            IServiceProvider serviceProvider,
            Microsoft.Extensions.Options.IOptions<MultiLevelCacheOptions> options,
            ILogger<CacheSynchronizer> logger)
        {
            _redisConnection = redisConnection ?? throw new ArgumentNullException(nameof(redisConnection));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.EnableL1Sync)
            {
                _logger.LogInformation("L1 cache synchronization is disabled");
                return;
            }

            _subscriber = _redisConnection.GetSubscriber();

            await _subscriber.SubscribeAsync(_options.L1SyncChannel, (channel, message) =>
            {
                HandleSyncMessage(message);
            });

            _logger.LogInformation("Cache synchronizer started, listening on channel: {Channel}", _options.L1SyncChannel);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        private void HandleSyncMessage(string message)
        {
            try
            {
                var parts = message.Split(':');
                if (parts.Length < 2)
                    return;

                var action = parts[0];
                var key = parts[1];

                var cache = GetMultiLevelCache();
                if (cache == null)
                    return;

                switch (action)
                {
                    case "EVICT":
                        cache.RemoveFromL1(key);
                        _logger.LogDebug("Received eviction message for key: {Key}", key);
                        break;
                    case "CLEAR":
                        cache.ClearL1();
                        _logger.LogDebug("Received clear message");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling sync message: {Message}", message);
            }
        }

        public async Task PublishEvictionAsync(string key, CancellationToken cancellationToken = default)
        {
            if (!_options.EnableL1Sync || _subscriber == null)
                return;

            var message = $"EVICT:{key}";
            await _subscriber.PublishAsync(_options.L1SyncChannel, message);
            _logger.LogDebug("Published eviction message for key: {Key}", key);
        }

        public async Task PublishClearAsync(CancellationToken cancellationToken = default)
        {
            if (!_options.EnableL1Sync || _subscriber == null)
                return;

            var message = "CLEAR:all";
            await _subscriber.PublishAsync(_options.L1SyncChannel, message);
            _logger.LogDebug("Published clear message");
        }

        private MultiLevelCache? GetMultiLevelCache()
        {
            try
            {
                return _serviceProvider.GetService(typeof(MultiLevelCache)) as MultiLevelCache;
            }
            catch
            {
                return null;
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_subscriber != null)
            {
                await _subscriber.UnsubscribeAsync(_options.L1SyncChannel);
            }
            await base.StopAsync(cancellationToken);
        }
    }
}
