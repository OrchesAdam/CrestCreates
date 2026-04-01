using System;
using System.Linq;
using System.Reflection;
using System.Threading;
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
                if (returnType.IsGenericType)
                {
                    var taskType = returnType.GetGenericTypeDefinition();
                    var resultType = returnType.GetGenericArguments()[0];
                    
                    if (taskType == typeof(Task<>))
                    {
                        HandleGenericTask(invocation, attribute, resultType);
                    }
                    else
                    {
                        HandleNonGenericTask(invocation, attribute);
                    }
                }
                else
                {
                    HandleNonGenericTask(invocation, attribute);
                }
            }
            else
            {
                InterceptCacheableSync(invocation, attribute);
            }
        }
        
        private void HandleNonGenericTask(IInvocation invocation, CacheableAttribute attribute)
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
                invocation.ReturnValue = Task.CompletedTask;
                return;
            }

            _logger.LogDebug("Cache miss for key: {CacheKey}", cacheKey);
            invocation.Proceed();

            var task = (Task)invocation.ReturnValue;
            task.GetAwaiter().GetResult();
            
            var expiration = TimeSpan.FromSeconds(attribute.Expiration);
            _cache.SetAsync(cacheKey, new object(), expiration).GetAwaiter().GetResult();
            _logger.LogDebug("Cached value for key: {CacheKey}", cacheKey);
        }
        
        private void HandleGenericTask(IInvocation invocation, CacheableAttribute attribute, Type resultType)
        {
            var parameters = invocation.Method.GetParameters();
            
            if (!_expressionParser.EvaluateCondition(attribute.Condition, invocation.Arguments, parameters))
            {
                invocation.Proceed();
                return;
            }

            var cacheKey = GenerateCacheKey(attribute.CacheName, attribute.Key, invocation.Arguments, parameters);
            
            // 尝试从缓存获取值
            var getMethod = typeof(ICache).GetMethod("GetAsync", new[] { typeof(string), typeof(CancellationToken) });
            var genericGetMethod = getMethod?.MakeGenericMethod(resultType);
            var cachedValue = genericGetMethod?.Invoke(_cache, new object[] { cacheKey, CancellationToken.None });
            
            if (cachedValue != null)
            {
                _logger.LogDebug("Cache hit for key: {CacheKey}", cacheKey);
                var taskFromResultMethod = typeof(Task).GetMethod("FromResult");
                if (taskFromResultMethod != null)
                {
                    var genericTaskFromResultMethod = taskFromResultMethod.MakeGenericMethod(resultType);
                    invocation.ReturnValue = genericTaskFromResultMethod.Invoke(null, new[] { cachedValue });
                }
                else
                {
                    invocation.Proceed();
                }
                return;
            }

            _logger.LogDebug("Cache miss for key: {CacheKey}", cacheKey);
            invocation.Proceed();

            var task = invocation.ReturnValue;
            var taskResultProperty = task.GetType().GetProperty("Result");
            var result = taskResultProperty?.GetValue(task);
            
            if (result != null && !_expressionParser.EvaluateCondition(attribute.Unless, new object[] { result }, new[] { new FakeParameterInfo("result", resultType) }))
            {
                var expiration = TimeSpan.FromSeconds(attribute.Expiration);
                var setMethod = typeof(ICache).GetMethod("SetAsync", new[] { typeof(string), typeof(object), typeof(TimeSpan?), typeof(CancellationToken) });
                setMethod?.Invoke(_cache, new object[] { cacheKey, result, expiration, CancellationToken.None });
                _logger.LogDebug("Cached value for key: {CacheKey}", cacheKey);
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



        private void InterceptCachePut(IInvocation invocation, CachePutAttribute attribute)
        {
            invocation.Proceed();
            
            var returnType = invocation.Method.ReturnType;
            if (typeof(Task).IsAssignableFrom(returnType))
            {
                if (returnType.IsGenericType)
                {
                    var resultType = returnType.GetGenericArguments()[0];
                    HandleCachePutGenericTask(invocation, attribute, resultType);
                }
                else
                {
                    HandleCachePutNonGenericTask(invocation, attribute);
                }
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

        private void HandleCachePutNonGenericTask(IInvocation invocation, CachePutAttribute attribute)
        {
            var parameters = invocation.Method.GetParameters();
            
            if (!_expressionParser.EvaluateCondition(attribute.Condition, invocation.Arguments, parameters))
                return;

            var cacheKey = GenerateCacheKey(attribute.CacheName, attribute.Key, invocation.Arguments, parameters);
            
            var task = (Task)invocation.ReturnValue;
            task.GetAwaiter().GetResult();
            
            var expiration = TimeSpan.FromSeconds(attribute.Expiration);
            _cache.SetAsync(cacheKey, new object(), expiration).GetAwaiter().GetResult();
            _logger.LogDebug("Cache put for key: {CacheKey}", cacheKey);
        }

        private void HandleCachePutGenericTask(IInvocation invocation, CachePutAttribute attribute, Type resultType)
        {
            var parameters = invocation.Method.GetParameters();
            
            if (!_expressionParser.EvaluateCondition(attribute.Condition, invocation.Arguments, parameters))
                return;

            var cacheKey = GenerateCacheKey(attribute.CacheName, attribute.Key, invocation.Arguments, parameters);
            
            var task = invocation.ReturnValue;
            var taskResultProperty = task.GetType().GetProperty("Result");
            var result = taskResultProperty?.GetValue(task);
            
            if (result != null && !_expressionParser.EvaluateCondition(attribute.Unless, new object[] { result }, new[] { new FakeParameterInfo("result", resultType) }))
            {
                var expiration = TimeSpan.FromSeconds(attribute.Expiration);
                var setMethod = typeof(ICache).GetMethod("SetAsync", new[] { typeof(string), typeof(object), typeof(TimeSpan?), typeof(CancellationToken) });
                setMethod?.Invoke(_cache, new object[] { cacheKey, result, expiration, CancellationToken.None });
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
