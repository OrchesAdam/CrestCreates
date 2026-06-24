using System;

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
