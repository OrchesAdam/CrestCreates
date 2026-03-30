using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Xunit;
using FluentAssertions;
using CrestCreates.Infrastructure.Caching;
using MsMemoryCache = Microsoft.Extensions.Caching.Memory.MemoryCache;
using OurMemoryCache = CrestCreates.Infrastructure.Caching.MemoryCache;

namespace CrestCreates.Infrastructure.Tests
{
    public class MemoryCacheTests
    {
        private readonly OurMemoryCache _memoryCache;
        private readonly MsMemoryCache _innerCache;

        public MemoryCacheTests()
        {
            _innerCache = new MsMemoryCache(new MemoryCacheOptions());
            _memoryCache = new OurMemoryCache(_innerCache);
        }

        [Fact]
        public async Task GetAsync_Should_Return_Null_When_Key_Not_Exists()
        {
            var result = await _memoryCache.GetAsync<string>("nonexistent-key");
            result.Should().BeNull();
        }

        [Fact]
        public async Task SetAsync_Should_Store_Value_In_Cache()
        {
            var key = "test-key";
            var value = "test-value";

            await _memoryCache.SetAsync(key, value);
            var result = await _memoryCache.GetAsync<string>(key);

            result.Should().Be(value);
        }

        [Fact]
        public async Task SetAsync_Should_Store_Value_With_Expiration()
        {
            var key = "test-expire-key";
            var value = "test-expire-value";
            var expiration = TimeSpan.FromMilliseconds(100);

            await _memoryCache.SetAsync(key, value, expiration);
            var result = await _memoryCache.GetAsync<string>(key);
            result.Should().Be(value);
        }

        [Fact]
        public async Task RemoveAsync_Should_Remove_Value_From_Cache()
        {
            var key = "test-remove-key";
            var value = "test-remove-value";

            await _memoryCache.SetAsync(key, value);
            await _memoryCache.RemoveAsync(key);
            var result = await _memoryCache.GetAsync<string>(key);

            result.Should().BeNull();
        }

        [Fact]
        public async Task ExistsAsync_Should_Return_True_When_Key_Exists()
        {
            var key = "test-exists-key";
            var value = "test-exists-value";

            await _memoryCache.SetAsync(key, value);
            var result = await _memoryCache.ExistsAsync(key);

            result.Should().BeTrue();
        }

        [Fact]
        public async Task ExistsAsync_Should_Return_False_When_Key_Not_Exists()
        {
            var result = await _memoryCache.ExistsAsync("nonexistent-key");
            result.Should().BeFalse();
        }

        [Fact]
        public async Task SetAsync_Should_Store_Complex_Object()
        {
            var key = "test-object-key";
            var value = new TestObject { Id = 1, Name = "Test" };

            await _memoryCache.SetAsync(key, value);
            var result = await _memoryCache.GetAsync<TestObject>(key);

            result.Should().NotBeNull();
            result.Id.Should().Be(1);
            result.Name.Should().Be("Test");
        }

        [Fact]
        public async Task SetAsync_Should_Store_Multiple_Values()
        {
            var key1 = "key1";
            var value1 = "value1";
            var key2 = "key2";
            var value2 = "value2";

            await _memoryCache.SetAsync(key1, value1);
            await _memoryCache.SetAsync(key2, value2);

            var result1 = await _memoryCache.GetAsync<string>(key1);
            var result2 = await _memoryCache.GetAsync<string>(key2);

            result1.Should().Be(value1);
            result2.Should().Be(value2);
        }

        private class TestObject
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }
    }
}
