# Background Job Platform Design

**Date:** 2026-04-15
**Status:** Approved

## 1. Overview

Elevate the existing `Scheduling` / `Quartz` capability from a "scheduler wrapper" to a unified "background job platform" with consistent job definition, registration, scheduling, execution, failure handling, and observability.

## 2. Architecture

```
CrestCreates.Scheduling/           ← Abstractions (interfaces, models)
CrestCreates.Scheduling.Quartz/    ← Implementation (references Quartz)
```

## 3. Core Models

### 3.1 Job Interfaces

```csharp
// Jobs/IJob.cs
public interface IJob
{
    Task ExecuteAsync(JobExecutionContext<NoArgs> context, CancellationToken ct = default);
}

public interface IJob<TArg> where TArg : IJobArgs
{
    Task ExecuteAsync(JobExecutionContext<TArg> context, CancellationToken ct = default);
}

public record NoArgs : IJobArgs { }
```

```csharp
// Jobs/IJobArgs.cs
public interface IJobArgs { }
```

```csharp
// Jobs/JobExecutionContext.cs
public record JobExecutionContext<TArg>(
    TArg Args,
    JobId JobId,
    Guid? TenantId,
    Guid? OrganizationId,
    Guid? UserId,
    DateTimeOffset ScheduledAt,
    CancellationToken CancellationToken
) where TArg : IJobArgs;
```

```csharp
// Jobs/JobId.cs
public readonly record struct JobId
{
    public string Name { get; init; }
    public string Group { get; init; }
    public Guid Uuid { get; init; }

    public static JobId New() => new(Guid.NewGuid());
    public static JobId Create(string name, string group = "Default") => new(name, group, Guid.Empty);
}
```

### 3.2 Job Types

| Type | Description | Trigger |
|------|-------------|---------|
| One-time | Executes once after delay | User/system |
| Delayed | Executes at specified time | User/system |
| Recurring | Cron-based periodic execution | System (no user context) |

### 3.3 Context Population

- **One-time / Delayed jobs (user-triggered):** Context is captured at schedule time from explicit parameters or `CurrentTenant`/`ICurrentUser`
- **Recurring jobs (system-created):** `TenantId`, `OrganizationId`, `UserId` remain null

## 4. Scheduler Service Interface

```csharp
// Services/ISchedulerService.cs
public interface ISchedulerService
{
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);

    // Recurring jobs (Cron)
    Task<JobId> RegisterAsync<TJob>(JobMetadata metadata) where TJob : IJob;
    Task<JobId> RegisterAsync<TJob, TArg>(JobMetadata metadata) where TJob : IJob<TArg> where TArg : IJobArgs;

    // Delayed / scheduled jobs
    Task<JobId> ScheduleAsync<TJob>(TimeSpan delay, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : IJob;
    Task<JobId> ScheduleAsync<TJob, TArg>(TimeSpan delay, TArg args, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : IJob<TArg> where TArg : IJobArgs;
    Task<JobId> ScheduleAsync<TJob>(DateTimeOffset scheduledTime, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : IJob;
    Task<JobId> ScheduleAsync<TJob, TArg>(DateTimeOffset scheduledTime, TArg args, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : IJob<TArg> where TArg : IJobArgs;

    // Immediate execution
    Task ExecuteNowAsync<TJob>(Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : IJob;
    Task ExecuteNowAsync<TJob, TArg>(TArg args, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : IJob<TArg> where TArg : IJobArgs;

    // Lifecycle
    Task DeleteAsync(JobId jobId);
    Task CancelAsync(JobId jobId);
    Task PauseAsync(JobId jobId);
    Task ResumeAsync(JobId jobId);

    // Query
    Task<bool> ExistsAsync(JobId jobId);
    Task<IEnumerable<JobInfo>> GetAllAsync(JobStatus status = JobStatus.All);
}

public enum JobStatus { All, Running, Paused, Scheduled, Completed, Failed }

public record JobInfo(
    JobId Id,
    Type JobType,
    Type? ArgType,
    string? CronExpression,
    DateTimeOffset? NextFireTime,
    JobStatus Status,
    int? ExecutionCount
);

public class JobMetadata
{
    public required string Name { get; init; }
    public string Group { get; init; } = "Default";
    public string? CronExpression { get; init; }
    public TimeSpan? Timeout { get; init; }
    public JobRetryOptions? Retry { get; init; }
    public string? Description { get; init; }
    public bool Enabled { get; init; } = true;
}
```

