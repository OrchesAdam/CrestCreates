namespace CrestCreates.Scheduling.IntegrationTests.Jobs;

public class FailingJob : CrestCreates.Scheduling.Jobs.IJob
{
    private readonly int _failuresBeforeSuccess;
    private int _attemptCount;

    public int AttemptCount => _attemptCount;
    public bool HasSucceeded { get; private set; }

    public FailingJob(int failuresBeforeSuccess = 3)
    {
        _failuresBeforeSuccess = failuresBeforeSuccess;
    }

    public Task ExecuteAsync(CrestCreates.Scheduling.Jobs.JobExecutionContext<CrestCreates.Scheduling.Jobs.NoArgs> context, CancellationToken ct)
    {
        _attemptCount++;
        if (_attemptCount < _failuresBeforeSuccess)
        {
            throw new InvalidOperationException($"Job failed on attempt {_attemptCount}");
        }
        HasSucceeded = true;
        return Task.CompletedTask;
    }
}
