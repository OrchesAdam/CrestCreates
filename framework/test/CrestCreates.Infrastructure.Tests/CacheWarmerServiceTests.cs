using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using CrestCreates.Infrastructure.Caching.Advanced;

namespace CrestCreates.Infrastructure.Tests
{
    public class CacheWarmerServiceTests
    {
        private readonly Mock<ILogger<CacheWarmerService>> _mockLogger;
        private readonly CancellationTokenSource _cts;

        public CacheWarmerServiceTests()
        {
            _mockLogger = new Mock<ILogger<CacheWarmerService>>();
            _cts = new CancellationTokenSource();
        }

        [Fact]
        public void Constructor_WithNullWarmers_ThrowsArgumentNullException()
        {
            var action = () => new CacheWarmerService(null!, _mockLogger.Object);

            action.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("warmers");
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            var warmers = Enumerable.Empty<ICacheWarmer>();

            var action = () => new CacheWarmerService(warmers, null!);

            action.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("logger");
        }

        [Fact]
        public async Task ExecuteAsync_WithMultipleWarmers_ExecutesInPriorityOrder()
        {
            var executionOrder = new List<string>();
            var warmer1 = CreateMockWarmer("Warmer1", 2, () => { executionOrder.Add("Warmer1"); });
            var warmer2 = CreateMockWarmer("Warmer2", 1, () => { executionOrder.Add("Warmer2"); });
            var warmer3 = CreateMockWarmer("Warmer3", 3, () => { executionOrder.Add("Warmer3"); });
            var warmers = new[] { warmer1.Object, warmer2.Object, warmer3.Object };
            var service = new CacheWarmerService(warmers, _mockLogger.Object);

            await service.StartAsync(_cts.Token);
            await Task.Delay(100);
            await service.StopAsync(_cts.Token);

            executionOrder.Should().Equal("Warmer2", "Warmer1", "Warmer3");
        }

        [Fact]
        public async Task ExecuteAsync_WithEmptyWarmers_DoesNothing()
        {
            var warmers = Enumerable.Empty<ICacheWarmer>();
            var service = new CacheWarmerService(warmers, _mockLogger.Object);

            Func<Task> action = () => service.StartAsync(_cts.Token);

            await action.Should().NotThrowAsync();
        }

        [Fact]
        public async Task ExecuteAsync_WarmerThrowsException_ContinuesWithOtherWarmers()
        {
            var executionOrder = new List<string>();
            var failingWarmer = CreateMockWarmer("FailingWarmer", 1, () => { throw new Exception("Warmer failed"); });
            var successfulWarmer = CreateMockWarmer("SuccessfulWarmer", 2, () => { executionOrder.Add("SuccessfulWarmer"); });
            var warmers = new[] { failingWarmer.Object, successfulWarmer.Object };
            var service = new CacheWarmerService(warmers, _mockLogger.Object);

            await service.StartAsync(_cts.Token);
            await Task.Delay(100);
            await service.StopAsync(_cts.Token);

            executionOrder.Should().Contain("SuccessfulWarmer");
        }

        [Fact]
        public async Task ExecuteAsync_CancellationTokenCancelled_StopsExecution()
        {
            var tcs = new TaskCompletionSource<bool>();
            var longRunningWarmer = CreateMockWarmer("LongRunning", 1, async () =>
            {
                await tcs.Task;
            });
            var warmers = new[] { longRunningWarmer.Object };
            var service = new CacheWarmerService(warmers, _mockLogger.Object);

            await service.StartAsync(_cts.Token);
            await Task.Delay(50);
            _cts.Cancel();
            tcs.SetResult(true);
            await Task.Delay(50);

            longRunningWarmer.Verify(w => w.WarmUpAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        private Mock<ICacheWarmer> CreateMockWarmer(string name, int priority, Action? action = null)
        {
            var mock = new Mock<ICacheWarmer>();
            mock.Setup(w => w.Name).Returns(name);
            mock.Setup(w => w.Priority).Returns(priority);
            mock.Setup(w => w.WarmUpAsync(It.IsAny<CancellationToken>()))
                .Returns<CancellationToken>(ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    action?.Invoke();
                    return Task.CompletedTask;
                });
            return mock;
        }
    }
}
