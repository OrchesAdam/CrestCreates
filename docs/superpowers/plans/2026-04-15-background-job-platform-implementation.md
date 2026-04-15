# Background Job Platform Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement unified background job platform abstractions and Quartz implementation as specified in `2026-04-15-background-job-platform-design.md`

**Architecture:** Two-module structure — `CrestCreates.Scheduling.Abstractions` (interfaces/models) and `CrestCreates.Scheduling.Quartz` (Quartz.NET implementation). Business modules depend only on abstractions.

**Tech Stack:** .NET 10, Quartz 3.17.1, System.Text.Json

---

## File Structure

```
framework/src/
├── CrestCreates.Scheduling/
│   ├── CrestCreates.Scheduling.abstractions.csproj   (rename from .csproj)
│   ├── Jobs/
│   │   ├── IJob.cs
│   │   ├── IJobArgs.cs
│   │   ├── JobExecutionContext.cs
│   │   └── JobId.cs
│   ├── Services/
│   │   ├── ISchedulerService.cs
│   │   └── IJobFailureHandler.cs
│   └── Modules/
│       └── SchedulingModule.cs
│
└── CrestCreates.Scheduling.Quartz/
    ├── Jobs/
    │   └── QuartzJobAdapter.cs
    ├── Services/
    │   └── QuartzSchedulerService.cs
    ├── Modules/
    │   └── SchedulingQuartzModule.cs
    └── CrestCreates.Scheduling.Quartz.csproj

framework/test/
└── CrestCreates.Scheduling.Tests/
    ├── CrestCreates.Scheduling.Tests.csproj
    └── Jobs/
        ├── IJobTests.cs
        └── JobExecutionContextTests.cs
```

---

## Task 1: Rename Scheduling project to Abstractions

**Files:**
- Rename: `framework/src/CrestCreates.Scheduling/CrestCreates.Scheduling.csproj` → `CrestCreates.Scheduling.Abstractions.csproj`
- Modify: `CrestCreates.Scheduling.Abstractions.csproj` (update AssemblyName and RootNamespace)

- [ ] **Step 1: Rename project file**

```bash
mv /root/workspace/CrestCreates/framework/src/CrestCreates.Scheduling/CrestCreates.Scheduling.csproj \
   /root/workspace/CrestCreates/framework/src/CrestCreates.Scheduling/CrestCreates.Scheduling.Abstractions.csproj
```

- [ ] **Step 2: Update project file content**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Scheduling</RootNamespace>
    <AssemblyName>CrestCreates.Scheduling.Abstractions</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Modularity\CrestCreates.Modularity.csproj" />
    <ProjectReference Include="..\CrestCreates.MultiTenancy.Abstract\CrestCreates.MultiTenancy.Abstract.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Update all project references to use new name**

Search for `CrestCreates.Scheduling.csproj` references and update to `CrestCreates.Scheduling.Abstractions.csproj`.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "refactor(scheduling): rename to Scheduling.Abstractions"
```

---

## Task 2: Create Job model files in Abstractions

**Files:**
- Create: `framework/src/CrestCreates.Scheduling/Jobs/IJobArgs.cs`
- Create: `framework/src/CrestCreates.Scheduling/Jobs/IJob.cs`
- Create: `framework/src/CrestCreates.Scheduling/Jobs/JobExecutionContext.cs`
- Create: `framework/src/CrestCreates.Scheduling/Jobs/JobId.cs`

- [ ] **Step 1: Write IJobArgs.cs**

```csharp
namespace CrestCreates.Scheduling.Jobs;

public interface IJobArgs { }
```

- [ ] **Step 2: Write IJob.cs**

```csharp
using CrestCreates.Scheduling.Jobs;

namespace CrestCreates.Scheduling.Jobs;

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

- [ ] **Step 3: Write JobExecutionContext.cs**

```csharp
namespace CrestCreates.Scheduling.Jobs;

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

- [ ] **Step 4: Write JobId.cs**

```csharp
namespace CrestCreates.Scheduling.Jobs;

public readonly record struct JobId
{
    public string Name { get; init; }
    public string Group { get; init; }
    public Guid Uuid { get; init; }

    public static JobId New() => new(Guid.NewGuid());
    public static JobId Create(string name, string group = "Default") => new(name, group, Guid.Empty);

    public override string ToString()
        => Uuid != Guid.Empty ? Uuid.ToString() : $"{Group}/{Name}";
}
```

- [ ] **Step 5: Run build to verify**

```bash
cd /root/workspace/CrestCreates && dotnet build framework/src/CrestCreates.Scheduling.Abstractions/CrestCreates.Scheduling.Abstractions.csproj
```

Expected: Build succeeds

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(scheduling): add job model types"
```

