using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Quartz.Spi;
using Quartz.Simpl;

namespace CrestCreates.Scheduling.Quartz.Jobs;

public class DependencyInjectionJobFactory(IServiceProvider serviceProvider) : SimpleJobFactory
{
    public override IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
        => new ScopedJob(serviceProvider.CreateAsyncScope(), bundle.JobDetail.JobType);

    // Resolve inside Execute so even constructor failures use Quartz's asynchronous return path.
    private sealed class ScopedJob(AsyncServiceScope scope, Type jobType) : IJob, IAsyncDisposable
    {
        private int _disposed;
        public Task Execute(IJobExecutionContext context)
            => ((IJob)scope.ServiceProvider.GetRequiredService(jobType)).Execute(context);

        public ValueTask DisposeAsync()
            => Interlocked.Exchange(ref _disposed, 1) == 0 ? scope.DisposeAsync() : ValueTask.CompletedTask;
    }
}
