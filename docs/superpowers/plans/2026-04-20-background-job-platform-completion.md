# Background Job Platform Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the Background Job Platform by adding job execution history persistence and comprehensive integration test coverage.

**Architecture:** Add JobRecord entity and IJobHistoryRepository for persistence. Extend IJobExecutionHandler with lifecycle hooks (scheduled, started, succeeded, cancelled) that record to the repository. Integrate hooks into existing QuartzJobAdapter.

**Tech Stack:** .NET 10, Quartz, xUnit, In-Memory Repository Pattern

---

## File Structure

```
framework/src/CrestCreates.Scheduling/
├── Jobs/
│   └── JobRecord.cs                    # NEW: Execution record entity
└── Services/
    ├── IJobExecutionHandler.cs         # NEW: Extended handler with lifecycle hooks
    ├── IJobHistoryRepository.cs         # NEW: Repository interface
    └── DefaultJobExecutionHandler.cs   # NEW: Default implementation

framework/src/CrestCreates.Scheduling.Quartz/
├── Jobs/
│   └── QuartzJobAdapter.cs             # MODIFIED: Add hook calls
└── Services/
    └── QuartzSchedulerService.cs        # MODIFIED: Call OnJobScheduledAsync

framework/test/CrestCreates.Scheduling.Tests/
├── Jobs/
│   └── InMemoryJobHistoryRepository.cs # NEW: In-memory repository for tests
└── SchedulingIntegrationTests.cs       # NEW: Integration tests
```

---

## Task 1: Create JobRecord Entity

**Files:**
- Create: `framework/src/CrestCreates.Scheduling/Jobs/JobRecord.cs`

- [ ] **Step 1: Create JobRecord.cs**

```csharp
namespace CrestCreates.Scheduling.Jobs;

public interface IJobRecord
{
    Guid Id { get; }
    string JobName { get; }
    string? JobGroup { get; }
    Guid JobUuid { get; }
    string? CronExpression { get; }
    JobExecutionResult Result { get; }
    DateTimeOffset CreatedAt { get; }
    DateTimeOffset? StartedAt { get; }
    DateTimeOffset? FinishedAt { get; }
    TimeSpan? Duration { get; }
    Guid? TenantId { get; }
    Guid? OrganizationId { get; }
    Guid? UserId { get; }
    string? ArgsJson { get; }
    int AttemptNumber { get; }
    string? ErrorMessage { get; }
    string? StackTrace { get; }
}

public enum JobExecutionResult
{
    Scheduled = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4
}

public class JobRecord : IJobRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string JobName { get; init; }
    public string? JobGroup { get; init; }
    public Guid JobUuid { get; init; }
    public string? CronExpression { get; init; }
    public JobExecutionResult Result { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public TimeSpan? Duration => FinishedAt - StartedAt;
    public Guid? TenantId { get; init; }
    public Guid? OrganizationId { get; init; }
    public Guid? UserId { get; init; }
    public string? ArgsJson { get; init; }
    public int AttemptNumber { get; init; } = 1;
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
}
```

- [ ] **Step 2: Add System.Text.Json using**

Add `using System.Text.Json;` at the top of the file (needed for ArgsJson property pattern).

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Scheduling/Jobs/JobRecord.cs
git commit -m "feat(Scheduling): add JobRecord entity for execution history tracking"
```

---

## Task 2: Create IJobExecutionHandler Interface

**Files:**
- Create: `framework/src/CrestCreates.Scheduling/Services/IJobExecutionHandler.cs`

- [ ] **Step 1: Create IJobExecutionHandler.cs**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Scheduling.Jobs;

namespace CrestCreates.Scheduling.Services;

public interface IJobExecutionHandler : IJobFailureHandler
{
    Task OnJobScheduledAsync(JobScheduledContext context, CancellationToken ct = default);
    Task OnJobStartedAsync(JobStartedContext context, CancellationToken ct = default);
    Task OnJobSucceededAsync(JobSucceededContext context, CancellationToken ct = default);
    Task OnJobCancelledAsync(JobCancelledContext context, CancellationToken ct = default);
}

public record JobScheduledContext(
    Guid JobId,
    Type JobType,
    Type? ArgType,
    Guid? TenantId,
    Guid? OrganizationId,
    Guid? UserId,
    string? ArgsJson,
    DateTimeOffset ScheduledAt
);

public record JobStartedContext(
    Guid JobId,
    Type JobType,
    Type? ArgType,
    Guid? TenantId,
    Guid? OrganizationId,
    Guid? UserId,
    string? ArgsJson,
    int AttemptNumber,
    DateTimeOffset StartedAt
);

public record JobSucceededContext(
    Guid JobId,
    Type JobType,
    Type? ArgType,
    Guid? TenantId,
    Guid? OrganizationId,
    Guid? UserId,
    string? ArgsJson,
    int AttemptNumber,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    TimeSpan Duration
);

public record JobCancelledContext(
    Guid JobId,
    Type JobType,
    Type? ArgType,
    Guid? TenantId,
    Guid? OrganizationId,
    Guid? UserId,
    string? ArgsJson,
    int AttemptNumber,
    DateTimeOffset CancelledAt
);
```

