using System;

namespace CrestCreates.Scheduling.Services;

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
