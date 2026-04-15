using System;
using System.Threading.Tasks;
using CrestCreates.Scheduling.Jobs;
using CrestCreates.Scheduling.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CrestCreates.Scheduling.Tests;

public class JobFailureHandlerTests
{
    private readonly Mock<ILogger<DefaultJobFailureHandler>> _loggerMock;
    private readonly JobFailureContext _context;

    public JobFailureHandlerTests()
    {
        _loggerMock = new Mock<ILogger<DefaultJobFailureHandler>>();
        _context = new JobFailureContext(
            JobId: JobId.New(),
            JobType: typeof(TestJob),
            ArgType: typeof(TestJobArgs),
            Exception: new InvalidOperationException("Test error"),
            TenantId: Guid.NewGuid(),
            OrganizationId: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            FailedAt: DateTimeOffset.UtcNow,
            Args: new TestJobArgs { Value = 1 },
            AttemptNumber: 1
        );
    }

    [Fact]
    public async Task HandleAsync_Should_LogError()
    {
        // Arrange
        var handler = new DefaultJobFailureHandler(_loggerMock.Object);

        // Act
        await handler.HandleAsync(_context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                _context.Exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void ShouldRetry_WithNoRetryOptions_ShouldReturnFalse()
    {
        // Arrange
        var handler = new DefaultJobFailureHandler(_loggerMock.Object);

        // Act
        var result = handler.ShouldRetry(_context);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldRetry_WithMaxRetriesZero_ShouldReturnFalse()
    {
        // Arrange
        var options = Options.Create(new JobRetryOptions { MaxRetries = 0 });
        var handler = new DefaultJobFailureHandler(_loggerMock.Object, options);

        // Act
        var result = handler.ShouldRetry(_context);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldRetry_WithRemainingRetries_ShouldReturnTrue()
    {
        // Arrange
        var options = Options.Create(new JobRetryOptions { MaxRetries = 3 });
        var handler = new DefaultJobFailureHandler(_loggerMock.Object, options);
        var context = _context with { AttemptNumber = 1 };

        // Act
        var result = handler.ShouldRetry(context);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRetry_WhenAttemptExceedsMaxRetries_ShouldReturnFalse()
    {
        // Arrange
        var options = Options.Create(new JobRetryOptions { MaxRetries = 3 });
        var handler = new DefaultJobFailureHandler(_loggerMock.Object, options);
        var context = _context with { AttemptNumber = 3 };

        // Act
        var result = handler.ShouldRetry(context);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void GetNextRetryDelay_WithNoRetryOptions_ShouldReturnNull()
    {
        // Arrange
        var handler = new DefaultJobFailureHandler(_loggerMock.Object);

        // Act
        var result = handler.GetNextRetryDelay(_context, 1);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetNextRetryDelay_WithInitialDelay_ShouldReturnExponentialDelay()
    {
        // Arrange
        var options = Options.Create(new JobRetryOptions
        {
            InitialDelay = TimeSpan.FromSeconds(1),
            BackoffMultiplier = 2.0
        });
        var handler = new DefaultJobFailureHandler(_loggerMock.Object, options);

        // Act
        var delay1 = handler.GetNextRetryDelay(_context, 1);
        var delay2 = handler.GetNextRetryDelay(_context, 2);
        var delay3 = handler.GetNextRetryDelay(_context, 3);

        // Assert
        delay1.Should().Be(TimeSpan.FromSeconds(1));   // 1 * 2^0 = 1
        delay2.Should().Be(TimeSpan.FromSeconds(2));   // 1 * 2^1 = 2
        delay3.Should().Be(TimeSpan.FromSeconds(4));   // 1 * 2^2 = 4
    }

    [Fact]
    public void GetNextRetryDelay_WithMaxDelay_ShouldCapDelay()
    {
        // Arrange
        var options = Options.Create(new JobRetryOptions
        {
            InitialDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(5),
            BackoffMultiplier = 2.0
        });
        var handler = new DefaultJobFailureHandler(_loggerMock.Object, options);

        // Act
        var delay = handler.GetNextRetryDelay(_context, 10);

        // Assert
        delay.Should().Be(TimeSpan.FromSeconds(5)); // Capped at MaxDelay
    }

    [Fact]
    public void GetNextRetryDelay_WithMaxDelayNotReached_ShouldReturnFullDelay()
    {
        // Arrange
        var options = Options.Create(new JobRetryOptions
        {
            InitialDelay = TimeSpan.FromSeconds(2),
            MaxDelay = TimeSpan.FromSeconds(10),
            BackoffMultiplier = 2.0
        });
        var handler = new DefaultJobFailureHandler(_loggerMock.Object, options);

        // Act
        var delay = handler.GetNextRetryDelay(_context, 2);

        // Assert
        delay.Should().Be(TimeSpan.FromSeconds(4)); // 2 * 2^1 = 4, less than max
    }

    private class TestJob : IJob<NoArgs>
    {
        public Task ExecuteAsync(JobExecutionContext<NoArgs> context, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private record TestJobArgs : IJobArgs
    {
        public int Value { get; init; }
    }
}
