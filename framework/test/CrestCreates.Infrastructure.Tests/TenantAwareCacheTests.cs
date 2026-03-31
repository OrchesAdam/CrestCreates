using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using CrestCreates.Infrastructure.Caching;
using CrestCreates.Infrastructure.Caching.MultiTenancy;

namespace CrestCreates.Infrastructure.Tests
{
    public class TenantAwareCacheTests
    {
        private readonly Mock<ICache> _mockInnerCache;
        private readonly Mock<ICacheKeyGenerator> _mockKeyGenerator;
        private readonly Mock<ILogger<TenantAwareCache>> _mockLogger;
        private readonly TenantAwareCache _tenantAwareCache;
        private readonly CancellationTokenSource _cts;

        public TenantAwareCacheTests()
        {
            _mockInnerCache = new Mock<ICache>();
            _mockKeyGenerator = new Mock<ICacheKeyGenerator>();
            _mockLogger = new Mock<ILogger<TenantAwareCache>>();
            _tenantAwareCache = new TenantAwareCache(
                _mockInnerCache.Object,
                _mockKeyGenerator.Object,
                _mockLogger.Object);
            _cts = new CancellationTokenSource();
        }

        [Fact]
        public void Constructor_WithNullInnerCache_ThrowsArgumentNullException()
        {
            var action = () => new TenantAwareCache(
                null!,
                _mockKeyGenerator.Object,
                _mockLogger.Object);

            action.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("innerCache");
        }

        [Fact]
        public void Constructor_WithNullKeyGenerator_ThrowsArgumentNullException()
        {
            var action = () => new TenantAwareCache(
                _mockInnerCache.Object,
                null!,
                _mockLogger.Object);

            action.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("keyGenerator");
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            var action = () => new TenantAwareCache(
                _mockInnerCache.Object,
                _mockKeyGenerator.Object,
                null!);

            action.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("logger");
        }

        [Fact]
        public async Task GetAsync_GeneratesTenantKeyAndCallsInnerCache()
        {
            var testKey = "test-key";
            var tenantKey = "tenant1:test-key";
            var testValue = "test-value";
            _mockKeyGenerator.Setup(g => g.GenerateKey(testKey)).Returns(tenantKey);
            _mockInnerCache.Setup(c => c.GetAsync<string>(tenantKey, _cts.Token))
                .ReturnsAsync(testValue);

            var result = await _tenantAwareCache.GetAsync<string>(testKey, _cts.Token);

            result.Should().Be(testValue);
            _mockKeyGenerator.Verify(g => g.GenerateKey(testKey), Times.Once);
            _mockInnerCache.Verify(c => c.GetAsync<string>(tenantKey, _cts.Token), Times.Once);
        }

        [Fact]
        public async Task SetAsync_GeneratesTenantKeyAndCallsInnerCache()
        {
            var testKey = "test-key";
            var tenantKey = "tenant1:test-key";
            var testValue = "test-value";
            var expiration = TimeSpan.FromMinutes(30);
            _mockKeyGenerator.Setup(g => g.GenerateKey(testKey)).Returns(tenantKey);

            await _tenantAwareCache.SetAsync(testKey, testValue, expiration, _cts.Token);

            _mockKeyGenerator.Verify(g => g.GenerateKey(testKey), Times.Once);
            _mockInnerCache.Verify(c => c.SetAsync(tenantKey, testValue, expiration, _cts.Token), Times.Once);
        }

        [Fact]
        public async Task RemoveAsync_GeneratesTenantKeyAndCallsInnerCache()
        {
            var testKey = "test-key";
            var tenantKey = "tenant1:test-key";
            _mockKeyGenerator.Setup(g => g.GenerateKey(testKey)).Returns(tenantKey);

            await _tenantAwareCache.RemoveAsync(testKey, _cts.Token);

            _mockKeyGenerator.Verify(g => g.GenerateKey(testKey), Times.Once);
            _mockInnerCache.Verify(c => c.RemoveAsync(tenantKey, _cts.Token), Times.Once);
        }

        [Fact]
        public async Task ExistsAsync_GeneratesTenantKeyAndCallsInnerCache()
        {
            var testKey = "test-key";
            var tenantKey = "tenant1:test-key";
            _mockKeyGenerator.Setup(g => g.GenerateKey(testKey)).Returns(tenantKey);
            _mockInnerCache.Setup(c => c.ExistsAsync(tenantKey, _cts.Token))
                .ReturnsAsync(true);

            var result = await _tenantAwareCache.ExistsAsync(testKey, _cts.Token);

            result.Should().BeTrue();
            _mockKeyGenerator.Verify(g => g.GenerateKey(testKey), Times.Once);
            _mockInnerCache.Verify(c => c.ExistsAsync(tenantKey, _cts.Token), Times.Once);
        }

        [Fact]
        public async Task ClearAsync_CallsInnerCacheClear()
        {
            await _tenantAwareCache.ClearAsync(_cts.Token);

            _mockInnerCache.Verify(c => c.ClearAsync(_cts.Token), Times.Once);
        }

        [Fact]
        public async Task GetAsync_WithNullValue_ReturnsNull()
        {
            var testKey = "test-key";
            var tenantKey = "tenant1:test-key";
            _mockKeyGenerator.Setup(g => g.GenerateKey(testKey)).Returns(tenantKey);
            _mockInnerCache.Setup(c => c.GetAsync<string>(tenantKey, _cts.Token))
                .ReturnsAsync((string?)null);

            var result = await _tenantAwareCache.GetAsync<string>(testKey, _cts.Token);

            result.Should().BeNull();
        }

        [Fact]
        public async Task SetAsync_WithoutExpiration_CallsInnerCacheWithNullExpiration()
        {
            var testKey = "test-key";
            var tenantKey = "tenant1:test-key";
            var testValue = "test-value";
            _mockKeyGenerator.Setup(g => g.GenerateKey(testKey)).Returns(tenantKey);

            await _tenantAwareCache.SetAsync(testKey, testValue, null, _cts.Token);

            _mockInnerCache.Verify(c => c.SetAsync(tenantKey, testValue, null, _cts.Token), Times.Once);
        }

        [Fact]
        public async Task CancellationToken_Triggered_PropagatesToInnerCache()
        {
            var testKey = "test-key";
            var tenantKey = "tenant1:test-key";
            _mockKeyGenerator.Setup(g => g.GenerateKey(testKey)).Returns(tenantKey);
            _mockInnerCache.Setup(c => c.GetAsync<string>(tenantKey, _cts.Token))
                .ThrowsAsync(new OperationCanceledException());
            _cts.Cancel();

            var action = () => _tenantAwareCache.GetAsync<string>(testKey, _cts.Token);

            await action.Should().ThrowAsync<OperationCanceledException>();
        }
    }
}
