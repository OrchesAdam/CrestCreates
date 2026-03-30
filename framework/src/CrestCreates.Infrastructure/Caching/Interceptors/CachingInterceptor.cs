using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using CrestCreates.Infrastructure.Caching.Attributes;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Infrastructure.Caching.Interceptors
{
    public class CachingInterceptor : IInterceptor
    {
        private readonly ICache _cache;
        private readonly ICacheKeyExpressionParser _expressionParser;
        private readonly ILogger<CachingInterceptor> _logger;

        public CachingInterceptor(
            ICache cache,
            ICacheKeyExpressionParser expressionParser,
            ILogger<CachingInterceptor> logger)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _expressionParser = expressionParser ?? throw new ArgumentNullException(nameof(expressionParser));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Intercept(IInvocation invocation)
        {
            var cacheableAttr = GetAttribute<CacheableAttribute>(invocation);
            var cacheEvictAttrs = GetAttributes<CacheEvictAttribute>(invocation);
            var cachePutAttr = GetAttribute<CachePutAttribute>(invocation);

            if (cacheableAttr != null)
            {
                InterceptCacheable(invocation, cacheableAttr);
                return;
            }

            if (cachePutAttr != null)
            {
                InterceptCachePut(invocation, cachePutAttr);
                return;
            }

            if (cacheEvictAttrs.Any())
            {
                InterceptCacheEvict(invocation, cacheEvictAttrs);
                return;
            }

            invocation.Proceed();
        }

        private void InterceptCacheable(IInvocation invocation, CacheableAttribute attribute)
        {
            var returnType = invocation.Method.ReturnType;
            
            if (typeof(Task).IsAssignableFrom(returnType))
            {
                var resultType = returnType.IsGenericType ? returnType.GetGenericArguments()[0] : typeof(object);
                var method = typeof(CachingInterceptor).GetMethod(nameof(InterceptCacheableAsync), BindingFlags.NonPublic | BindingFlags.Instance);
                var genericMethod = method?.MakeGenericMethod(resultType);
                genericMethod?.Invoke(this, new object[] { invocation, attribute });
            }
            else
            {
                InterceptCacheableSync(invocation, attribute);
            }
        }

        private void InterceptCacheableSync(IInvocation invocation, CacheableAttribute attribute)
        {
            var parameters = invocation.Method.GetParameters();
            
            if (!_expressionParser.EvaluateCondition(attribute.Condition, invocation.Arguments, parameters))
            {
                invocation.Proceed();
                return;
            }

            var cacheKey = GenerateCacheKey(attribute.CacheName, attribute.Key, invocation.Arguments, parameters);
            
            var cachedValue = _cache.GetAsync<object>(cacheKey).GetAwaiter().GetResult();
            if (cachedValue != null)
            {
                _logger.LogDebug("Cache hit for key: {CacheKey}", cacheKey);
                invocation.ReturnValue = cachedValue;
                return;
            }

            _logger.LogDebug("Cache miss for key: {CacheKey}", cacheKey);
            invocation.Proceed();

            var result = invocation.ReturnValue;
            if (result != null && _expressionParser.EvaluateCondition(attribute.Unless, new[] { result }, new[] { new FakeParameterInfo("result", result.GetType()) }))
            {
                var expiration = TimeSpan.FromSeconds(attribute.Expiration);
                _cache.SetAsync(cacheKey, result, expiration).GetAwaiter().GetResult();
                _logger.LogDebug("Cached value for key: {CacheKey}", cacheKey);
            }
        }

        private async Task<T?> InterceptCacheableAsync<T>(IInvocation invocation, CacheableAttribute attribute)
        {
            var parameters = invocation.Method.GetParameters();
            
            if (!_expressionParser.EvaluateCondition(attribute.Condition, invocation.Arguments, parameters))
            {
                invocation.Proceed();
                var task = (Task<T>)invocation.ReturnValue;
                return await task;
            }

            var cacheKey = GenerateCacheKey(attribute.CacheName, attribute.Key, invocation.Arguments, parameters);
            
            var cachedValue = await _cache.GetAsync<T>(cacheKey);
            if (cachedValue != null)
            {
                _logger.LogDebug("Cache hit for key: {CacheKey}", cacheKey);
                invocation.ReturnValue = Task.FromResult(cachedValue);
                return cachedValue;
            }

            _logger.LogDebug("Cache miss for key: {CacheKey}", cacheKey);
            invocation.Proceed();

            var taskResult = (Task<T>)invocation.ReturnValue;
            var result = await taskResult;
            
            if (result != null && !_expressionParser.EvaluateCondition(attribute.Unless, new object[] { result }, new[] { new FakeParameterInfo("result", typeof(T)) }))
            {
                var expiration = TimeSpan.FromSeconds(attribute.Expiration);
                await _cache.SetAsync(cacheKey, result, expiration);
                _logger.LogDebug("Cached value for key: {CacheKey}", cacheKey);
            }

            return result;
        }

        private void InterceptCachePut(IInvocation invocation, CachePutAttribute attribute)
        {
            invocation.Proceed();
            
            var returnType = invocation.Method.ReturnType;
            if (typeof(Task).IsAssignableFrom(returnType))
            {
                var resultType = returnType.IsGenericType ? returnType.GetGenericArguments()[0] : typeof(object);
                var method = typeof(CachingInterceptor).GetMethod(nameof(HandleCachePutAsync), BindingFlags.NonPublic | BindingFlags.Instance);
                var genericMethod = method?.MakeGenericMethod(resultType);
                genericMethod?.Invoke(this, new object[] { invocation, attribute, null });
            }
            else
            {
                HandleCachePutSync(invocation, attribute);
            }
        }

        private void HandleCachePutSync(IInvocation invocation, CachePutAttribute attribute)
        {
            var parameters = invocation.Method.GetParameters();
            
            if (!_expressionParser.EvaluateCondition(attribute.Condition, invocation.Arguments, parameters))
                return;

            var cacheKey = GenerateCacheKey(attribute.CacheName, attribute.Key, invocation.Arguments, parameters);
            var valueToCache = invocation.ReturnValue;

            if (valueToCache != null && !_expressionParser.EvaluateCondition(attribute.Unless, new[] { valueToCache }, new[] { new FakeParameterInfo("result", valueToCache.GetType()) }))
            {
                var expiration = TimeSpan.FromSeconds(attribute.Expiration);
                _cache.SetAsync(cacheKey, valueToCache, expiration).GetAwaiter().GetResult();
                _logger.LogDebug("Cache put for key: {CacheKey}", cacheKey);
            }
        }

        private async Task HandleCachePutAsync<T>(IInvocation invocation, CachePutAttribute attribute, object? result = null)
        {
            var parameters = invocation.Method.GetParameters();
            
            if (!_expressionParser.EvaluateCondition(attribute.Condition, invocation.Arguments, parameters))
                return;

            var cacheKey = GenerateCacheKey(attribute.CacheName, attribute.Key, invocation.Arguments, parameters);
            
            T? valueToCache;
            if (result != null)
            {
                valueToCache = (T)result;
            }
            else
            {
                var task = (Task<T>)invocation.ReturnValue;
                valueToCache = await task;
            }

            if (valueToCache != null && !_expressionParser.EvaluateCondition(attribute.Unless, new object[] { valueToCache! }, new[] { new FakeParameterInfo("result", typeof(T)) }))
            {
                var expiration = TimeSpan.FromSeconds(attribute.Expiration);
                await _cache.SetAsync(cacheKey, valueToCache, expiration);
                _logger.LogDebug("Cache put for key: {CacheKey}", cacheKey);
            }
        }

        private void InterceptCacheEvict(IInvocation invocation, CacheEvictAttribute[] attributes)
        {
            var parameters = invocation.Method.GetParameters();
            
            foreach (var attribute in attributes.Where(a => a.BeforeInvocation))
            {
                HandleCacheEvictSync(attribute, invocation.Arguments, parameters);
            }

            invocation.Proceed();

            foreach (var attribute in attributes.Where(a => !a.BeforeInvocation))
            {
                HandleCacheEvictSync(attribute, invocation.Arguments, parameters);
            }
        }

        private void HandleCacheEvictSync(CacheEvictAttribute attribute, object[] args, ParameterInfo[] parameters)
        {
            if (!_expressionParser.EvaluateCondition(attribute.Condition, args, parameters))
                return;

            if (attribute.AllEntries)
            {
                _cache.ClearAsync().GetAwaiter().GetResult();
                _logger.LogDebug("Cache cleared for cache name: {CacheName}", attribute.CacheName);
            }
            else
            {
                var cacheKey = GenerateCacheKey(attribute.CacheName, attribute.Key, args, parameters);
                _cache.RemoveAsync(cacheKey).GetAwaiter().GetResult();
                _logger.LogDebug("Cache evicted for key: {CacheKey}", cacheKey);
            }
        }

        private string GenerateCacheKey(string cacheName, string? keyExpression, object[] args, ParameterInfo[] parameters)
        {
            var keySuffix = _expressionParser.Parse(keyExpression ?? string.Empty, args, parameters);
            return string.IsNullOrEmpty(keySuffix) ? $"{cacheName}" : $"{cacheName}:{keySuffix}";
        }

        private TAttribute? GetAttribute<TAttribute>(IInvocation invocation) where TAttribute : Attribute
        {
            return invocation.Method.GetCustomAttribute<TAttribute>() ??
                   invocation.MethodInvocationTarget.GetCustomAttribute<TAttribute>();
        }

        private TAttribute[] GetAttributes<TAttribute>(IInvocation invocation) where TAttribute : Attribute
        {
            var methodAttrs = invocation.Method.GetCustomAttributes<TAttribute>();
            var targetAttrs = invocation.MethodInvocationTarget.GetCustomAttributes<TAttribute>();
            return methodAttrs.Concat(targetAttrs).ToArray();
        }

        private class FakeParameterInfo : ParameterInfo
        {
            private readonly string _name;
            private readonly Type _type;

            public FakeParameterInfo(string name, Type type)
            {
                _name = name;
                _type = type;
            }

            public override string Name => _name;
            public override Type ParameterType => _type;
        }
    }
}
