using System;
using CrestCreates.Scheduling.Jobs;

namespace CrestCreates.Scheduling.Services;

public record JobInfo(
    JobId Id,
    Type JobType,
    Type? ArgType,
    string? CronExpression,
    DateTimeOffset? NextFireTime,
    JobStatus Status,
    int? ExecutionCount
);