## 5. Failure Handling & Retry

```csharp
// Services/IJobFailureHandler.cs
public interface IJobFailureHandler
{
    Task HandleAsync(JobFailureContext context, CancellationToken ct = default);
    bool ShouldRetry(JobFailureContext context);
    TimeSpan? GetNextRetryDelay(JobFailureContext context, int attemptNumber);
}

public record JobFailureContext(
    JobId JobId,
    Type JobType,
    Type? ArgType,
    Exception Exception,
    Guid? TenantId,
    Guid? OrganizationId,
    Guid? UserId,
    DateTimeOffset FailedAt,
    object? Args,
    int AttemptNumber
);

public class JobRetryOptions
{
    public int MaxRetries { get; init; } = 0;
    public TimeSpan? InitialDelay { get; init; }
    public TimeSpan? MaxDelay { get; init; }
    public double BackoffMultiplier { get; init; } = 2.0;
}

public class DefaultJobFailureHandler : IJobFailureHandler
{
    private readonly ILogger<DefaultJobFailureHandler> _logger;
    private readonly JobRetryOptions? _retryOptions;

    public DefaultJobFailureHandler(
        ILogger<DefaultJobFailureHandler> logger,
        IOptions<JobRetryOptions>? retryOptions = null)
    {
        _logger = logger;
        _retryOptions = retryOptions?.Value;
    }

    public Task HandleAsync(JobFailureContext context, CancellationToken ct = default)
    {
        _logger.LogError(context.Exception,
            "Job {JobId} ({JobType}) failed. Tenant={TenantId}, Org={OrgId}, User={UserId}, Attempt={Attempt}",
            context.JobId, context.JobType.Name, context.TenantId, context.OrganizationId, context.UserId,
            context.AttemptNumber);
        return Task.CompletedTask;
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

**Retry flow (inside Quartz adapter):**
1. Execute `job.ExecuteAsync()`
2. Catch exception → create `JobFailureContext(attemptNumber=1)`
3. Call `failureHandler.HandleAsync()`
4. Check `ShouldRetry()`:
   - Yes → calculate delay → reschedule same job (attemptNumber+1)
   - No → log and re-throw

- Triggered only on unhandled exceptions (not cancellation or timeout)
- `AttemptNumber` starts at 1
- `HandleAsync` is called on every failure including final attempt before giving up

## 6. File Structure

```
CrestCreates.Scheduling/
├── Jobs/
│   ├── IJob.cs                    # IJob, IJob<TArg>, NoArgs
│   ├── IJobArgs.cs                # IJobArgs
│   ├── JobExecutionContext.cs     # JobExecutionContext<TArg>
│   └── JobId.cs                   # JobId (readonly struct)
├── Services/
│   ├── ISchedulerService.cs       # ISchedulerService, JobMetadata, JobInfo, JobStatus
│   └── IJobFailureHandler.cs      # IJobFailureHandler, JobFailureContext, DefaultJobFailureHandler
├── SchedulingModule.cs
└── CrestCreates.Scheduling.abstractions.csproj

CrestCreates.Scheduling.Quartz/
├── Jobs/
│   └── QuartzJobAdapter.cs        # Quartz IJob adapter
├── Services/
│   └── QuartzSchedulerService.cs  # ISchedulerService implementation
├── SchedulingQuartzModule.cs
└── CrestCreates.Scheduling.Quartz.csproj
```

## 7. AOT Considerations

- Job discovery via SourceGenerator scanning all `IJob`/`IJob<TArg>` implementations (no reflection at runtime)
- DI registration generated at compile time
- Job scheduling uses generic `ScheduleJobAsync<TJob>()` — no string-based lookup
- Arguments are strongly-typed `record` types — no boxing/unboxing
- Serialization uses `System.Text.Json` (built-in, AOT-friendly)

## 8. SourceGenerator

Scans for all types implementing `IJob` or `IJob<TArg>` and generates:
- DI registration (`services.AddTransient<TJob>()`)
- `AddBackgroundJobs()` extension method

## 9. Acceptance Criteria

- [ ] Business modules do not reference Quartz directly
- [ ] Jobs receive explicit `JobExecutionContext` with tenant/org/user/args
- [ ] One-time, delayed, and recurring jobs share unified interfaces
- [ ] Failure handling is pluggable via `IJobFailureHandler`
- [ ] Retry behavior is configurable per-job via `JobRetryOptions`
- [ ] All scheduling is type-driven (no string keys in business code)
