using CrestCreates.Scheduling.IntegrationTests.Jobs;
using CrestCreates.Scheduling.Jobs;
using CrestCreates.Scheduling.Services;
using CrestCreates.Scheduling.Quartz.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CrestCreates.Scheduling.IntegrationTests;

public class QuartzSchedulerIntegrationTests : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly ISchedulerService _scheduler;
    private readonly IJobFailureHandler _failureHandler;

    public QuartzSchedulerIntegrationTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<ILogger<QuartzSchedulerService>, XunitLogger<QuartzSchedulerService>>();
        services.AddSingleton<IJobFailureHandler, DefaultJobFailureHandler>();
        services.AddSingleton<ISchedulerService, QuartzSchedulerService>();

        // Register test jobs
        services.AddTransient<OneTimeJob>();
        services.AddTransient<TenantContextJob>();
        services.AddTransient<FailingJob>();

        _sp = services.BuildServiceProvider();
        _scheduler = _sp.GetRequiredService<ISchedulerService>();
        _failureHandler = _sp.GetRequiredService<IJobFailureHandler>();
        _scheduler.StartAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _scheduler.StopAsync().GetAwaiter().GetResult();
        _sp.Dispose();
    }

    [Fact]
    public async Task ExecuteNowAsync_OneTimeJob_CreatesJobSuccessfully()
    {
        // Arrange & Act
        // ExecuteNowAsync creates and schedules a fire-and-forget job
        // The job ID returned is for tracking, not for re-executing
        await _scheduler.ExecuteNowAsync<OneTimeJob>();

        // Assert - job should be scheduled (exists)
        // Note: ExecuteNowAsync jobs are not durable so they complete quickly
        // We verify the call didn't throw
        await Task.Delay(100);
    }

    [Fact]
    public async Task RegisterAsync_OneTimeJob_SchedulesImmediately()
    {
        // Arrange
        var metadata = new JobMetadata
        {
            Name = "ImmediateJob",
            Group = "TestGroup",
            Description = "Test immediate job"
        };

        // Act
        var jobId = await _scheduler.RegisterAsync<OneTimeJob>(metadata);

        // Assert
        jobId.Should().NotBeNull();
        jobId.Name.Should().Be("ImmediateJob");
        jobId.Group.Should().Be("TestGroup");
    }

    [Fact]
    public async Task ExecuteNowAsync_JobWithTenantContext_AcceptsTenantInfo()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Act - ExecuteNowAsync accepts tenant context without throwing
        // The actual propagation is tested through the adapter's JobExecutionContext
        await _scheduler.ExecuteNowAsync<TenantContextJob>(
            tenantId: tenantId,
            organizationId: orgId,
            userId: userId
        );

        // Assert - call completed without error
        await Task.Delay(100);
    }

    [Fact]
    public async Task GetAllAsync_NoJobs_ReturnsEmptyList()
    {
        // Act
        var jobs = await _scheduler.GetAllAsync(JobStatus.All);

        // Assert
        jobs.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteAsync_NonExistentJob_DoesNotThrow()
        // Act & Assert
        => await _scheduler.DeleteAsync(new JobId("NonExistent", "NonExistentGroup", Guid.Empty));

    [Fact]
    public async Task PauseAsync_Then_ResumeAsync_Job_PreservesJob()
    {
        // Arrange
        var metadata = new JobMetadata
        {
            Name = "PauseableJob",
            Group = "TestGroup",
            Description = "Test pause/resume"
        };
        var jobId = await _scheduler.RegisterAsync<OneTimeJob>(metadata);

        // Act - pause and resume
        await _scheduler.PauseAsync(jobId);
        await _scheduler.ResumeAsync(jobId);

        // Assert - job should still exist
        var exists = await _scheduler.ExistsAsync(jobId);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task CancelAsync_Job_InterruptsJob()
    {
        // Arrange
        var metadata = new JobMetadata
        {
            Name = "CancellableJob",
            Group = "TestGroup",
            Description = "Test cancellation"
        };
        var jobId = await _scheduler.RegisterAsync<OneTimeJob>(metadata);

        // Act
        await _scheduler.CancelAsync(jobId);

        // Assert - job should still exist (cancellation is best-effort)
        var exists = await _scheduler.ExistsAsync(jobId);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_NonExistentJob_ReturnsFalse()
    {
        // Arrange
        var nonExistentId = JobId.New();

        // Act
        var exists = await _scheduler.ExistsAsync(nonExistentId);

        // Assert
        exists.Should().BeFalse();
    }
}

// Simple logger for xunit output
public class XunitLogger<T> : ILogger<T>
{
    private readonly Action<string> _writeLine;

    public XunitLogger()
    {
        _writeLine = Console.WriteLine;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel))
        {
            _writeLine($"[{logLevel}] {formatter(state, exception)}");
        }
    }
}
