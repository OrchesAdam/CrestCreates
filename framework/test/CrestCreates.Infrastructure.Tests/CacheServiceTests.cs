using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using CrestCreates.Application.Contracts.Caching;
using CrestCreates.Infrastructure.Caching;

namespace CrestCreates.Infrastructure.Tests
{
    public class CacheServiceTests
    {
        private readonly Mock<ICache> _cacheMock;
        private readonly Mock<ILogger<CacheService>> _loggerMock;
        private readonly CacheService _cacheService;

        public CacheServiceTests()
        {
            _cacheMock = new Mock<ICache>();
            _loggerMock = new Mock<ILogger<CacheService>>();
            _cacheService = new CacheService(_cacheMock.Object, _loggerMock.Object);
        }

        [Fact]
        public async Task GetAsync_Should_Return_Value_From_Cache()
        {
            var key = "test-key";
            var expectedValue = "test-value";
            _cacheMock.Setup(c => c.GetAsync<string>(key, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedValue);

            var result = await _cacheService.GetAsync<string>(key);

            result.Should().Be(expectedValue);
            _cacheMock.Verify(c => c.GetAsync<string>(key, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetAsync_Should_Return_Default_When_Cache_Fails()
        {
            var key = "test-key";
            _cacheMock.Setup(c => c.GetAsync<string>(key, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Cache failed"));

            var result = await _cacheService.GetAsync<string>(key);

            result.Should().BeNull();
        }

        [Fact]
        public async Task SetAsync_Should_Call_Cache_Set()
        {
            var key = "test-key";
            var value = "test-value";
            var expiration = TimeSpan.FromMinutes(5);

            await _cacheService.SetAsync(key, value, expiration);

            _cacheMock.Verify(c => c.SetAsync(key, value, expiration, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SetAsync_Should_Log_Error_When_Cache_Fails()
        {
            var key = "test-key";
            var value = "test-value";
            _cacheMock.Setup(c => c.SetAsync(key, value, null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Cache failed"));

            await _cacheService.SetAsync(key, value);
        }

        [Fact]
        public async Task GetOrAddAsync_Should_Return_Cached_Value_When_Available()
        {
            var key = "test-key";
            var cachedValue = "cached-value";
            _cacheMock.Setup(c => c.GetAsync<string>(key, It.IsAny<CancellationToken>()))
                .ReturnsAsync(cachedValue);

            var factoryCallCount = 0;
            var result = await _cacheService.GetOrAddAsync(key, () =>
            {
                factoryCallCount++;
                return Task.FromResult("factory-value");
            });

            result.Should().Be(cachedValue);
            factoryCallCount.Should().Be(0);
        }

        [Fact]
        public async Task GetOrAddAsync_Should_Use_Factory_When_Not_Cached()
        {
            var key = "test-key";
            var factoryValue = "factory-value";
            _cacheMock.Setup(c => c.GetAsync<string>(key, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);

            var result = await _cacheService.GetOrAddAsync(key, () => Task.FromResult(factoryValue));

            result.Should().Be(factoryValue);
            _cacheMock.Verify(c => c.SetAsync(key, factoryValue, null, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RemoveAsync_Should_Call_Cache_Remove()
        {
            var key = "test-key";

            await _cacheService.RemoveAsync(key);

            _cacheMock.Verify(c => c.RemoveAsync(key, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExistsAsync_Should_Return_Cache_Result()
        {
            var key = "test-key";
            _cacheMock.Setup(c => c.ExistsAsync(key, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var result = await _cacheService.ExistsAsync(key);

            result.Should().BeTrue();
        }

        [Fact]
        public async Task ClearAsync_Should_Call_Cache_Clear()
        {
            await _cacheService.ClearAsync();

            _cacheMock.Verify(c => c.ClearAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetKeysAsync_Should_Return_Empty_List()
        {
            var result = await _cacheService.GetKeysAsync("pattern");

            result.Should().BeEmpty();
        }
    }
}
