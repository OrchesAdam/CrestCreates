# Background Job Platform Completion Design

**Date:** 2026-04-20
**Status:** Approved

## 1. Overview

Complete the Background Job Platform by:
1. Adding job execution history persistence via ORM
2. Providing complete integration test coverage for all job types and lifecycle scenarios

## 2. Architecture

```
CrestCreates.Scheduling/                    # Abstractions
├── Jobs/
│   ├── IJob.cs
│   ├── IJobArgs.cs
│   ├── JobExecutionContext.cs
│   ├── JobId.cs
│   └── JobRecord.cs                       # NEW: Execution record entity
├── Services/
│   ├── ISchedulerService.cs
│   ├── IJobFailureHandler.cs               # Extended by IJobExecutionHandler
│   ├── IJobExecutionHandler.cs             # NEW: Pre/post execution hooks
│   └── IJobHistoryRepository.cs           # NEW: History persistence interface

CrestCreates.Scheduling.Quartz/             # Implementation
├── Jobs/
│   └── QuartzJobAdapter.cs                # MODIFIED: Call IJobExecutionHandler
└── Services/
    └── QuartzSchedulerService.cs           # Uses IJobHistoryRepository
```

## 3. New Models

### 3.1 JobRecord Entity

```csharp
// Jobs/JobRecord.cs
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

### 3.2 IJobExecutionHandler

```csharp
// Services/IJobExecutionHandler.cs
namespace CrestCreates.Scheduling.Services;

public interface IJobExecutionHandler : IJobFailureHandler
{
    Task OnJobScheduledAsync(JobScheduledContext context, CancellationToken ct = default);
    Task OnJobStartedAsync(JobStartedContext context, CancellationToken ct = default);
    Task OnJobSucceededAsync(JobSucceededContext context, CancellationToken ct = default);
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
```

### 3.3 IJobHistoryRepository

```csharp
// Services/IJobHistoryRepository.cs
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

## 4. Default Implementation

### 4.1 DefaultJobExecutionHandler

Combines failure handling with execution recording:

```csharp
// Services/DefaultJobExecutionHandler.cs
namespace CrestCreates.Scheduling.Services;

public class DefaultJobExecutionHandler : IJobExecutionHandler
{
    private readonly IJobHistoryRepository _historyRepository;
    private readonly JobRetryOptions? _retryOptions;

    public DefaultJobExecutionHandler(
        IJobHistoryRepository historyRepository,
        IOptions<JobRetryOptions>? retryOptions = null)
    {
        _historyRepository = historyRepository;
        _retryOptions = retryOptions?.Value;
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
        return _historyRepository.CreateAsync(record, ct);
    }

    public Task OnJobStartedAsync(JobStartedContext context, CancellationToken ct = default)
    {
        // Update existing record to Running
        return Task.CompletedTask; // Implementation detail: find and update
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

## 5. Quartz Adapter Integration

The `QuartzJobAdapter` will call `IJobExecutionHandler` hooks:

```csharp
// In QuartzJobAdapter.Execute()
public async Task Execute(IJobExecutionContext quartzContext)
{
    var handler = serviceProvider.GetRequiredService<IJobExecutionHandler>();

    // 1. Record start
    await handler.OnJobStartedAsync(new JobStartedContext(...));

    try
    {
        // 2. Execute job
        await job.ExecuteAsync(context, ct);

        // 3. Record success
        await handler.OnJobSucceededAsync(new JobSucceededContext(...));
    }
    catch (Exception ex)
    {
        // 4. Handle failure
        var failureContext = new JobFailureContext(..., attemptNumber, ex);
        await handler.HandleAsync(failureContext, ct);

        if (handler.ShouldRetry(failureContext))
        {
            // Reschedule with retry
        }
        else
        {
            throw;
        }
    }
}
```

## 6. In-Memory Repository for Tests

```csharp
// For integration tests only
public class InMemoryJobHistoryRepository : IJobHistoryRepository
{
    private readonly List<JobRecord> _records = new();
    private readonly object _lock = new();

    public Task<IJobRecord> CreateAsync(IJobRecord record, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _records.Add((JobRecord)record);
        }
        return Task.FromResult(record);
    }

    public Task UpdateAsync(IJobRecord record, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IJobRecord?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_records.FirstOrDefault(r => r.Id == id));
        }
    }

    public Task<IEnumerable<IJobRecord>> GetByJobIdAsync(Guid jobUuid, int limit = 100, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_records.Where(r => r.JobUuid == jobUuid).Take(limit));
        }
    }

    public Task<IEnumerable<IJobRecord>> GetByTenantAsync(Guid tenantId, int limit = 100, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_records.Where(r => r.TenantId == tenantId).Take(limit));
        }
    }
}
```

## 7. Integration Tests Coverage

| Test | Verifies |
|------|----------|
| `ExecuteNowAsync_OneTimeJob_CompletesSuccessfully` | One-time job execution, record has Result=Succeeded |
| `ScheduleAsync_DelayedJob_ExecutesAfterDelay` | Delayed job, Duration matches delay |
| `RegisterAsync_CronJob_SchedulesRecurring` | Cron job executes multiple times, multiple records created |
| `ExecuteNowAsync_FailingJob_RecordsFailure` | Failed job records ErrorMessage and StackTrace |
| `ExecuteNowAsync_RetryJob_RetriesAndRecords` | Retry produces multiple records with incrementing AttemptNumber |
| `ExecuteNowAsync_TenantContextJob_PropagatesContext` | TenantId/OrgId/UserId correctly recorded |
| `CancelAsync_Job_SetsCancelledStatus` | Cancelled job has Result=Cancelled |
| `PauseAsync_ThenResumeAsync_Job` | Pause/resume lifecycle, no execution during pause |
| `GetHistoryAsync_ReturnsJobRecords` | Query job history by JobUuid |
| `GetHistoryAsync_FilterByTenant` | Query job history by TenantId |

## 8. Acceptance Criteria

- [ ] JobRecord entity stores complete execution history
- [ ] IJobHistoryRepository provides CRUD and query operations
- [ ] DefaultJobExecutionHandler records success and failure
- [ ] Retry produces multiple records with correct AttemptNumber
- [ ] Integration tests cover all job types: one-time, delayed, cron
- [ ] Integration tests cover lifecycle: cancel, pause, resume
- [ ] Integration tests verify tenant context propagation
- [ ] Tests use in-memory repository (no external dependencies)
