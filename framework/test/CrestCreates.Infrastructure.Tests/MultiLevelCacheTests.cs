using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;
using FluentAssertions;
using CrestCreates.Infrastructure.Caching.MultiLevel;

namespace CrestCreates.Infrastructure.Tests
{
    public class MultiLevelCacheTests : IDisposable
    {
        private readonly MemoryCache _l1Cache;
        private readonly Mock<IDatabase> _mockRedisDatabase;
        private readonly Mock<IConnectionMultiplexer> _mockRedisConnection;
        private readonly Mock<ILogger<MultiLevelCache>> _mockLogger;
        private readonly Mock<IOptions<MultiLevelCacheOptions>> _mockOptions;
        private readonly MultiLevelCacheOptions _options;
        private readonly CancellationTokenSource _cts;

        public MultiLevelCacheTests()
        {
            _l1Cache = new MemoryCache(new MemoryCacheOptions());
            _mockRedisDatabase = new Mock<IDatabase>();
            _mockRedisConnection = new Mock<IConnectionMultiplexer>();
            _mockLogger = new Mock<ILogger<MultiLevelCache>>();
            _mockOptions = new Mock<IOptions<MultiLevelCacheOptions>>();
            _options = new MultiLevelCacheOptions
            {
                L1Expiration = TimeSpan.FromMinutes(30),
                L2Expiration = TimeSpan.FromHours(1),
                EnableL1Sync = false
            };
            _mockOptions.Setup(o => o.Value).Returns(_options);
            _mockRedisConnection.Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(_mockRedisDatabase.Object);
            _cts = new CancellationTokenSource();
        }

        public void Dispose()
        {
            _l1Cache.Dispose();
            _cts.Dispose();
        }

        private MultiLevelCache CreateMultiLevelCache(CacheSynchronizer? synchronizer = null)
        {
            return new MultiLevelCache(
                _l1Cache,
                _mockRedisConnection.Object,
                _mockOptions.Object,
                _mockLogger.Object,
                synchronizer);
        }

        [Fact]
        public void Constructor_WithNullL1Cache_ThrowsArgumentNullException()
        {
            var action = () => new MultiLevelCache(
                null!,
                _mockRedisConnection.Object,
                _mockOptions.Object,
                _mockLogger.Object);

            action.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("l1Cache");
        }

        [Fact]
        public void Constructor_WithNullRedisConnection_ThrowsArgumentNullException()
        {
            var action = () => new MultiLevelCache(
                _l1Cache,
                null!,
                _mockOptions.Object,
                _mockLogger.Object);

            action.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("redisConnection");
        }

        [Fact]
        public void Constructor_WithNullOptions_ThrowsArgumentNullException()
        {
            var action = () => new MultiLevelCache(
                _l1Cache,
                _mockRedisConnection.Object,
                null!,
                _mockLogger.Object);

            action.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("options");
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            var action = () => new MultiLevelCache(
                _l1Cache,
                _mockRedisConnection.Object,
                _mockOptions.Object,
                null!);

            action.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("logger");
        }

        [Fact]
        public async Task GetAsync_L1CacheHit_ReturnsValueFromL1()
        {
            var cache = CreateMultiLevelCache();
            var testKey = "test-key";
            var testValue = "test-value";
            _l1Cache.Set(testKey, testValue);

            var result = await cache.GetAsync<string>(testKey, _cts.Token);

            result.Should().Be(testValue);
            _mockRedisDatabase.Verify(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()),
                Times.Never);
        }

        [Fact]
        public async Task GetAsync_L1MissL2Hit_ReturnsValueFromL2AndBackfillsL1()
        {
            var cache = CreateMultiLevelCache();
            var testKey = "test-key";
            var testValue = "test-value";
            var redisValue = RedisValue.Unbox(System.Text.Json.JsonSerializer.Serialize(testValue));
            _mockRedisDatabase.Setup(d => d.StringGetAsync(It.Is<RedisKey>(k => k == testKey), It.IsAny<CommandFlags>()))
                .ReturnsAsync(redisValue);

            var result = await cache.GetAsync<string>(testKey, _cts.Token);

            result.Should().Be(testValue);
            _l1Cache.Get<string>(testKey).Should().Be(testValue);
        }