- [ ] **Step 2: Commit**

```bash
git add framework/src/CrestCreates.Scheduling/Services/IJobExecutionHandler.cs
git commit -m "feat(Scheduling): add IJobExecutionHandler with lifecycle hooks"
```

---

## Task 3: Create IJobHistoryRepository Interface

**Files:**
- Create: `framework/src/CrestCreates.Scheduling/Services/IJobHistoryRepository.cs`

- [ ] **Step 1: Create IJobHistoryRepository.cs**

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Scheduling.Jobs;

namespace CrestCreates.Scheduling.Services;

public interface IJobHistoryRepository
{
    Task<IJobRecord> CreateAsync(IJobRecord record, CancellationToken ct = default);
    Task UpdateAsync(IJobRecord record, CancellationToken ct = default);
    Task<IJobRecord?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<IJobRecord>> GetByJobIdAsync(Guid jobUuid, int limit = 100, CancellationToken ct = default);
    Task<IEnumerable<IJobRecord>> GetByTenantAsync(Guid tenantId, int limit = 100, CancellationToken ct = default);
}
```

- [ ] **Step 2: Commit**

```bash
git add framework/src/CrestCreates.Scheduling/Services/IJobHistoryRepository.cs
git commit -m "feat(Scheduling): add IJobHistoryRepository for execution history persistence"
```

---

## Task 4: Create DefaultJobExecutionHandler

**Files:**
- Create: `framework/src/CrestCreates.Scheduling/Services/DefaultJobExecutionHandler.cs`

- [ ] **Step 1: Create DefaultJobExecutionHandler.cs**

```csharp
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Scheduling.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.Scheduling.Services;

public class DefaultJobExecutionHandler : IJobExecutionHandler
{
    private readonly IJobHistoryRepository _historyRepository;
    private readonly JobRetryOptions? _retryOptions;
    private readonly ILogger<DefaultJobExecutionHandler> _logger;

    public DefaultJobExecutionHandler(
        IJobHistoryRepository historyRepository,
        IOptions<JobRetryOptions>? retryOptions = null,
        ILogger<DefaultJobExecutionHandler>? logger = null)
    {
        _historyRepository = historyRepository;
        _retryOptions = retryOptions?.Value;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DefaultJobExecutionHandler>.Instance;
    }

    public Task OnJobScheduledAsync(JobScheduledContext context, CancellationToken ct = default)
    {
        var record = new JobRecord
        {
            JobName = context.JobType.Name,
            JobUuid = context.JobId,
            TenantId = context.TenantId,
            OrganizationId = context.OrganizationId,
            UserId = context.UserId,
            ArgsJson = context.ArgsJson,
            Result = JobExecutionResult.Scheduled,
            AttemptNumber = 1
        };
        _logger.LogDebug("Job {JobId} scheduled", context.JobId);
        return _historyRepository.CreateAsync(record, ct);
    }

    public Task OnJobStartedAsync(JobStartedContext context, CancellationToken ct = default)
    {
        var record = new JobRecord
        {
            JobName = context.JobType.Name,
            JobUuid = context.JobId,
            TenantId = context.TenantId,
            OrganizationId = context.OrganizationId,
            UserId = context.UserId,
            ArgsJson = context.ArgsJson,
            Result = JobExecutionResult.Running,
            AttemptNumber = context.AttemptNumber,
            StartedAt = context.StartedAt
        };
        _logger.LogDebug("Job {JobId} started (attempt {Attempt})", context.JobId, context.AttemptNumber);
        return _historyRepository.CreateAsync(record, ct);
    }