---

## Task 3: Create Service interfaces

**Files:**
- Create: `framework/src/CrestCreates.Scheduling/Services/ISchedulerService.cs`
- Create: `framework/src/CrestCreates.Scheduling/Services/IJobFailureHandler.cs`

- [ ] **Step 1: Write ISchedulerService.cs**

```csharp
using CrestCreates.Scheduling.Jobs;

namespace CrestCreates.Scheduling.Services;

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

- [ ] **Step 2: Write IJobFailureHandler.cs**

```csharp
namespace CrestCreates.Scheduling.Services;

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

- [ ] **Step 3: Run build to verify**

```bash
dotnet build framework/src/CrestCreates.Scheduling.Abstractions/CrestCreates.Scheduling.Abstractions.csproj
```

Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat(scheduling): add scheduler service interfaces"
```

---

## Task 4: Update SchedulingModule

**Files:**
- Modify: `framework/src/CrestCreates.Scheduling/Modules/SchedulingModule.cs`

- [ ] **Step 1: Write SchedulingModule.cs**

```csharp
using CrestCreates.Modularity;
using CrestCreates.Scheduling.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Scheduling.Modules;

public class SchedulingModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        base.OnConfigureServices(services);

        services.AddSingleton<IJobFailureHandler, DefaultJobFailureHandler>();
    }
}
```

- [ ] **Step 2: Run build to verify**

```bash
dotnet build framework/src/CrestCreates.Scheduling.Abstractions/CrestCreates.Scheduling.Abstractions.csproj
```

Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "feat(scheduling): update SchedulingModule to register DefaultJobFailureHandler"
```

---

## Task 5: Update Quartz project file

**Files:**
- Modify: `framework/src/CrestCreates.Scheduling.Quartz/CrestCreates.Scheduling.Quartz.csproj`

- [ ] **Step 1: Update csproj to add Quartz package reference**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Scheduling.Quartz</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Quartz" />
    <PackageReference Include="Microsoft.Extensions.Options" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Scheduling.Abstractions\CrestCreates.Scheduling.Abstractions.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Run restore to verify**

```bash
dotnet restore framework/src/CrestCreates.Scheduling.Quartz/CrestCreates.Scheduling.Quartz.csproj
```

Expected: Restore succeeds

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "feat(scheduling.quartz): update csproj with Quartz package"
```

---

## Task 6: Create QuartzJobAdapter

**Files:**
- Create: `framework/src/CrestCreates.Scheduling.Quartz/Jobs/QuartzJobAdapter.cs`

- [ ] **Step 1: Write QuartzJobAdapter.cs**

```csharp
using System.Text.Json;
using CrestCreates.Scheduling.Jobs;
using CrestCreates.Scheduling.Services;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace CrestCreates.Scheduling.Quartz.Jobs;

