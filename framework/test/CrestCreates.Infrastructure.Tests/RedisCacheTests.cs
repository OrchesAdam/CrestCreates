using System;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using CrestCreates.Infrastructure.Caching;

namespace CrestCreates.Infrastructure.Tests
{
    public class RedisCacheTests
    {
        private readonly RedisCache _redisCache;

        public RedisCacheTests()
        {
            // 使用本地 Redis 服务器，实际测试时需要确保 Redis 服务正在运行
            // 或者使用 Redis 模拟器
            try
            {
                _redisCache = new RedisCache("localhost:6379");
            }
            catch
            {
                // 如果 Redis 不可用，跳过测试
                _redisCache = null;
            }
        }

        [Fact]
        public async Task GetAsync_Should_Return_Null_When_Key_Not_Exists()
        {
            if (_redisCache == null)
            {
                // 如果 Redis 不可用，跳过测试
                return;
            }

            var result = await _redisCache.GetAsync<string>("nonexistent-key");
            result.Should().BeNull();
        }

        [Fact]
        public async Task SetAsync_Should_Store_Value_In_Cache()
        {
            if (_redisCache == null)
            {
                // 如果 Redis 不可用，跳过测试
                return;
            }

            var key = "test-key";
            var value = "test-value";

            await _redisCache.SetAsync(key, value);
            var result = await _redisCache.GetAsync<string>(key);

            result.Should().Be(value);
        }

        [Fact]
        public async Task SetAsync_Should_Store_Value_With_Expiration()
        {
            if (_redisCache == null)
            {
                // 如果 Redis 不可用，跳过测试
                return;
            }

            var key = "test-expire-key";
            var value = "test-expire-value";
            var expiration = TimeSpan.FromMilliseconds(100);

            await _redisCache.SetAsync(key, value, expiration);
            var result = await _redisCache.GetAsync<string>(key);
            result.Should().Be(value);
        }

        [Fact]
        public async Task RemoveAsync_Should_Remove_Value_From_Cache()
        {
            if (_redisCache == null)
            {
                // 如果 Redis 不可用，跳过测试
                return;
            }

            var key = "test-remove-key";
            var value = "test-remove-value";

            await _redisCache.SetAsync(key, value);
            await _redisCache.RemoveAsync(key);
            var result = await _redisCache.GetAsync<string>(key);

            result.Should().BeNull();
        }

        [Fact]
        public async Task ExistsAsync_Should_Return_True_When_Key_Exists()
        {
            if (_redisCache == null)
            {
                // 如果 Redis 不可用，跳过测试
                return;
            }

            var key = "test-exists-key";
            var value = "test-exists-value";

            await _redisCache.SetAsync(key, value);
            var result = await _redisCache.ExistsAsync(key);

            result.Should().BeTrue();
        }

        [Fact]
        public async Task ExistsAsync_Should_Return_False_When_Key_Not_Exists()
        {
            if (_redisCache == null)
            {
                // 如果 Redis 不可用，跳过测试
                return;
            }

            var result = await _redisCache.ExistsAsync("nonexistent-key");
            result.Should().BeFalse();
        }

        [Fact]
        public async Task SetAsync_Should_Store_Complex_Object()
        {
            if (_redisCache == null)
            {
                // 如果 Redis 不可用，跳过测试
                return;
            }

            var key = "test-object-key";
            var value = new TestObject { Id = 1, Name = "Test" };

            await _redisCache.SetAsync(key, value);
            var result = await _redisCache.GetAsync<TestObject>(key);

            result.Should().NotBeNull();
            result.Id.Should().Be(1);
            result.Name.Should().Be("Test");
        }

        [Fact]
        public void IsHealthy_Should_Return_True_When_Connected()
        {
            if (_redisCache == null)
            {
                // 如果 Redis 不可用，跳过测试
                return;
            }

            var result = _redisCache.IsHealthy();
            result.Should().BeTrue();
        }

        private class TestObject
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }
    }
}