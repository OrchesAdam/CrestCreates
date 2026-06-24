using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Scheduling.Jobs;

public interface IJob<TArg> where TArg : IJobArgs
{
    Task ExecuteAsync(JobExecutionContext<TArg> context, CancellationToken ct = default);
}

public interface IJob : IJob<NoArgs>
{
}