internal class QuartzJobAdapter<TJob, TArg> : IJob where TJob : IJob<TArg> where TArg : IJobArgs
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IJobFailureHandler _failureHandler;

    public QuartzJobAdapter(IServiceProvider serviceProvider, IJobFailureHandler failureHandler)
    {
        _serviceProvider = serviceProvider;
        _failureHandler = failureHandler;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        using var scope = _serviceProvider.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<TJob>();

        var scheduledAt = context.ScheduledFireTimeUtc ?? DateTimeOffset.UtcNow;
        var argsJson = context.MergedJobDataMap.GetString("Args");
        var args = argsJson != null ? JsonSerializer.Deserialize<TArg>(argsJson) : default;
        var tenantId = context.MergedJobDataMap.GetString("TenantId");
        var organizationId = context.MergedJobDataMap.GetString("OrganizationId");
        var userId = context.MergedJobDataMap.GetString("UserId");
        var attemptNumber = context.MergedJobDataMap.GetIntValue("AttemptNumber") ?? 1;

        var jobId = new JobId(context.JobKey.Name, context.JobKey.Group, context.FireInstanceId);
        var jobContext = new JobExecutionContext<TArg>(
            Args: args!,
            JobId: jobId,
            TenantId: string.IsNullOrEmpty(tenantId) ? null : Guid.Parse(tenantId),
            OrganizationId: string.IsNullOrEmpty(organizationId) ? null : Guid.Parse(organizationId),
            UserId: string.IsNullOrEmpty(userId) ? null : Guid.Parse(userId),
            ScheduledAt: scheduledAt,
            CancellationToken: context.CancellationToken
        );

        try
        {
            await job.ExecuteAsync(jobContext, context.CancellationToken);
        }
        catch (Exception ex)
        {
            var failureContext = new JobFailureContext(
                jobId,
                typeof(TJob),
                typeof(TArg),
                ex,
                jobContext.TenantId,
                jobContext.OrganizationId,
                jobContext.UserId,
                DateTimeOffset.UtcNow,
                args,
                attemptNumber
            );

            await _failureHandler.HandleAsync(failureContext, context.CancellationToken);

            if (_failureHandler.ShouldRetry(failureContext))
            {
                var delay = _failureHandler.GetNextRetryDelay(failureContext, attemptNumber);
                context.MergedJobDataMap.Put("AttemptNumber", attemptNumber + 1);

                var trigger = TriggerBuilder.Create()
                    .StartAt(DateTimeOffset.UtcNow.Add(delay ?? TimeSpan.Zero))
                    .Build();

                context.Scheduler.RescheduleJob(new TriggerKey(context.Trigger.Key.Name), trigger);
            }

            throw;
        }
    }
}
```

- [ ] **Step 2: Run build to verify**

```bash
dotnet build framework/src/CrestCreates.Scheduling.Quartz/CrestCreates.Scheduling.Quartz.csproj
```

Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "feat(scheduling.quartz): add QuartzJobAdapter"
```

---

## Task 7: Implement QuartzSchedulerService

**Files:**
- Modify: `framework/src/CrestCreates.Scheduling.Quartz/Services/QuartzSchedulerService.cs`
- Delete: `framework/src/CrestCreates.Scheduling/Services/ISchedulerService.cs` (merged into Abstractions)
- Delete: `framework/src/CrestCreates.Scheduling/Jobs/IJob.cs` (merged into Abstractions)

- [ ] **Step 1: Write QuartzSchedulerService.cs**

