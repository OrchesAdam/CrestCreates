# Background Job Platform Completion Design

**Date:** 2026-04-15
**Status:** Draft
**Parent:** 2026-04-15-background-job-platform-design.md

## 1. Overview

Complete the remaining work for Tasks 2/3/4 of the background job platform:
1. **Task 2:** Full job auto-discovery via `BackgroundJobAttribute` and SourceGenerator
2. **Task 3:** Real job state persistence using Quartz native JobState
3. **Task 4:** Real integration tests covering all job types and failure scenarios

## 2. Architecture

```
CrestCreates.Scheduling.Abstractions/
├── Attributes/
│   └── BackgroundJobAttribute.cs     # NEW: Job discovery attribute
├── Jobs/
│   ├── IJob.cs
│   ├── IJobArgs.cs
│   ├── JobExecutionContext.cs
│   └── JobId.cs
├── Services/
│   ├── ISchedulerService.cs
│   └── IJobFailureHandler.cs
└── Modules/
    └── SchedulingModule.cs

CrestCreates.Scheduling.Quartz/
├── Jobs/
│   └── QuartzJobAdapter.cs            # FIX: await on reschedule
├── Services/
│   └── QuartzSchedulerService.cs     # FIX: real JobState mapping
└── Modules/
    └── SchedulingQuartzModule.cs

framework/test/
└── CrestCreates.Scheduling.IntegrationTests/  # NEW: Integration tests
```

## 3. Task 2: Background Job Auto-Discovery

### 3.1 BackgroundJobAttribute

```csharp
// Attributes/BackgroundJobAttribute.cs
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class BackgroundJobAttribute : Attribute
{
    /// <summary>Job display name. Defaults to class name.</summary>
    public string? Name { get; init; }

    /// <summary>Cron expression for recurring jobs. If empty, job is one-time/delayed.</summary>
    public string? CronExpression { get; init; }

    /// <summary>Whether authorization is required. Default false.</summary>
    public bool EnableAuthorization { get; init; }

    /// <summary>Job group name. Default "Default".</summary>
    public string Group { get; init; } = "Default";
}
```

### 3.2 SourceGenerator Enhancement

The existing `BackgroundJobsSourceGenerator` already has the skeleton. It needs to:
1. Reference the new `BackgroundJobAttribute` in `CrestCreates.Scheduling.Abstractions`
2. Generate `AddBackgroundJobs()` extension that registers all discovered jobs

**Generated code example:**
```csharp
namespace CrestCreates.Scheduling;
public static class BackgroundJobExtensions
{
    public static IServiceCollection AddBackgroundJobs(this IServiceCollection services)
    {
        services.AddTransient<MyJob>();
        services.AddTransient<AnotherJob>();
        return services;
    }
}
```

### 3.3 QuartzJobAdapter Fix

Line 78 in `QuartzJobAdapter.cs`:
```csharp
// WRONG (current):
await context.Scheduler.RescheduleJob(new TriggerKey(context.Trigger.Key.Name), trigger);

// CORRECT:
await context.Scheduler.RescheduleJob(new TriggerKey(context.Trigger.Key.Name), context.Trigger.Key, trigger);
```

### 3.4 Usage Example

```csharp
[BackgroundJob(Name = "EmailSender", CronExpression = "0 0 * * * ?")]
public class EmailSenderJob : IJob<EmailArgs>
{
    public Task ExecuteAsync(JobExecutionContext<EmailArgs> context, CancellationToken ct)
    {
        // Send email...
    }
}

// In module:
services.AddBackgroundJobs(); // Auto-discovers and registers
```

## 4. Task 3: Job State Persistence

### 4.1 Quartz JobState Mapping

Replace the stub `GetJobStatus()` with real Quartz state reading:

```csharp
// QuartzSchedulerService.cs
private JobStatus ToJobStatus(IJobDetail detail, IEnumerable<ITrigger> triggers)
{
    var state = detail.JobState;

    // Check triggers for more accurate state
    foreach (var trigger in triggers)
    {
        if (trigger is ICronTrigger && trigger.GetNextFireTimeUtc() != null)
            return JobStatus.Scheduled;
        if (trigger is ISimpleTrigger && trigger.GetNextFireTimeUtc() != null)
            return JobStatus.Scheduled;
    }

    return state switch
    {
        Quartz.Impl.AdoJobStore.JobState.Normal => JobStatus.Running,
        Quartz.Impl.AdoJobStore.JobState.Paused => JobStatus.Paused,
        Quartz.Impl.AdoJobStore.JobState.Complete => JobStatus.Completed,
        Quartz.Impl.AdoJobStore.JobState.Error => JobStatus.Failed,
        Quartz.Impl.AdoJobStore.JobState.Blocked => JobStatus.Running,
        _ => JobStatus.Running
    };
}
```

### 4.2 GetAllAsync Enhancement

```csharp
public async Task<IEnumerable<JobInfo>> GetAllAsync(JobStatus status = JobStatus.All)
{
    await EnsureStarted();
    var jobGroups = await _scheduler.GetJobGroupNames();
    var result = new List<JobInfo>();

    foreach (var group in jobGroups)
    {
        var jobKeys = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(group));
        foreach (var key in jobKeys)
        {
            var detail = await _scheduler.GetJobDetail(key);
            var triggers = await _scheduler.GetTriggersOfJob(key);
            var trigger = triggers.FirstOrDefault();

            var jobStatus = ToJobStatus(detail!, triggers);

            if (status != JobStatus.All && jobStatus != status)
                continue;

            result.Add(new JobInfo(
                Id: new JobId(key.Name, key.Group, Guid.Empty),
                JobType: detail!.JobType,
                ArgType: detail!.JobType.IsGenericType ? detail!.JobType.GetGenericArguments()[0] : null,
                CronExpression: trigger is ICronTrigger cronTrigger ? cronTrigger.CronExpressionString : null,
                NextFireTime: trigger?.GetNextFireTimeUtc()?.LocalDateTime,
                Status: jobStatus,
                ExecutionCount: null  // Quartz doesn't track count without persistence
            ));
        }
    }

    return result;
}
```

