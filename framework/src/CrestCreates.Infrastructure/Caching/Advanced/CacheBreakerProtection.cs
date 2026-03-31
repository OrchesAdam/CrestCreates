using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Infrastructure.Caching;

namespace CrestCreates.Infrastructure.Caching.Advanced
{
    public class CacheBreakerProtection : ICacheBreakerProtection
    {
        private readonly ICache _cache;
        private readonly Dictionary<string, bool> _bloomFilter;
        private readonly TimeSpan _nullValueExpiration;

        public CacheBreakerProtection(ICache cache, TimeSpan? nullValueExpiration = null)
        {
            _cache = cache;
            _bloomFilter = new Dictionary<string, bool>();
            _nullValueExpiration = nullValueExpiration ?? TimeSpan.FromMinutes(5);
        }

        /// <summary>
        /// 获取缓存，如果不存在则从数据源获取并缓存
        /// </summary>
        /// <typeparam name="T">缓存数据类型</typeparam>
        /// <param name="key">缓存键</param>
        /// <param name="dataRetriever">数据源获取函数</param>
        /// <param name="expiration">缓存过期时间</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>缓存的数据</returns>
        public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> dataRetriever, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
        {
            // 1. 检查布隆过滤器，如果不存在则直接返回默认值
            if (!_bloomFilter.ContainsKey(key) || !_bloomFilter[key])
            {
                // 检查是否存在空值缓存
                var nullValueKey = GetNullValueKey(key);
                if (await _cache.ExistsAsync(nullValueKey, cancellationToken))
                {
                    return default;
                }
            }

            // 2. 尝试从缓存获取
            var cachedValue = await _cache.GetAsync<T>(key, cancellationToken);
            if (cachedValue != null && !cachedValue.Equals(default(T)))
            {
                return cachedValue;
            }

            // 3. 从数据源获取
            try
            {
                var value = await dataRetriever();
                
                if (value != null && !value.Equals(default(T)))
                {
                    // 缓存数据
                    await _cache.SetAsync(key, value, expiration, cancellationToken);
                    // 更新布隆过滤器
                    _bloomFilter[key] = true;
                    return value;
                }
                else
                {
                    // 缓存空值
                    var nullValueKey = GetNullValueKey(key);
                    await _cache.SetAsync(nullValueKey, true, _nullValueExpiration, cancellationToken);
                    // 更新布隆过滤器
                    _bloomFilter[key] = false;
                    return default;
                }
            }
            catch (Exception ex)
            {
                // 处理数据源获取失败的情况
                Console.WriteLine($"Error retrieving data: {ex.Message}");
                return default;
            }
        }

        /// <summary>
        /// 移除缓存
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            await _cache.RemoveAsync(key, cancellationToken);
            var nullValueKey = GetNullValueKey(key);
            await _cache.RemoveAsync(nullValueKey, cancellationToken);
            _bloomFilter.Remove(key);
        }

        /// <summary>
        /// 清除所有缓存保护数据
        /// </summary>
        public void ClearBloomFilter()
        {
            _bloomFilter.Clear();
        }

        /// <summary>
        /// 获取空值缓存的键
        /// </summary>
        private string GetNullValueKey(string originalKey)
        {
            return $"null:{originalKey}";
        }
    }
}