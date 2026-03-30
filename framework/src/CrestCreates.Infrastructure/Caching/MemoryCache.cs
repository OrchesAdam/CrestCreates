using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace CrestCreates.Infrastructure.Caching
{
    public class MemoryCache : ICache
    {
        private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _memoryCache;

        public MemoryCache(Microsoft.Extensions.Caching.Memory.IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        public async Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _memoryCache.TryGetValue(key, out T value) ? value : default;
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var options = new MemoryCacheEntryOptions();
            if (expiration.HasValue)
            {
                options.SetAbsoluteExpiration(expiration.Value);
            }
            _memoryCache.Set(key, value, options);
        }

        public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _memoryCache.Remove(key);
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _memoryCache.TryGetValue(key, out _);
        }

        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 内存缓存不支持全局清除，这里可以留空或者实现一个自定义的清除机制
        }
    }
}