```csharp
using System.Text.Json;
using CrestCreates.Scheduling.Jobs;
using CrestCreates.Scheduling.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl;
using IScheduler = Quartz.IScheduler;

namespace CrestCreates.Scheduling.Quartz.Services;

public class QuartzSchedulerService : ISchedulerService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IJobFailureHandler _failureHandler;
    private readonly ILogger<QuartzSchedulerService> _logger;
    private IScheduler? _scheduler;

    public QuartzSchedulerService(
        IServiceProvider serviceProvider,
        IJobFailureHandler failureHandler,
        ILogger<QuartzSchedulerService> logger)
    {
        _serviceProvider = serviceProvider;
        _failureHandler = failureHandler;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_scheduler != null) return;

        var factory = new StdSchedulerFactory();
        _scheduler = await factory.GetScheduler(ct);
        await _scheduler.Start(ct);
        _logger.LogInformation("Quartz scheduler started");
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_scheduler != null)
        {
            await _scheduler.Shutdown(waitForJobsToComplete: true, ct: ct);
            _logger.LogInformation("Quartz scheduler stopped");
        }
    }

    public async Task<JobId> RegisterAsync<TJob>(JobMetadata metadata) where TJob : IJob
        => await RegisterAsync<TJob, NoArgs>(metadata);

    public async Task<JobId> RegisterAsync<TJob, TArg>(JobMetadata metadata) where TJob : IJob<TArg> where TArg : IJobArgs
    {
        await EnsureStarted();

        var jobId = JobId.Create(metadata.Name, metadata.Group);
        var jobKey = new JobKey(metadata.Name, metadata.Group);

        if (await _scheduler!.CheckExists(jobKey))
        {
            await _scheduler.DeleteJob(jobKey);
        }

        var jobData = new JobDataMap
        {
            { "Args", JsonSerializer.Serialize(default(TArg)) }
        };

        var job = JobBuilder.Create<QuartzJobAdapter<TJob, TArg>>()
            .WithIdentity(jobKey)
            .WithDescription(metadata.Description)
            .UsingJobData(jobData)
            .StoreDurably()
            .Build();

        ITrigger trigger;

        if (!string.IsNullOrEmpty(metadata.CronExpression))
        {
            trigger = TriggerBuilder.Create()
                .WithIdentity($"{metadata.Name}Trigger", metadata.Group)
                .WithCronSchedule(metadata.CronExpression)
                .ForJob(jobKey)
                .Build();
        }
        else
        {
            trigger = TriggerBuilder.Create()
                .WithIdentity($"{metadata.Name}Trigger", metadata.Group)
                .StartNow()
                .ForJob(jobKey)
                .Build();
        }

        await _scheduler.ScheduleJob(job, trigger);
        _logger.LogInformation("Registered job {JobName} in group {Group}", metadata.Name, metadata.Group);

        return jobId;
    }

    public async Task<JobId> ScheduleAsync<TJob>(TimeSpan delay, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : IJob
        => await ScheduleAsync<TJob, NoArgs>(delay, default, tenantId, organizationId, userId);

    public async Task<JobId> ScheduleAsync<TJob, TArg>(TimeSpan delay, TArg args, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : IJob<TArg> where TArg : IJobArgs
        => await ScheduleInternalAsync<TJob, TArg>(DateTimeOffset.UtcNow.Add(delay), args, tenantId, organizationId, userId);

    public async Task<JobId> ScheduleAsync<TJob>(DateTimeOffset scheduledTime, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : IJob
        => await ScheduleAsync<TJob, NoArgs>(scheduledTime, default, tenantId, organizationId, userId);

    public async Task<JobId> ScheduleAsync<TJob, TArg>(DateTimeOffset scheduledTime, TArg args, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : IJob<TArg> where TArg : IJobArgs
        => await ScheduleInternalAsync<TJob, TArg>(scheduledTime, args, tenantId, organizationId, userId);

    private async Task<JobId> ScheduleInternalAsync<TJob, TArg>(DateTimeOffset scheduledTime, TArg args, Guid? tenantId, Guid? organizationId, Guid? userId) where TJob : IJob<TArg> where TArg : IJobArgs
    {
        await EnsureStarted();

        var jobId = JobId.New();
        var jobKey = new JobKey(jobId.Uuid.ToString());
        var triggerKey = new TriggerKey($"{jobId.Uuid}Trigger");

        var jobData = new JobDataMap
        {
            { "Args", JsonSerializer.Serialize(args) },
            { "TenantId", tenantId?.ToString() ?? string.Empty },
            { "OrganizationId", organizationId?.ToString() ?? string.Empty },
            { "UserId", userId?.ToString() ?? string.Empty },
            { "AttemptNumber", 1 }
        };

        var job = JobBuilder.Create<QuartzJobAdapter<TJob, TArg>>()
            .WithIdentity(jobKey)
            .UsingJobData(jobData)
            .StoreDurably()
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .StartAt(scheduledTime)
            .ForJob(jobKey)
            .Build();

        await _scheduler!.ScheduleJob(job, trigger);
        _logger.LogInformation("Scheduled job {JobId} for {ScheduledTime}", jobId, scheduledTime);

        return jobId;
    }

    public async Task ExecuteNowAsync<TJob>(Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : IJob
        => await ExecuteNowAsync<TJob, NoArgs>(default, tenantId, organizationId, userId);

    public async Task ExecuteNowAsync<TJob, TArg>(TArg args, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : IJob<TArg> where TArg : IJobArgs
    {
        await EnsureStarted();

        var jobId = JobId.New();
        var jobKey = new JobKey(jobId.Uuid.ToString());

        var jobData = new JobDataMap
        {
            { "Args", JsonSerializer.Serialize(args) },
            { "TenantId", tenantId?.ToString() ?? string.Empty },
            { "OrganizationId", organizationId?.ToString() ?? string.Empty },
            { "UserId", userId?.ToString() ?? string.Empty },
            { "AttemptNumber", 1 }
        };

        var job = JobBuilder.Create<QuartzJobAdapter<TJob, TArg>>()
            .WithIdentity(jobKey)
            .UsingJobData(jobData)
            .StoreDurably()
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity($"{jobId.Uuid}Trigger")
            .StartNow()
            .ForJob(jobKey)
            .Build();

        await _scheduler!.ScheduleJob(job, trigger);
    }

    public async Task DeleteAsync(JobId jobId)
    {
        await EnsureStarted();
        var key = ToQuartzKey(jobId);
        await _scheduler!.DeleteJob(key);
        _logger.LogInformation("Deleted job {JobId}", jobId);
    }

    public async Task CancelAsync(JobId jobId)
    {
        await EnsureStarted();
        var key = new JobKey(jobId.Uuid.ToString());
        if (await _scheduler!.CheckExists(key))
        {
            await _scheduler.Interrupt(key);
        }
    }

    public async Task PauseAsync(JobId jobId)
    {
        await EnsureStarted();
        var key = ToQuartzKey(jobId);
        await _scheduler!.PauseJob(key);
    }

    public async Task ResumeAsync(JobId jobId)
    {
        await EnsureStarted();
        var key = ToQuartzKey(jobId);
        await _scheduler!.ResumeJob(key);
    }

    public async Task<bool> ExistsAsync(JobId jobId)
    {
        await EnsureStarted();
        var key = ToQuartzKey(jobId);
        return await _scheduler!.CheckExists(key);
    }

    public async Task<IEnumerable<JobInfo>> GetAllAsync(JobStatus status = JobStatus.All)
    {
        await EnsureStarted();
        var jobGroups = await _scheduler!.GetJobGroupNames();
        var result = new List<JobInfo>();

        foreach (var group in jobGroups)
        {
            var jobKeys = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(group));
            foreach (var key in jobKeys)
            {
                var detail = await _scheduler.GetJobDetail(key);
                var triggers = await _scheduler.GetTriggersOfJob(key);
                var trigger = triggers.FirstOrDefault();

                result.Add(new JobInfo(
                    Id: new JobId(key.Name, key.Group, Guid.Empty),
                    JobType: detail.JobType,
                    ArgType: detail.JobType.IsGenericType ? detail.JobType.GetGenericArguments()[0] : null,
                    CronExpression: trigger is ICronTrigger cronTrigger ? cronTrigger.CronExpressionString : null,
                    NextFireTime: trigger?.GetNextFireTimeUtc()?.LocalDateTime,
                    Status: GetJobStatus(detail).ToJobStatus(),
                    ExecutionCount: null
                ));
            }
        }

        return result;
    }

    private JobKey ToQuartzKey(JobId jobId)
    {
        return jobId.Uuid != Guid.Empty
            ? new JobKey(jobId.Uuid.ToString())
            : new JobKey(jobId.Name, jobId.Group);
    }

    private Quartz.Impl.AdoJobStore.JobState GetJobStatus(IJobDetail detail)
    {
        return Quartz.Impl.AdoJobStore.JobState.Normal;
    }

    private async Task EnsureStarted()
    {
        if (_scheduler == null)
        {
            await StartAsync();
        }
    }
}

internal static class JobStateExtensions
{
    public static JobStatus ToJobStatus(this Quartz.Impl.AdoJobStore.JobState state) => state switch
    {
        Quartz.Impl.AdoJobStore.JobState.Normal => JobStatus.Running,
        Quartz.Impl.AdoJobStore.JobState.Paused => JobStatus.Paused,
        Quartz.Impl.AdoJobStore.JobState.Complete => JobStatus.Completed,
        Quartz.Impl.AdoJobStore.JobState.Error => JobStatus.Failed,
        Quartz.Impl.AdoJobStore.JobState.Blocked => JobStatus.Running,
        _ => JobStatus.Scheduled
    };
}
```

