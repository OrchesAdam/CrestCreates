using System;

namespace CrestCreates.Scheduling.Services;

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
