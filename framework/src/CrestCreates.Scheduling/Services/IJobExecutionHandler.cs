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