- [ ] **Step 2: Run build to verify**

```bash
dotnet build framework/src/CrestCreates.Scheduling.Quartz/CrestCreates.Scheduling.Quartz.csproj
```

Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "feat(scheduling.quartz): implement QuartzSchedulerService"
```

---

## Task 8: Update SchedulingQuartzModule

**Files:**
- Modify: `framework/src/CrestCreates.Scheduling.Quartz/Modules/SchedulingQuartzModule.cs`

- [ ] **Step 1: Write SchedulingQuartzModule.cs**

```csharp
using CrestCreates.Modularity;
using CrestCreates.Scheduling.Services;
using CrestCreates.Scheduling.Quartz.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Scheduling.Quartz.Modules;

public class SchedulingQuartzModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        base.OnConfigureServices(services);

        services.AddSingleton<ISchedulerService, QuartzSchedulerService>();
    }

    public override async Task OnApplicationInitialization(IHost host)
    {
        await base.OnApplicationInitialization(host);

        var schedulerService = host.Services.GetRequiredService<ISchedulerService>();
        await schedulerService.StartAsync();
    }
}
```

- [ ] **Step 2: Run build to verify**

```bash
dotnet build framework/src/CrestCreates.Scheduling.Quartz/CrestCreates.Scheduling.Quartz.csproj
```

Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "feat(scheduling.quartz): update SchedulingQuartzModule"
```

