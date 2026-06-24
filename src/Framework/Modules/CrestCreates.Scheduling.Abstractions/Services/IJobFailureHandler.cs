using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Scheduling.Services;

public interface IJobFailureHandler
{
    Task HandleAsync(JobFailureContext context, CancellationToken ct = default);
    bool ShouldRetry(JobFailureContext context);
    TimeSpan? GetNextRetryDelay(JobFailureContext context, int attemptNumber);
}
