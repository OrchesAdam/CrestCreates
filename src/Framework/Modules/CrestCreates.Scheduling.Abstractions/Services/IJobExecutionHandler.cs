using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Scheduling.Services;

public interface IJobExecutionHandler : IJobFailureHandler
{
    Task OnJobScheduledAsync(JobScheduledContext context, CancellationToken ct = default);
    Task OnJobStartedAsync(JobStartedContext context, CancellationToken ct = default);
    Task OnJobSucceededAsync(JobSucceededContext context, CancellationToken ct = default);
    Task OnJobCancelledAsync(JobCancelledContext context, CancellationToken ct = default);
}
