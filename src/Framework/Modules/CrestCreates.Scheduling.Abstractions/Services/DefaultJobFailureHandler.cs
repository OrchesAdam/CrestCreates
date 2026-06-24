using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Scheduling.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.Scheduling.Services;

public class DefaultJobFailureHandler : IJobFailureHandler
{
    private readonly ILogger<DefaultJobFailureHandler> _logger;
    private readonly JobRetryOptions? _retryOptions;

    public DefaultJobFailureHandler(
        ILogger<DefaultJobFailureHandler> logger,
        IOptions<JobRetryOptions>? retryOptions = null)
    {
        _logger = logger;
        _retryOptions = retryOptions?.Value;
    }

    public Task HandleAsync(JobFailureContext context, CancellationToken ct = default)
    {
        _logger.LogError(context.Exception,
            "Job {JobId} ({JobType}) failed. Tenant={TenantId}, Org={OrgId}, User={UserId}, Attempt={Attempt}",
            context.JobId, context.JobType.Name, context.TenantId, context.OrganizationId, context.UserId,
            context.AttemptNumber);
        return Task.CompletedTask;
    }

    public bool ShouldRetry(JobFailureContext context)
        => _retryOptions?.MaxRetries > 0 && context.AttemptNumber < _retryOptions.MaxRetries;

    public TimeSpan? GetNextRetryDelay(JobFailureContext context, int attemptNumber)
    {
        if (_retryOptions?.InitialDelay == null) return null;
        var delay = _retryOptions.InitialDelay.Value * Math.Pow(_retryOptions.BackoffMultiplier, attemptNumber - 1);
        return _retryOptions.MaxDelay.HasValue
            ? TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds, _retryOptions.MaxDelay.Value.TotalMilliseconds))
            : delay;
    }
}
