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
