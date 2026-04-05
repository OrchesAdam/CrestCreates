using CrestCreates.Aop.Abstractions;
using CrestCreates.Caching.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rougamo;
using Rougamo.Context;

namespace CrestCreates.Caching.Interceptors;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class CacheMoAttribute : AsyncMoAttribute
{
    private readonly string _prefix;
    private readonly int _expirationMinutes;
    public int Order => InterceptorOrders.Cache;

    public CacheMoAttribute(string prefix, int expirationMinutes = 0)
    {
        _prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
        _expirationMinutes = expirationMinutes;
    }

    public override async ValueTask OnEntryAsync(MethodContext context)
    {
        try
        {
            var cacheOptions = context.GetService<IOptions<CacheOptions>>()?.Value;
            if (cacheOptions?.Enabled != true)
            {
                return;
            }

            var cache = context.GetService<ICrestCacheService>();
            if (cache == null)
            {
                var logger = context.GetService<ILogger<CacheMoAttribute>>();
                logger?.LogWarning("ICrestCacheService 未注册，跳过缓存");
                return;
            }

            var keyGenerator = context.GetService<ICrestCacheKeyGenerator>();
            if (keyGenerator == null)
            {
                var logger = context.GetService<ILogger<CacheMoAttribute>>();
                logger?.LogWarning("ICrestCacheKeyGenerator 未注册，跳过缓存");
                return;
            }

            var cachedValue = await cache.GetAsync<object>(_prefix, context.Arguments);
            if (cachedValue != null)
            {
                context.ReturnValue = cachedValue;
            }
        }
        catch (Exception exception)
        {
            var logger = context.GetService<ILogger<CacheMoAttribute>>();
            logger?.LogWarning(exception, "缓存读取失败");
        }
    }

    public override async ValueTask OnSuccessAsync(MethodContext context)
    {
        try
        {
            var cacheOptions = context.GetService<IOptions<CacheOptions>>()?.Value;
            if (cacheOptions?.Enabled != true)
            {
                return;
            }

            var cache = context.GetService<ICrestCacheService>();
            if (cache == null)
            {
                return;
            }

            if (context.ReturnValue != null)
            {
                var expiration = _expirationMinutes > 0 
                    ? TimeSpan.FromMinutes(_expirationMinutes) 
                    : cacheOptions.DefaultExpiration;

                await cache.SetAsync(_prefix, context.ReturnValue, expiration, context.Arguments);
            }
        }
        catch (Exception exception)
        {
            var logger = context.GetService<ILogger<CacheMoAttribute>>();
            logger?.LogWarning(exception, "缓存写入失败");
        }
    }

    public override ValueTask OnExceptionAsync(MethodContext context)
    {
        return ValueTask.CompletedTask;
    }
}