---

## Task 9: Create unit tests

**Files:**
- Create: `framework/test/CrestCreates.Scheduling.Tests/CrestCreates.Scheduling.Tests.csproj`
- Create: `framework/test/CrestCreates.Scheduling.Tests/Jobs/IJobTests.cs`
- Create: `framework/test/CrestCreates.Scheduling.Tests/Jobs/JobIdTests.cs`
- Create: `framework/test/CrestCreates.Scheduling.Tests/Jobs/JobExecutionContextTests.cs`
- Create: `framework/test/CrestCreates.Scheduling.Tests/Services/DefaultJobFailureHandlerTests.cs`

- [ ] **Step 1: Create test project**

```bash
mkdir -p /root/workspace/CrestCreates/framework/test/CrestCreates.Scheduling.Tests/Jobs
mkdir -p /root/workspace/CrestCreates/framework/test/CrestCreates.Scheduling.Tests/Services
```

Create `CrestCreates.Scheduling.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Moq" />
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="AutoFixture" />
    <PackageReference Include="AutoFixture.AutoMoq" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\CrestCreates.Scheduling.Abstractions\CrestCreates.Scheduling.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.TestBase\CrestCreates.TestBase.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write IJobTests.cs**

```csharp
using CrestCreates.Scheduling.Jobs;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Scheduling.Tests.Jobs;

public class IJobTests
{
    [Fact]
    public void NoArgs_ShouldImplementIJobArgs()
    {
        // Arrange & Act
        var noArgs = new NoArgs();

        // Assert
        noArgs.Should().BeAssignableTo<IJobArgs>();
    }

    [Fact]
    public void IJob_ExecuteAsync_Signature_ShouldBeValid()
    {
        // Arrange
        var job = new TestJob();

        // Act & Assert
        job.ExecuteAsync(default!).Should().BeAssignableTo<Task>();
    }

    [Fact]
    public void IJobOfT_ExecuteAsync_ShouldAcceptJobExecutionContext()
    {
        // Arrange
        var job = new TestJobWithArgs();
        var context = new JobExecutionContext<NoArgs>(
            new NoArgs(),
            JobId.New(),
            null, null, null,
            DateTimeOffset.UtcNow,
            CancellationToken.None
        );

        // Act & Assert - should compile
        var task = job.ExecuteAsync(context, CancellationToken.None);
        task.Should().BeAssignableTo<Task>();
    }

