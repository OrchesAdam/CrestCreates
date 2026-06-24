using System;

namespace CrestCreates.Scheduling.Services;

public class JobMetadata
{
    public required string Name { get; init; }
    public string Group { get; init; } = "Default";
    public string? CronExpression { get; init; }
    public TimeSpan? Timeout { get; init; }
    public JobRetryOptions? Retry { get; init; }
    public string? Description { get; init; }
    public bool Enabled { get; init; } = true;
}
