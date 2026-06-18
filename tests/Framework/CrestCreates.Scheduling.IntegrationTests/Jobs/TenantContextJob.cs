namespace CrestCreates.Scheduling.IntegrationTests.Jobs;

public class TenantContextJob : CrestCreates.Scheduling.Jobs.IJob
{
    public Guid? ReceivedTenantId { get; private set; }
    public Guid? ReceivedOrganizationId { get; private set; }
    public Guid? ReceivedUserId { get; private set; }

    public Task ExecuteAsync(CrestCreates.Scheduling.Jobs.JobExecutionContext<CrestCreates.Scheduling.Jobs.NoArgs> context, CancellationToken ct)
    {
        ReceivedTenantId = context.TenantId;
        ReceivedOrganizationId = context.OrganizationId;
        ReceivedUserId = context.UserId;
        return Task.CompletedTask;
    }
}
