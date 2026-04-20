using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Scheduling.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.Scheduling.Services;

public class DefaultJobExecutionHandler : IJobExecutionHandler
{
    private readonly IJobHistoryRepository _historyRepository;
    private readonly JobRetryOptions? _retryOptions;
    private readonly ILogger<DefaultJobExecutionHandler> _logger;

    public DefaultJobExecutionHandler(
        IJobHistoryRepository historyRepository,
        IOptions<JobRetryOptions>? retryOptions = null,
        ILogger<DefaultJobExecutionHandler>? logger = null)
    {
        _historyRepository = historyRepository;
        _retryOptions = retryOptions?.Value;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DefaultJobExecutionHandler>.Instance;
    }

    public Task OnJobScheduledAsync(JobScheduledContext context, CancellationToken ct = default)
    {
        var record = new JobRecord
        {
            JobName = context.JobType.Name,
            JobUuid = context.JobId,
            TenantId = context.TenantId,
            OrganizationId = context.OrganizationId,
            UserId = context.UserId,
            ArgsJson = context.ArgsJson,
            Result = JobExecutionResult.Scheduled,
            AttemptNumber = 1
        };
        _logger.LogDebug("Job {JobId} scheduled", context.JobId);
        return _historyRepository.CreateAsync(record, ct);
    }

    public Task OnJobStartedAsync(JobStartedContext context, CancellationToken ct = default)
    {
        var record = new JobRecord
        {
            JobName = context.JobType.Name,
            JobUuid = context.JobId,
            TenantId = context.TenantId,
            OrganizationId = context.OrganizationId,
            UserId = context.UserId,
            ArgsJson = context.ArgsJson,
            Result = JobExecutionResult.Running,
            AttemptNumber = context.AttemptNumber,
            StartedAt = context.StartedAt
        };
        _logger.LogDebug("Job {JobId} started (attempt {Attempt})", context.JobId, context.AttemptNumber);
        return _historyRepository.CreateAsync(record, ct);
    }

    public Task OnJobSucceededAsync(JobSucceededContext context, CancellationToken ct = default)
    {
        var record = new JobRecord
        {
            JobName = context.JobType.Name,
            JobUuid = context.JobId,
            TenantId = context.TenantId,
            OrganizationId = context.OrganizationId,
            UserId = context.UserId,
            ArgsJson = context.ArgsJson,
            Result = JobExecutionResult.Succeeded,
            AttemptNumber = context.AttemptNumber,
            StartedAt = context.StartedAt,
            FinishedAt = context.FinishedAt
        };
        _logger.LogInformation("Job {JobId} succeeded in {Duration}", context.JobId, context.Duration);
        return _historyRepository.CreateAsync(record, ct);
    }

    public Task OnJobCancelledAsync(JobCancelledContext context, CancellationToken ct = default)
    {
        var record = new JobRecord
        {
            JobName = context.JobType.Name,
            JobUuid = context.JobId,
            TenantId = context.TenantId,
            OrganizationId = context.OrganizationId,
            UserId = context.UserId,
            ArgsJson = context.ArgsJson,
            Result = JobExecutionResult.Cancelled,
            AttemptNumber = context.AttemptNumber
        };
        _logger.LogWarning("Job {JobId} cancelled (attempt {Attempt})", context.JobId, context.AttemptNumber);
        return _historyRepository.CreateAsync(record, ct);
    }

    public Task HandleAsync(JobFailureContext context, CancellationToken ct = default)
    {
        var record = new JobRecord
        {
            JobName = context.JobType.Name,
            JobUuid = context.JobId.Uuid,
            TenantId = context.TenantId,
            OrganizationId = context.OrganizationId,
            UserId = context.UserId,
            ArgsJson = context.Args is IJobArgs args ? JsonSerializer.Serialize(args) : null,
            Result = JobExecutionResult.Failed,
            AttemptNumber = context.AttemptNumber,
            ErrorMessage = context.Exception.Message,
            StackTrace = context.Exception.StackTrace
        };
        _logger.LogError(context.Exception, "Job {JobId} failed (attempt {Attempt})", context.JobId, context.AttemptNumber);
        return _historyRepository.CreateAsync(record, ct);
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
