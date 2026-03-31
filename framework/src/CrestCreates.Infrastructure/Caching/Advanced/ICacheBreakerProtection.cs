using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Infrastructure.Caching.Advanced
{
    public interface ICacheBreakerProtection
    {
        Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> dataRetriever, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
        Task RemoveAsync(string key, CancellationToken cancellationToken = default);
        void ClearBloomFilter();
    }
}