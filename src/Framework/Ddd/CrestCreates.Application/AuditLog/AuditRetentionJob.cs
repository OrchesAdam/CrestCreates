using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.AuditRetention;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Scheduling.Jobs;

namespace CrestCreates.Application.AuditLog;

public sealed class AuditRetentionJobArgs : IJobArgs
{
    public string StoreId { get; set; } = string.Empty;
    public string? TenantId { get; set; }
    public string ReasonCode { get; set; } = "retention.scheduled";
}

/// <summary>
/// Typed retention entry point for the existing scheduler. It calls the same Application admission
/// as HTTP and direct DI callers. The current scheduler does not establish an authenticated
/// principal, so automatic registration stays off until that platform gap is closed; callers that
/// already hold a trusted identity may invoke it through ISchedulerService.
/// </summary>
public sealed class AuditRetentionJob : IJob<AuditRetentionJobArgs>
{
    private readonly IAuditRetentionAppService _retention;

    public AuditRetentionJob(IAuditRetentionAppService retention)
        => _retention = retention ?? throw new System.ArgumentNullException(nameof(retention));

    public async Task ExecuteAsync(JobExecutionContext<AuditRetentionJobArgs> context, CancellationToken ct = default)
    {
        var occurrence = context.ScheduledAt.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        await _retention.ExecuteAsync(
            new AuditRetentionExecuteInput
            {
                StoreId = context.Args.StoreId,
                TenantId = context.Args.TenantId,
                RequestId = $"retention-job-{context.JobId.Uuid:N}-{occurrence}",
                ReasonCode = context.Args.ReasonCode
            },
            ct);
    }
}