        [Fact]
        public async Task GetAsync_BothMisses_ReturnsDefault()
        {
            var cache = CreateMultiLevelCache();
            var testKey = "test-key";
            _mockRedisDatabase.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);

            var result = await cache.GetAsync<string>(testKey, _cts.Token);

            result.Should().BeNull();
        }

        [Fact]
        public async Task GetAsync_L2Throws_StillReturnsDefault()
        {
            var cache = CreateMultiLevelCache();
            var testKey = "test-key";
            _mockRedisDatabase.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new Exception("Redis error"));

            var result = await cache.GetAsync<string>(testKey, _cts.Token);

            result.Should().BeNull();
        }

        [Fact]
        public async Task SetAsync_SetsBothL1AndL2()
        {
            var cache = CreateMultiLevelCache();
            var testKey = "test-key";
            var testValue = "test-value";

            await cache.SetAsync(testKey, testValue, TimeSpan.FromMinutes(10), _cts.Token);

            _l1Cache.Get<string>(testKey).Should().Be(testValue);
            _mockRedisDatabase.Verify(d => d.StringSetAsync(
                It.Is<RedisKey>(k => k == testKey),
                It.Is<RedisValue>(v => v.HasValue),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()),
                Times.Once);
        }

        [Fact]
        public async Task SetAsync_WithNullValue_DoesNothing()
        {
            var cache = CreateMultiLevelCache();
            var testKey = "test-key";

            await cache.SetAsync<string>(testKey, null!, null, _cts.Token);

            _mockRedisDatabase.Verify(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()),
                Times.Never);
        }

        [Fact]
        public async Task RemoveAsync_RemovesFromBothL1AndL2()
        {
            var cache = CreateMultiLevelCache();
            var testKey = "test-key";
            _l1Cache.Set(testKey, "value");

            await cache.RemoveAsync(testKey, _cts.Token);

            _l1Cache.TryGetValue(testKey, out _).Should().BeFalse();
            _mockRedisDatabase.Verify(d => d.KeyDeleteAsync(
                It.Is<RedisKey>(k => k == testKey),
                It.IsAny<CommandFlags>()),
                Times.Once);
        }

        [Fact]
        public async Task ExistsAsync_L1Exists_ReturnsTrue()
        {
            var cache = CreateMultiLevelCache();
            var testKey = "test-key";
            _l1Cache.Set(testKey, "value");

            var result = await cache.ExistsAsync(testKey, _cts.Token);

            result.Should().BeTrue();
        }

        [Fact]
        public async Task ExistsAsync_L1NotExistsL2Exists_ReturnsTrue()
        {
            var cache = CreateMultiLevelCache();
            var testKey = "test-key";
            _mockRedisDatabase.Setup(d => d.KeyExistsAsync(
                It.Is<RedisKey>(k => k == testKey),
                It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var result = await cache.ExistsAsync(testKey, _cts.Token);

            result.Should().BeTrue();
        }

        [Fact]
        public void RemoveFromL1_RemovesFromL1()
        {
            var cache = CreateMultiLevelCache();
            var testKey = "test-key";
            _l1Cache.Set(testKey, "value");

            cache.RemoveFromL1(testKey);

            _l1Cache.TryGetValue(testKey, out _).Should().BeFalse();
        }

        [Fact]
        public async Task CancellationToken_Triggered_ThrowsOperationCanceledException()
        {
            var cache = CreateMultiLevelCache();
            _cts.Cancel();

            var action = () => cache.GetAsync<string>("test-key", _cts.Token);

            await action.Should().ThrowAsync<OperationCanceledException>();
        }
    }
}
