using System;

namespace CrestCreates.Scheduling.Services;

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