    public Task OnJobSucceededAsync(JobSucceededContext context, CancellationToken ct = default)
    {
        var record = new JobRecord
        {
            JobName = context.JobType.Name,
            JobUuid = context.JobId,
            TenantId = context.TenantId,
            OrganizationId = context.OrganizationId,
            UserId = context.UserId,
            ArgsJson = context.ArgsJson,
            Result = JobExecutionResult.Succeeded,
            AttemptNumber = context.AttemptNumber,
            StartedAt = context.StartedAt,
            FinishedAt = context.FinishedAt,
            Duration = context.Duration
        };
        _logger.LogInformation("Job {JobId} succeeded in {Duration}", context.JobId, context.Duration);
        return _historyRepository.CreateAsync(record, ct);
    }

    public Task OnJobCancelledAsync(JobCancelledContext context, CancellationToken ct = default)
    {
        var record = new JobRecord
        {
            JobName = context.JobType.Name,
            JobUuid = context.JobId,
            TenantId = context.TenantId,
            OrganizationId = context.OrganizationId,
            UserId = context.UserId,
            ArgsJson = context.ArgsJson,
            Result = JobExecutionResult.Cancelled,
            AttemptNumber = context.AttemptNumber
        };
        _logger.LogWarning("Job {JobId} cancelled (attempt {Attempt})", context.JobId, context.AttemptNumber);
        return _historyRepository.CreateAsync(record, ct);
    }

    public Task HandleAsync(JobFailureContext context, CancellationToken ct = default)
    {
        var record = new JobRecord
        {
            JobName = context.JobType.Name,
            JobUuid = context.JobId,
            TenantId = context.TenantId,
            OrganizationId = context.OrganizationId,
            UserId = context.UserId,
            ArgsJson = context.Args is IJobArgs args ? JsonSerializer.Serialize(args) : null,
            Result = JobExecutionResult.Failed,
            AttemptNumber = context.AttemptNumber,
            ErrorMessage = context.Exception.Message,
            StackTrace = context.Exception.StackTrace
        };
        _logger.LogError(context.Exception, "Job {JobId} failed (attempt {Attempt})", context.JobId, context.AttemptNumber);
        return _historyRepository.CreateAsync(record, ct);
    }

    public bool ShouldRetry(JobFailureContext context)
        => _retryOptions?.MaxRetries > 0 && context.AttemptNumber < _retryOptions.MaxRetries;

    public TimeSpan? GetNextRetryDelay(JobFailureContext context, int attemptNumber)
    {
        if (_retryOptions?.InitialDelay == null) return null;
        var delay = _retryOptions.InitialDelay.Value * Math.Pow(_retryOptions.BackoffMultiplier, attemptNumber - 1);
        return _retryOptions.MaxDelay.HasValue
            ? TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds, _retryOptions.MaxDelay.TotalMilliseconds))
            : delay;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add framework/src/CrestCreates.Scheduling/Services/DefaultJobExecutionHandler.cs
git commit -m "feat(Scheduling): add DefaultJobExecutionHandler implementation"
```

---

## Task 5: Update QuartzJobAdapter to Call Execution Hooks

**Files:**
- Modify: `framework/src/CrestCreates.Scheduling.Quartz/Jobs/QuartzJobAdapter.cs`

- [ ] **Step 1: Update QuartzJobAdapter to use IJobExecutionHandler**

Replace the existing adapter with updated version:

```csharp
using System.Text.Json;
using CrestCreates.Scheduling.Jobs;
using CrestCreates.Scheduling.Services;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using QuartzJob = Quartz.IJob;

namespace CrestCreates.Scheduling.Quartz.Jobs;

