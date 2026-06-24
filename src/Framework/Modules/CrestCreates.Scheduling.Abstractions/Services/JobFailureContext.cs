using System;
using CrestCreates.Scheduling.Jobs;

namespace CrestCreates.Scheduling.Services;

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
