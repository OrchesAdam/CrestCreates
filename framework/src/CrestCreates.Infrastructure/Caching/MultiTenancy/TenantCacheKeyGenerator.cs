using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.Options;

namespace CrestCreates.Infrastructure.Caching.MultiTenancy
{
    public class TenantCacheKeyGenerator : ICacheKeyGenerator
    {
        private readonly ICurrentTenant _currentTenant;
        private readonly CacheOptions _options;

        public TenantCacheKeyGenerator(
            ICurrentTenant currentTenant,
            IOptions<CacheOptions> options)
        {
            _currentTenant = currentTenant ?? throw new System.ArgumentNullException(nameof(currentTenant));
            _options = options?.Value ?? throw new System.ArgumentNullException(nameof(options));
        }

        public string GenerateKey(string baseKey)
        {
            var tenantId = _currentTenant.Id ?? "default";
            var prefix = _options.EnableKeyPrefix ? _options.KeyPrefix : "";
            return $"{prefix}{tenantId}:{baseKey}";
        }
    }
}