internal class QuartzJobAdapter<TJob, TArg> : QuartzJob
    where TJob : CrestCreates.Scheduling.Jobs.IJob<TArg>
    where TArg : IJobArgs
{
    private readonly IServiceProvider _serviceProvider;

    public QuartzJobAdapter(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    async Task QuartzJob.Execute(IJobExecutionContext context)
    {
        using var scope = _serviceProvider.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<TJob>();
        var handler = scope.ServiceProvider.GetRequiredService<IJobExecutionHandler>();

        var scheduledAt = context.ScheduledFireTimeUtc ?? DateTimeOffset.UtcNow;
        var startedAt = DateTimeOffset.UtcNow;
        var argsJson = context.MergedJobDataMap.GetString("Args");
        var args = argsJson != null ? JsonSerializer.Deserialize<TArg>(argsJson) : default;
        var tenantIdStr = context.MergedJobDataMap.GetString("TenantId");
        var organizationIdStr = context.MergedJobDataMap.GetString("OrganizationId");
        var userIdStr = context.MergedJobDataMap.GetString("UserId");
        var attemptNumber = context.MergedJobDataMap.ContainsKey("AttemptNumber")
            ? context.MergedJobDataMap.GetIntValue("AttemptNumber")
            : 1;

        var tenantId = string.IsNullOrEmpty(tenantIdStr) ? null : Guid.Parse(tenantIdStr);
        var organizationId = string.IsNullOrEmpty(organizationIdStr) ? null : Guid.Parse(organizationIdStr);
        var userId = string.IsNullOrEmpty(userIdStr) ? null : Guid.Parse(userIdStr);
        var jobId = new JobId(context.JobDetail.Key.Name, context.JobDetail.Key.Group, Guid.Parse(context.FireInstanceId));

        var jobContext = new JobExecutionContext<TArg>(
            Args: args!,
            JobId: jobId,
            TenantId: tenantId,
            OrganizationId: organizationId,
            UserId: userId,
            ScheduledAt: scheduledAt,
            CancellationToken: context.CancellationToken
        );

        // Record job started
        await handler.OnJobStartedAsync(new JobStartedContext(
            jobId.Uuid,
            typeof(TJob),
            typeof(TArg),
            tenantId,
            organizationId,
            userId,
            argsJson,
            attemptNumber,
            startedAt
        ), context.CancellationToken);

        try
        {
            await job.ExecuteAsync(jobContext, context.CancellationToken);

            var finishedAt = DateTimeOffset.UtcNow;
            await handler.OnJobSucceededAsync(new JobSucceededContext(
                jobId.Uuid,
                typeof(TJob),
                typeof(TArg),
                tenantId,
                organizationId,
                userId,
                argsJson,
                attemptNumber,
                startedAt,
                finishedAt,
                finishedAt - startedAt
            ), context.CancellationToken);
        }
        catch (Exception ex)
        {
            var failureContext = new JobFailureContext(
                jobId,
                typeof(TJob),
                typeof(TArg),
                ex,
                tenantId,
                organizationId,
                userId,
                DateTimeOffset.UtcNow,
                args,
                attemptNumber
            );

            await handler.HandleAsync(failureContext, context.CancellationToken);

            if (handler.ShouldRetry(failureContext))
            {
                var delay = handler.GetNextRetryDelay(failureContext, attemptNumber);
                context.MergedJobDataMap.Put("AttemptNumber", attemptNumber + 1);

                var trigger = TriggerBuilder.Create()
                    .StartAt(DateTimeOffset.UtcNow.Add(delay ?? TimeSpan.Zero))
                    .Build();

                await context.Scheduler.RescheduleJob(context.Trigger.Key, trigger);
            }

            throw;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add framework/src/CrestCreates.Scheduling.Quartz/Jobs/QuartzJobAdapter.cs
git commit -m "feat(Scheduling): integrate IJobExecutionHandler hooks in QuartzJobAdapter"
```

---

## Task 6: Update QuartzSchedulerService to Call OnJobScheduledAsync

**Files:**
- Modify: `framework/src/CrestCreates.Scheduling.Quartz/Services/QuartzSchedulerService.cs`

- [ ] **Step 1: Update ScheduleInternalAsync to call OnJobScheduledAsync**

Add after creating the trigger, before `await _scheduler.ScheduleJob`:

```csharp
// Add to ScheduleInternalAsync method, after creating trigger, before ScheduleJob
var executionHandler = _serviceProvider.GetService<IJobExecutionHandler>();
if (executionHandler != null)
{
    await executionHandler.OnJobScheduledAsync(new JobScheduledContext(
        jobId.Uuid,
        typeof(TJob),
        typeof(TArg),
        tenantId,
        organizationId,
        userId,
        JsonSerializer.Serialize(args),
        scheduledTime
    ));
}
```

- [ ] **Step 2: Commit**

```bash
git add framework/src/CrestCreates.Scheduling.Quartz/Services/QuartzSchedulerService.cs
git commit -m "feat(Scheduling): call OnJobScheduledAsync when scheduling jobs"
```

---

## Task 7: Create Test Project and InMemoryJobHistoryRepository

**Files:**
- Create: `framework/test/CrestCreates.Scheduling.Tests/CrestCreates.Scheduling.Tests.csproj`
- Create: `framework/test/CrestCreates.Scheduling.Tests/Jobs/InMemoryJobHistoryRepository.cs`

- [ ] **Step 1: Create test project**

Create directory structure and `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\CrestCreates.Scheduling\CrestCreates.Scheduling.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\CrestCreates.Scheduling.Quartz\CrestCreates.Scheduling.Quartz.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create InMemoryJobHistoryRepository.cs**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Scheduling.Jobs;
using CrestCreates.Scheduling.Services;

namespace CrestCreates.Scheduling.Tests.Jobs;

public class InMemoryJobHistoryRepository : IJobHistoryRepository
{
    private readonly List<JobRecord> _records = new();
    private readonly object _lock = new();
    private int _idCounter = 1;

    public Task<IJobRecord> CreateAsync(IJobRecord record, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var newRecord = new JobRecord
            {
                Id = Guid.NewGuid(),
                JobName = record.JobName,
                JobGroup = record.JobGroup,
                JobUuid = record.JobUuid,
                CronExpression = record.CronExpression,
                Result = record.Result,
                CreatedAt = DateTimeOffset.UtcNow,
                StartedAt = record.StartedAt,
                FinishedAt = record.FinishedAt,
                TenantId = record.TenantId,
                OrganizationId = record.OrganizationId,
                UserId = record.UserId,
                ArgsJson = record.ArgsJson,
                AttemptNumber = record.AttemptNumber,
                ErrorMessage = record.ErrorMessage,
                StackTrace = record.StackTrace
            };
            _records.Add(newRecord);
            return Task.FromResult<IJobRecord>(newRecord);
        }
    }

    public Task UpdateAsync(IJobRecord record, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var existing = _records.FirstOrDefault(r => r.Id == record.Id);
            if (existing != null)
            {
                _records.Remove(existing);
                _records.Add((JobRecord)record);
            }
        }
        return Task.CompletedTask;
    }

    public Task<IJobRecord?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IJobRecord?>(_records.FirstOrDefault(r => r.Id == id));
        }
    }

    public Task<IEnumerable<IJobRecord>> GetByJobIdAsync(Guid jobUuid, int limit = 100, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IEnumerable<IJobRecord>>(_records.Where(r => r.JobUuid == jobUuid).Take(limit).ToList());
        }
    }

    public Task<IEnumerable<IJobRecord>> GetByTenantAsync(Guid tenantId, int limit = 100, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IEnumerable<IJobRecord>>(_records.Where(r => r.TenantId == tenantId).Take(limit).ToList());
        }
    }

    public List<JobRecord> GetAllRecords() => _records.ToList();

    public void Clear() => _records.Clear();
}
```

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Scheduling.Tests/
git commit -m "test(Scheduling): add test project with InMemoryJobHistoryRepository"
```

