using System;

namespace CrestCreates.Scheduling.Services;

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
