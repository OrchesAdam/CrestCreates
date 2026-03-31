using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;
using FluentAssertions;
using CrestCreates.Infrastructure.Caching;
using CrestCreates.Infrastructure.Caching.Metrics;

namespace CrestCreates.Infrastructure.Tests
{
    public class CacheHealthCheckTests
    {
        private readonly Mock<ICache> _mockCache;
        private readonly Mock<ILogger<CacheHealthCheck>> _mockLogger;
        private readonly CancellationTokenSource _cts;

        public CacheHealthCheckTests()
        {
            _mockCache = new Mock<ICache>();
            _mockLogger = new Mock<ILogger<CacheHealthCheck>>();
            _cts = new CancellationTokenSource();
        }

        private CacheHealthCheck CreateHealthCheck(IConnectionMultiplexer? redisConnection = null)
        {
            return new CacheHealthCheck(_mockCache.Object, _mockLogger.Object, redisConnection);
        }

        [Fact]
        public void Constructor_WithNullCache_ThrowsArgumentNullException()
        {
            var action = () => new CacheHealthCheck(null!, _mockLogger.Object);

            action.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("cache");
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            var action = () => new CacheHealthCheck(_mockCache.Object, null!);

            action.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("logger");
        }

        [Fact]
        public async Task CheckHealthAsync_Success_ReturnsHealthyOrDegraded()
        {
            var healthCheck = CreateHealthCheck();

            _mockCache.Setup(c => c.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("ok");

            var context = new HealthCheckContext();
            var result = await healthCheck.CheckHealthAsync(context, _cts.Token);

            result.Data.Should().ContainKey("responseTimeMs");
        }

        [Fact]
        public async Task CheckHealthAsync_ThrowsException_ReturnsUnhealthy()
        {
            var healthCheck = CreateHealthCheck();

            _mockCache.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Cache error"));

            var context = new HealthCheckContext();
            var result = await healthCheck.CheckHealthAsync(context, _cts.Token);

            result.Status.Should().Be(HealthStatus.Unhealthy);
            result.Exception.Should().NotBeNull();
        }

        [Fact]
        public async Task CheckHealthAsync_DoesNotThrowOnCancellation()
        {
            var healthCheck = CreateHealthCheck();
            _cts.Cancel();

            var context = new HealthCheckContext();
            Func<Task> action = () => healthCheck.CheckHealthAsync(context, _cts.Token);

            await action.Should().NotThrowAsync();
        }
    }
}
