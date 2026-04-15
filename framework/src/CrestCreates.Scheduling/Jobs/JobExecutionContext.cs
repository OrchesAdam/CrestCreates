using System;
using System.Threading;

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
