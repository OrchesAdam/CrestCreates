using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Scheduling.Jobs;
using CrestCreates.Scheduling.Services;
using CrestCreates.Scheduling.Tests.Jobs;
using CrestCreates.Scheduling.Quartz.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.Scheduling.Tests;

public class SchedulingIntegrationTests : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISchedulerService _scheduler;
    private readonly InMemoryJobHistoryRepository _repository;

    public SchedulingIntegrationTests()
    {
        _repository = new InMemoryJobHistoryRepository();
        var services = new ServiceCollection();

        services.AddSingleton<IJobHistoryRepository>(_repository);
        services.AddLogging();
        services.AddSingleton<IJobExecutionHandler>(sp =>
            new DefaultJobExecutionHandler(
                _repository,
                new OptionsWrapper<JobRetryOptions>(new JobRetryOptions { MaxRetries = 3, InitialDelay = TimeSpan.FromMilliseconds(100) })));
        services.AddQuartzScheduling();
        services.AddQuartzJobs();
        services.AddScoped<SuccessJob>();
        services.AddScoped<FailingJob>();
        services.AddScoped<TenantJob>();
        services.AddScoped<DelayedJob>();
        services.AddScoped<RetryableJob>();

        _serviceProvider = services.BuildServiceProvider();
        _scheduler = _serviceProvider.GetRequiredService<ISchedulerService>();
        _scheduler.StartAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _scheduler.StopAsync().GetAwaiter().GetResult();
        if (_serviceProvider is IDisposable d) d.Dispose();
    }

    [Fact]
    public async Task ExecuteNowAsync_OneTimeJob_CompletesSuccessfully()
    {
        // Arrange
        var repository = _repository;
        repository.Clear();

        // Act
        var jobId = await _scheduler.ExecuteNowAsync<SuccessJob>();
        await Task.Delay(500);

        // Assert
        var records = await repository.GetByJobIdAsync(jobId.Uuid);
        var succeededRecords = records.Where(r => r.Result == JobExecutionResult.Succeeded).ToList();
        Assert.Single(succeededRecords);
        Assert.Equal("SuccessJob", succeededRecords[0].JobName);
    }

    [Fact]
    public async Task ExecuteNowAsync_FailingJob_RecordsFailure()
    {
        // Arrange
        var repository = _repository;
        repository.Clear();

        // Act
        var jobId = await _scheduler.ExecuteNowAsync<FailingJob>();
        await Task.Delay(1000); // Wait for execution and retries

        // Assert
        var records = await repository.GetByJobIdAsync(jobId.Uuid);
        var failedRecords = records.Where(r => r.Result == JobExecutionResult.Failed).ToList();

        // Should have at least one failed record (may have more due to retries)
        Assert.NotEmpty(failedRecords);
        Assert.All(failedRecords, r =>
        {
            Assert.Equal("FailingJob", r.JobName);
            Assert.NotNull(r.ErrorMessage);
            Assert.Contains("Test failure", r.ErrorMessage);
        });
    }

    [Fact]
    public async Task ExecuteNowAsync_TenantContextJob_PropagatesContext()
    {
        // Arrange
        var repository = _repository;
        repository.Clear();
        var tenantId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Act
        var jobId = await _scheduler.ExecuteNowAsync<SuccessJob>(tenantId, orgId, userId);
        await Task.Delay(500); // Wait for execution

        // Assert
        var records = await repository.GetByJobIdAsync(jobId.Uuid);
        var succeededRecords = records.Where(r => r.Result == JobExecutionResult.Succeeded).ToList();
        Assert.Single(succeededRecords);
        Assert.Equal(tenantId, succeededRecords[0].TenantId);
        Assert.Equal(orgId, succeededRecords[0].OrganizationId);
        Assert.Equal(userId, succeededRecords[0].UserId);
    }

    [Fact]
    public async Task ExecuteNowAsync_JobWithRetry_RecordsMultipleAttempts()
    {
        // Arrange
        var repository = _repository;
        repository.Clear();

        var retryServices = new ServiceCollection();
        retryServices.AddSingleton<IJobHistoryRepository>(repository);
        retryServices.AddLogging();
        retryServices.AddSingleton<IJobExecutionHandler>(sp =>
            new DefaultJobExecutionHandler(
                repository,
                new OptionsWrapper<JobRetryOptions>(new JobRetryOptions { MaxRetries = 2, InitialDelay = TimeSpan.FromMilliseconds(50) })));
        retryServices.AddQuartzScheduling();
        retryServices.AddQuartzJobs();
        retryServices.AddScoped<RetryableJob>();
        var retryProvider = retryServices.BuildServiceProvider();
        var retryScheduler = retryProvider.GetRequiredService<ISchedulerService>();
        retryScheduler.StartAsync().GetAwaiter().GetResult();

        // Reset static counter
        RetryableJob.Reset();

        // Act
        var jobId = await retryScheduler.ExecuteNowAsync<RetryableJob>();
        await Task.Delay(2000); // Wait for retries (up to 3 attempts)

        retryScheduler.StopAsync().GetAwaiter().GetResult();

        // Assert
        var records = await repository.GetByJobIdAsync(jobId.Uuid);
        var failedRecords = records.Where(r => r.Result == JobExecutionResult.Failed).ToList();
        Assert.True(failedRecords.Count >= 2, $"Expected at least 2 failed attempts, got {failedRecords.Count}");
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsJobRecords()
    {
        // Arrange
        var repository = _repository;
        repository.Clear();
        var jobId = await _scheduler.ExecuteNowAsync<SuccessJob>();
        await Task.Delay(500);

        // Act
        var records = await repository.GetByJobIdAsync(jobId.Uuid);

        // Assert
        Assert.NotEmpty(records);
    }

    [Fact]
    public async Task GetHistoryAsync_FilterByTenant()
    {
        // Arrange
        var repository = _repository;
        repository.Clear();
        var tenantId = Guid.NewGuid();
        await _scheduler.ExecuteNowAsync<SuccessJob>(tenantId);
        await Task.Delay(500);

        // Act
        var records = await repository.GetByTenantAsync(tenantId);

        // Assert
        Assert.NotEmpty(records);
        Assert.All(records, r => Assert.Equal(tenantId, r.TenantId));
    }

    [Fact]
    public async Task ExecuteNowAsync_RecordsStartedAndSucceeded()
    {
        // Arrange
        var repository = _repository;
        repository.Clear();

        // Act
        var jobId = await _scheduler.ExecuteNowAsync<SuccessJob>();
        await Task.Delay(500); // Wait for execution

        // Assert
        var records = (await repository.GetByJobIdAsync(jobId.Uuid)).ToList();
        
        // Should have both Running and Succeeded records
        Assert.Contains(records, r => r.Result == JobExecutionResult.Running);
        Assert.Contains(records, r => r.Result == JobExecutionResult.Succeeded);
    }
}

