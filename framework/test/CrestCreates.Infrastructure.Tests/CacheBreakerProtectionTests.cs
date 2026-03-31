using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Xunit;
using FluentAssertions;
using CrestCreates.Infrastructure.Caching;
using CrestCreates.Infrastructure.Caching.Advanced;
using MsMemoryCache = Microsoft.Extensions.Caching.Memory.MemoryCache;
using OurMemoryCache = CrestCreates.Infrastructure.Caching.MemoryCache;

namespace CrestCreates.Infrastructure.Tests
{
    public class CacheBreakerProtectionTests
    {
        private readonly CacheBreakerProtection _cacheBreaker;
        private readonly OurMemoryCache _memoryCache;

        public CacheBreakerProtectionTests()
        {
            var innerCache = new MsMemoryCache(new MemoryCacheOptions());
            _memoryCache = new OurMemoryCache(innerCache);
            _cacheBreaker = new CacheBreakerProtection(_memoryCache);
        }

        [Fact]
        public async Task GetOrCreateAsync_Should_Return_Value_From_Cache()
        {
            var key = "test-key";
            var expectedValue = "test-value";

            // 第一次调用，从数据源获取
            var result1 = await _cacheBreaker.GetOrCreateAsync(key, async () =>
            {
                await Task.Delay(10);
                return expectedValue;
            });

            // 第二次调用，从缓存获取
            var result2 = await _cacheBreaker.GetOrCreateAsync(key, async () =>
            {
                await Task.Delay(10);
                return "different-value";
            });

            result1.Should().Be(expectedValue);
            result2.Should().Be(expectedValue);
        }

        [Fact]
        public async Task GetOrCreateAsync_Should_Cache_Null_Value()
        {
            var key = "test-null-key";
            int callCount = 0;

            // 第一次调用，返回 null
            var result1 = await _cacheBreaker.GetOrCreateAsync<string>(key, async () =>
            {
                callCount++;
                await Task.Delay(10);
                return null;
            });

            // 第二次调用，应该从空值缓存获取
            var result2 = await _cacheBreaker.GetOrCreateAsync<string>(key, async () =>
            {
                callCount++;
                await Task.Delay(10);
                return "should-not-be-called";
            });

            result1.Should().BeNull();
            result2.Should().BeNull();
            callCount.Should().Be(1);
        }

        [Fact]
        public async Task GetOrCreateAsync_Should_Cache_Default_Value()
        {
            var key = "test-default-key";
            int callCount = 0;

            // 第一次调用，返回默认值
            var result1 = await _cacheBreaker.GetOrCreateAsync<int>(key, async () =>
            {
                callCount++;
                await Task.Delay(10);
                return default;
            });

            // 第二次调用，应该从空值缓存获取
            var result2 = await _cacheBreaker.GetOrCreateAsync<int>(key, async () =>
            {
                callCount++;
                await Task.Delay(10);
                return 42;
            });

            result1.Should().Be(default(int));
            result2.Should().Be(default(int));
            callCount.Should().Be(1);
        }

        [Fact]
        public async Task RemoveAsync_Should_Remove_Cache_And_Null_Value()
        {
            var key = "test-remove-key";
            var expectedValue = "test-value";
            int callCount = 0;

            // 第一次调用，从数据源获取
            var result1 = await _cacheBreaker.GetOrCreateAsync(key, async () =>
            {
                callCount++;
                await Task.Delay(10);
                return expectedValue;
            });

            // 移除缓存
            await _cacheBreaker.RemoveAsync(key);

            // 第二次调用，应该重新从数据源获取
            var result2 = await _cacheBreaker.GetOrCreateAsync(key, async () =>
            {
                callCount++;
                await Task.Delay(10);
                return expectedValue;
            });

            result1.Should().Be(expectedValue);
            result2.Should().Be(expectedValue);
            callCount.Should().Be(2);
        }

        [Fact]
        public void ClearBloomFilter_Should_Clear_All_Bloom_Filter_Data()
        {
            var key = "test-clear-key";

            // 调用 GetOrCreateAsync 来填充布隆过滤器
            _cacheBreaker.GetOrCreateAsync(key, async () =>
            {
                await Task.Delay(10);
                return "test-value";
            }).Wait();

            // 清除布隆过滤器
            _cacheBreaker.ClearBloomFilter();

            // 再次调用应该重新从数据源获取
            int callCount = 0;
            _cacheBreaker.GetOrCreateAsync(key, async () =>
            {
                callCount++;
                await Task.Delay(10);
                return "test-value";
            }).Wait();

            callCount.Should().Be(1);
        }
    }
}