---

## Task 8: Create Integration Tests

**Files:**
- Create: `framework/test/CrestCreates.Scheduling.Tests/SchedulingIntegrationTests.cs`

- [ ] **Step 1: Create integration test file**

```csharp
using CrestCreates.Scheduling.Jobs;
using CrestCreates.Scheduling.Services;
using CrestCreates.Scheduling.Tests.Jobs;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddSingleton<IJobExecutionHandler>(sp =>
            new DefaultJobExecutionHandler(
                _repository,
                new Microsoft.Extensions.Options.OptionsWrapper<JobRetryOptions>(new JobRetryOptions { MaxRetries = 3, InitialDelay = TimeSpan.FromMilliseconds(100) })));
        services.AddQuartzJobs();
        services.AddScoped<SuccessJob>();
        services.AddScoped<FailingJob>();
        services.AddScoped<TenantJob>();
        services.AddScoped<DelayedJob>();

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

        // Act
        var jobId = await _scheduler.ExecuteNowAsync<SuccessJob>();
        await Task.Delay(500); // Wait for execution

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

        // Act
        var jobId = await _scheduler.ExecuteNowAsync<FailingJob>();
        await Task.Delay(500); // Wait for execution

        // Assert
        var records = await repository.GetByJobIdAsync(jobId.Uuid);
        var failedRecords = records.Where(r => r.Result == JobExecutionResult.Failed).ToList();
        Assert.Single(failedRecords);
        Assert.Equal("FailedJob", failedRecords[0].JobName);
        Assert.NotNull(failedRecords[0].ErrorMessage);
        Assert.Contains("Test failure", failedRecords[0].ErrorMessage);
    }

    [Fact]
    public async Task ExecuteNowAsync_TenantContextJob_PropagatesContext()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var repository = _repository;

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
        var services = new ServiceCollection();
        services.AddSingleton<IJobHistoryRepository>(repository);
        services.AddSingleton<IJobExecutionHandler>(sp =>
            new DefaultJobExecutionHandler(
                repository,
                new Microsoft.Extensions.Options.OptionsWrapper<JobRetryOptions>(new JobRetryOptions { MaxRetries = 2, InitialDelay = TimeSpan.FromMilliseconds(50) })));
        services.AddQuartzJobs();
        services.AddScoped<RetryableJob>();

        // Act
        var jobId = await _scheduler.ExecuteNowAsync<RetryableJob>();
        await Task.Delay(1000); // Wait for retries

        // Assert
        var records = await repository.GetByJobIdAsync(jobId.Uuid);
        var failedRecords = records.Where(r => r.Result == JobExecutionResult.Failed).ToList();
        Assert.True(failedRecords.Count >= 2);
        Assert.Equal(1, failedRecords[0].AttemptNumber);
        Assert.Equal(2, failedRecords[1].AttemptNumber);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsJobRecords()
    {
        // Arrange
        var repository = _repository;
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
        var tenantId = Guid.NewGuid();
        await _scheduler.ExecuteNowAsync<SuccessJob>(tenantId);
        await Task.Delay(500);

        // Act
        var records = await repository.GetByTenantAsync(tenantId);

        // Assert
        Assert.NotEmpty(records);
        Assert.All(records, r => Assert.Equal(tenantId, r.TenantId));
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
```

