namespace CrestCreates.Scheduling.Jobs;

public enum JobExecutionResult
{
    Scheduled = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4
}