    private class TestJob : IJob
    {
        public Task ExecuteAsync(JobExecutionContext<NoArgs> context, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private class TestJobWithArgs : IJob<NoArgs>
    {
        public Task ExecuteAsync(JobExecutionContext<NoArgs> context, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Write JobIdTests.cs**

```csharp
using CrestCreates.Scheduling.Jobs;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Scheduling.Tests.Jobs;

public class JobIdTests
{
    [Fact]
    public void New_ShouldCreateJobIdWithUuid()
    {
        // Arrange & Act
        var jobId = JobId.New();

        // Assert
        jobId.Uuid.Should().NotBe(Guid.Empty);
        jobId.Name.Should().BeNull();
        jobId.Group.Should().BeNull();
    }

    [Fact]
    public void Create_ShouldCreateJobIdWithNameAndGroup()
    {
        // Arrange & Act
        var jobId = JobId.Create("TestJob", "TestGroup");

        // Assert
        jobId.Name.Should().Be("TestJob");
        jobId.Group.Should().Be("TestGroup");
        jobId.Uuid.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Create_WithDefaultGroup_ShouldUseDefaultValue()
    {
        // Arrange & Act
        var jobId = JobId.Create("TestJob");

        // Assert
        jobId.Group.Should().Be("Default");
    }

    [Fact]
    public void ToString_ForUuidBasedJobId_ShouldReturnUuidString()
    {
        // Arrange
        var jobId = JobId.New();

        // Act
        var result = jobId.ToString();

        // Assert
        result.Should().Be(jobId.Uuid.ToString());
    }

    [Fact]
    public void ToString_ForNameGroupBasedJobId_ShouldReturnGroupName()
    {
        // Arrange
        var jobId = JobId.Create("TestJob", "TestGroup");

        // Act
        var result = jobId.ToString();

        // Assert
        result.Should().Be("TestGroup/TestJob");
    }

    [Fact]
    public void JobId_ValueEquality_ShouldWorkCorrectly()
    {
        // Arrange
        var jobId1 = JobId.Create("TestJob", "TestGroup");
        var jobId2 = JobId.Create("TestJob", "TestGroup");
        var jobId3 = JobId.Create("DifferentJob", "TestGroup");

        // Assert
        jobId1.Should().Be(jobId2);
        jobId1.Should().NotBe(jobId3);
    }
}
```

- [ ] **Step 4: Write JobExecutionContextTests.cs**

```csharp
using CrestCreates.Scheduling.Jobs;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Scheduling.Tests.Jobs;

public class JobExecutionContextTests
{
    [Fact]
    public void JobExecutionContext_ShouldStoreAllProperties()
    {
        // Arrange
        var args = new TestArgs("test");
        var jobId = JobId.New();
        var tenantId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var scheduledAt = DateTimeOffset.UtcNow;
        var ct = CancellationToken.None;

        // Act
        var context = new JobExecutionContext<TestArgs>(
            args, jobId, tenantId, orgId, userId, scheduledAt, ct);

        // Assert
        context.Args.Should().Be(args);
        context.JobId.Should().Be(jobId);
        context.TenantId.Should().Be(tenantId);
        context.OrganizationId.Should().Be(orgId);
        context.UserId.Should().Be(userId);
        context.ScheduledAt.Should().Be(scheduledAt);
        context.CancellationToken.Should().Be(ct);
    }

    [Fact]
    public void JobExecutionContext_WithNullTenantIds_ShouldWork()
    {
        // Arrange & Act
        var context = new JobExecutionContext<NoArgs>(
            new NoArgs(), JobId.New(), null, null, null, DateTimeOffset.UtcNow, CancellationToken.None);

        // Assert
        context.TenantId.Should().BeNull();
        context.OrganizationId.Should().BeNull();
        context.UserId.Should().BeNull();
    }

    private record TestArgs(string Value) : IJobArgs;
}
```

- [ ] **Step 5: Write DefaultJobFailureHandlerTests.cs**

```csharp
using CrestCreates.Scheduling.Jobs;
using CrestCreates.Scheduling.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.Scheduling.Tests.Services;

public class DefaultJobFailureHandlerTests
{
    private readonly Mock<ILogger<DefaultJobFailureHandler>> _loggerMock;
    private readonly DefaultJobFailureHandler _handler;

    public DefaultJobFailureHandlerTests()
    {
        _loggerMock = new Mock<ILogger<DefaultJobFailureHandler>>();
        _handler = new DefaultJobFailureHandler(_loggerMock.Object);
    }

    [Fact]
    public void HandleAsync_ShouldLogError()
    {
        // Arrange
        var context = CreateFailureContext();

        // Act
        var task = _handler.HandleAsync(context, CancellationToken.None);
        task.Wait();

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((o, e) => true)),
            Times.Once);
    }

    [Fact]
    public void ShouldRetry_WhenNoRetryOptions_ShouldReturnFalse()
    {
        // Arrange
        var context = CreateFailureContext(attemptNumber: 1);

        // Act
        var result = _handler.ShouldRetry(context);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldRetry_WhenMaxRetriesReached_ShouldReturnFalse()
    {
        // Arrange
        var retryOptions = new JobRetryOptions { MaxRetries = 3 };
        var optionsMock = new Mock<IOptions<JobRetryOptions>>();
        optionsMock.Setup(x => x.Value).Returns(retryOptions);
        var handler = new DefaultJobFailureHandler(_loggerMock.Object, optionsMock.Object);
        var context = CreateFailureContext(attemptNumber: 4);

        // Act
        var result = handler.ShouldRetry(context);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldRetry_WhenWithinRetryLimit_ShouldReturnTrue()
    {
        // Arrange
        var retryOptions = new JobRetryOptions { MaxRetries = 3 };
        var optionsMock = new Mock<IOptions<JobRetryOptions>>();
        optionsMock.Setup(x => x.Value).Returns(retryOptions);
        var handler = new DefaultJobFailureHandler(_loggerMock.Object, optionsMock.Object);
        var context = CreateFailureContext(attemptNumber: 2);

        // Act
        var result = handler.ShouldRetry(context);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void GetNextRetryDelay_WithNoInitialDelay_ShouldReturnNull()
    {
        // Arrange
        var context = CreateFailureContext();

        // Act
        var result = _handler.GetNextRetryDelay(context, 1);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetNextRetryDelay_WithExponentialBackoff_ShouldCalculateCorrectly()
    {
        // Arrange
        var retryOptions = new JobRetryOptions
        {
            MaxRetries = 3,
            InitialDelay = TimeSpan.FromSeconds(1),
            BackoffMultiplier = 2.0
        };
        var optionsMock = new Mock<IOptions<JobRetryOptions>>();
        optionsMock.Setup(x => x.Value).Returns(retryOptions);
        var handler = new DefaultJobFailureHandler(_loggerMock.Object, optionsMock.Object);
        var context = CreateFailureContext();

        // Act - attempt 1 (first retry after initial failure)
        var delay1 = handler.GetNextRetryDelay(context, 1);
        // Act - attempt 2
        var delay2 = handler.GetNextRetryDelay(context, 2);
        // Act - attempt 3
        var delay3 = handler.GetNextRetryDelay(context, 3);

        // Assert
        delay1.Should().Be(TimeSpan.FromSeconds(1));   // 1 * 2^0
        delay2.Should().Be(TimeSpan.FromSeconds(2));   // 1 * 2^1
        delay3.Should().Be(TimeSpan.FromSeconds(4));   // 1 * 2^2
    }

    [Fact]
    public void GetNextRetryDelay_WithMaxDelay_ShouldCapAtMaximum()
    {
        // Arrange
        var retryOptions = new JobRetryOptions
        {
            MaxRetries = 5,
            InitialDelay = TimeSpan.FromSeconds(10),
            MaxDelay = TimeSpan.FromSeconds(30),
            BackoffMultiplier = 2.0
        };
        var optionsMock = new Mock<IOptions<JobRetryOptions>>();
        optionsMock.Setup(x => x.Value).Returns(retryOptions);
        var handler = new DefaultJobFailureHandler(_loggerMock.Object, optionsMock.Object);
        var context = CreateFailureContext();

        // Act - attempt 4 would be 80 seconds without cap
        var delay = handler.GetNextRetryDelay(context, 4);

        // Assert
        delay.Should().Be(TimeSpan.FromSeconds(30)); // capped at max
    }

    private JobFailureContext CreateFailureContext(int attemptNumber = 1)
    {
        return new JobFailureContext(
            JobId.New(),
            typeof(object),
            typeof(NoArgs),
            new Exception("Test exception"),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            null,
            attemptNumber
        );
    }
}
```

- [ ] **Step 6: Run tests**

```bash
dotnet test framework/test/CrestCreates.Scheduling.Tests/CrestCreates.Scheduling.Tests.csproj
```

Expected: All tests pass

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "test(scheduling): add unit tests for scheduling abstractions"
```

---

## Task 10: Clean up old files

**Files:**
- Delete: `framework/src/CrestCreates.Scheduling/Jobs/IJob.cs` (old file, replaced by new in Abstractions)
- Delete: `framework/src/CrestCreates.Scheduling/Services/ISchedulerService.cs` (old file, replaced by new in Abstractions)

- [ ] **Step 1: Verify no references to old files**

```bash
dotnet build /root/workspace/CrestCreates/framework/src/CrestCreates.Scheduling.Abstractions/CrestCreates.Scheduling.Abstractions.csproj
```

Expected: Build succeeds

- [ ] **Step 2: Commit cleanup**

```bash
git add -A && git commit -m "chore(scheduling): remove obsolete files"
```

---

## Task 11: Full solution build & test

- [ ] **Step 1: Restore and build entire solution**

```bash
dotnet restore /root/workspace/CrestCreates/CrestCreates.slnx
dotnet build /root/workspace/CrestCreates/CrestCreates.slnx
```

Expected: Build succeeds

- [ ] **Step 2: Run all tests**

```bash
dotnet test /root/workspace/CrestCreates/CrestCreates.slnx --no-build
```

Expected: All tests pass

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "chore(scheduling): full solution build verification"
```

---

## Self-Review Checklist

- [ ] Spec coverage: All spec items mapped to tasks
- [ ] No placeholders (TBD, TODO)
- [ ] Type consistency: JobId, JobExecutionContext, IJob, IJob<TArg> match spec
- [ ] File paths are exact
- [ ] Commands have expected output noted

---

**Plan complete.** Saved to `docs/superpowers/plans/2026-04-15-background-job-platform-implementation.md`

**Two execution options:**

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

Which approach?