- [ ] **Step 2: Add using for xunit assert helpers**

Make sure the file includes:
```csharp
using Xunit;
using Assert = Xunit.Assert;
```

- [ ] **Step 3: Run tests to verify they compile**

```bash
cd framework/test/CrestCreates.Scheduling.Tests
dotnet build
```

- [ ] **Step 4: Commit**

```bash
git add framework/test/CrestCreates.Scheduling.Tests/SchedulingIntegrationTests.cs
git commit -m "test(Scheduling): add integration tests for job execution history"
```

---

## Task 9: Add ServiceCollection Extensions

**Files:**
- Create: `framework/src/CrestCreates.Scheduling/Services/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Create extension method**

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Scheduling.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddScheduling(this IServiceCollection services)
    {
        services.AddSingleton<IJobFailureHandler, DefaultJobFailureHandler>();
        return services;
    }

    public static IServiceCollection AddScheduling(this IServiceCollection services, Action<JobRetryOptions> configureRetry)
    {
        services.Configure(configureRetry);
        services.AddSingleton<IJobFailureHandler, DefaultJobFailureHandler>();
        return services;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add framework/src/CrestCreates.Scheduling/Services/ServiceCollectionExtensions.cs
git commit -m "feat(Scheduling): add ServiceCollection extensions for DI registration"
```

---

## Task 10: Final Build and Test Verification

- [ ] **Step 1: Build entire solution**

```bash
dotnet build
```

- [ ] **Step 2: Run scheduling tests**

```bash
dotnet test --filter "FullyQualifiedName~Scheduling"
```

- [ ] **Step 3: Commit any remaining changes**

---

## Self-Review Checklist

Before submitting, verify:

- [ ] All acceptance criteria from spec have corresponding implementation
- [ ] All test scenarios covered (one-time, delayed, cron, retry, cancel, tenant context)
- [ ] No "TBD" or "TODO" placeholders in code
- [ ] Types consistent across all files
- [ ] Tests compile and are runnable
