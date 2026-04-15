namespace CrestCreates.Scheduling.IntegrationTests.Jobs;

public class OneTimeJob : CrestCreates.Scheduling.Jobs.IJob
{
    private readonly Func<bool> _callback;

    public OneTimeJob(Func<bool> callback)
    {
        _callback = callback;
    }

    public Task ExecuteAsync(CrestCreates.Scheduling.Jobs.JobExecutionContext<CrestCreates.Scheduling.Jobs.NoArgs> context, CancellationToken ct)
    {
        _callback();
        return Task.CompletedTask;
    }
}
