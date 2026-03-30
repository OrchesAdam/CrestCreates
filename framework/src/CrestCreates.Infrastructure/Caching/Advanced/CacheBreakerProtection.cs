using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Infrastructure.Caching.Advanced
{
    public interface ICacheBreakerProtection
    {
        Task<T?> GetOrProtectAsync<T>(
            string key,
            Func<Task<T?>> factory,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default);
    }

    public class CacheBreakerProtection : ICacheBreakerProtection
    {
        private readonly ICache _cache;
        private readonly ILogger<CacheBreakerProtection> _logger;
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public CacheBreakerProtection(
            ICache cache,
            ILogger<CacheBreakerProtection> logger)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<T?> GetOrProtectAsync<T>(
            string key,
            Func<Task<T?>> factory,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default)
        {
            var cachedValue = await _cache.GetAsync<T>(key, cancellationToken);
            if (cachedValue != null)
            {
                return cachedValue;
            }

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                cachedValue = await _cache.GetAsync<T>(key, cancellationToken);
                if (cachedValue != null)
                {
                    return cachedValue;
                }

                var value = await factory();
                if (value != null)
                {
                    var jitter = TimeSpan.FromSeconds(new Random().Next(0, 60));
                    var finalExpiration = expiration.HasValue 
                        ? expiration.Value + jitter 
                        : TimeSpan.FromMinutes(10) + jitter;

                    await _cache.SetAsync(key, value, finalExpiration, cancellationToken);
                    _logger.LogDebug("Cache protected with jitter for key: {Key}, expiration: {Expiration}", key, finalExpiration);
                }

                return value;
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}