// Test job implementations
public class SuccessJob : IJob
{
    public Task ExecuteAsync(JobExecutionContext<NoArgs> context, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}

public class FailingJob : IJob
{
    public Task ExecuteAsync(JobExecutionContext<NoArgs> context, CancellationToken ct = default)
    {
        throw new InvalidOperationException("Test failure");
    }
}

public class TenantJob : IJob
{
    public Task ExecuteAsync(JobExecutionContext<NoArgs> context, CancellationToken ct = default)
    {
        Assert.NotNull(context.TenantId);
        Assert.NotNull(context.OrganizationId);
        Assert.NotNull(context.UserId);
        return Task.CompletedTask;
    }
}

public class DelayedJob : IJob
{
    public Task ExecuteAsync(JobExecutionContext<NoArgs> context, CancellationToken ct = default)
    {
        Thread.Sleep(100);
        return Task.CompletedTask;
    }
}

public class RetryableJob : IJob
{
    private static int _attemptCount = 0;

    public static void Reset()
    {
        _attemptCount = 0;
    }

    public Task ExecuteAsync(JobExecutionContext<NoArgs> context, CancellationToken ct = default)
    {
        _attemptCount++;
        if (_attemptCount < 3)
        {
            throw new InvalidOperationException($"Attempt {_attemptCount} failed");
        }
        _attemptCount = 0;
        return Task.CompletedTask;
    }
}