## 5. Task 4: Integration Tests

### 5.1 Project Structure

```
framework/test/CrestCreates.Scheduling.IntegrationTests/
├── CrestCreates.Scheduling.IntegrationTests.csproj
├── Jobs/
│   ├── OneTimeJob.cs
│   ├── DelayedJob.cs
│   ├── CronJob.cs
│   ├── FailingJob.cs
│   └── TenantContextJob.cs
└── QuartzSchedulerIntegrationTests.cs
```

### 5.2 Test Scenarios

| Test | Method | Verifies |
|------|--------|----------|
| `ExecuteNow_OneTimeJob_Success` | `ExecuteNowAsync` | Job executes immediately |
| `ScheduleAsync_DelayedJob_ExecutesAfterDelay` | `ScheduleAsync(delay)` | Job waits for delay |
| `Register_CronJob_SchedulesRecurring` | `RegisterAsync` | Job recurs on cron |
| `JobExecution_Failure_RecordsError` | `ExecuteNowAsync` + failing job | Error logged |
| `JobExecution_FailureWithRetry_RetriesAndSucceeds` | Configured retry | Retries work |
| `TenantContext_PassedCorrectly` | With tenant params | TenantId propagated |

### 5.3 Test Infrastructure

```csharp
public class QuartzSchedulerIntegrationTests : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly ISchedulerService _scheduler;

    public QuartzSchedulerIntegrationTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddXunit());
        services.AddSingleton<IJobFailureHandler, DefaultJobFailureHandler>();
        services.AddSingleton<ISchedulerService, QuartzSchedulerService>();

        // Register test jobs
        services.AddTransient<OneTimeJob>();
        services.AddTransient<DelayedJob>();
        services.AddTransient<CronJob>();
        services.AddTransient<FailingJob>();
        services.AddTransient<TenantContextJob>();

        _sp = services.BuildServiceProvider();
        _scheduler = _sp.GetRequiredService<ISchedulerService>();
        _scheduler.StartAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _scheduler.StopAsync().GetAwaiter().GetResult();
        _sp.Dispose();
    }
}
```

### 5.4 Sample Test

```csharp
[Fact]
public async Task ExecuteNowAsync_OneTimeJob_CompletesSuccessfully()
{
    // Arrange
    var executed = false;
    var job = new OneTimeJob(() => executed = true);

    // Act
    await _scheduler.ExecuteNowAsync(job.GetType());

    // Wait for execution
    await Task.Delay(500);

    // Assert
    executed.Should().BeTrue();
}
```

### 5.5 Test Jobs

```csharp
public class OneTimeJob : IJob
{
    private readonly Action _callback;
    public OneTimeJob(Action callback) => _callback = callback;

    public Task ExecuteAsync(JobExecutionContext<NoArgs> context, CancellationToken ct)
    {
        _callback();
        return Task.CompletedTask;
    }
}

public class TenantContextJob : IJob
{
    public Guid? ReceivedTenantId { get; private set; }

    public Task ExecuteAsync(JobExecutionContext<NoArgs> context, CancellationToken ct)
    {
        ReceivedTenantId = context.TenantId;
        return Task.CompletedTask;
    }
}
```

## 6. File Changes Summary

### New Files
- `framework/src/CrestCreates.Scheduling/Attributes/BackgroundJobAttribute.cs`
- `framework/test/CrestCreates.Scheduling.IntegrationTests/CrestCreates.Scheduling.IntegrationTests.csproj`
- `framework/test/CrestCreates.Scheduling.IntegrationTests/Jobs/*.cs`
- `framework/test/CrestCreates.Scheduling.IntegrationTests/QuartzSchedulerIntegrationTests.cs`

### Modified Files
- `framework/tools/CrestCreates.CodeGenerator/BackgroundJobsGenerator/BackgroundJobsSourceGenerator.cs` (enhance)
- `framework/src/CrestCreates.Scheduling.Quartz/Jobs/QuartzJobAdapter.cs` (fix await)
- `framework/src/CrestCreates.Scheduling.Quartz/Services/QuartzSchedulerService.cs` (fix state mapping)

## 7. Acceptance Criteria

### Task 2
- [ ] `BackgroundJobAttribute` defined and usable
- [ ] SourceGenerator generates `AddBackgroundJobs()` with all discovered jobs
- [ ] Jobs marked with `[BackgroundJob]` are auto-registered
- [ ] `QuartzJobAdapter` retry logic works correctly with `await`

### Task 3
- [ ] `GetAllAsync()` returns real Quartz job states
- [ ] `JobStatus` correctly reflects Quartz JobState (Running/Paused/Completed/Failed/Scheduled)
- [ ] `NextFireTime` populated from Quartz trigger

### Task 4
- [ ] Integration test project builds and runs
- [ ] One-time job test passes
- [ ] Delayed job test passes
- [ ] Cron job test passes
- [ ] Failure/Retry test passes
- [ ] Tenant context propagation test passes
