using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Accountability.Abstractions.Retention;
using CrestCreates.Application.AuditLog;
using CrestCreates.Application.Contracts.AuditRetention;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Scheduling.Jobs;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.AuditLog;

public sealed class AuditRetentionJobTests
{
    private static readonly DateTimeOffset ScheduledAt = new(2026, 10, 27, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Job_maps_args_to_the_same_admission_with_a_stable_request_id()
    {
        AuditRetentionExecuteInput? captured = null;
        var service = new Mock<IAuditRetentionAppService>();
        service
            .Setup(app => app.ExecuteAsync(It.IsAny<AuditRetentionExecuteInput>(), It.IsAny<CancellationToken>()))
            .Callback((AuditRetentionExecuteInput input, CancellationToken _) => captured = input)
            .ReturnsAsync(new AuditRetentionRunDto());
        var job = new AuditRetentionJob(service.Object);
        var jobId = Guid.NewGuid();

        await job.ExecuteAsync(Context(jobId));

        captured.Should().NotBeNull();
        captured!.StoreId.Should().Be("store-1");
        captured.TenantId.Should().Be("tenant-1");
        captured.ReasonCode.Should().Be("retention.scheduled");
        captured.RequestId.Should().Be($"retention-job-{jobId:N}-20261027123000");
        service.Verify(
            app => app.ExecuteAsync(It.IsAny<AuditRetentionExecuteInput>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Job_reuses_the_request_id_for_the_same_occurrence()
    {
        var inputs = new System.Collections.Generic.List<AuditRetentionExecuteInput>();
        var service = new Mock<IAuditRetentionAppService>();
        service
            .Setup(app => app.ExecuteAsync(It.IsAny<AuditRetentionExecuteInput>(), It.IsAny<CancellationToken>()))
            .Callback((AuditRetentionExecuteInput input, CancellationToken _) => inputs.Add(input))
            .ReturnsAsync(new AuditRetentionRunDto());
        var job = new AuditRetentionJob(service.Object);
        var jobId = Guid.NewGuid();

        await job.ExecuteAsync(Context(jobId));
        await job.ExecuteAsync(Context(jobId));

        inputs.Should().HaveCount(2);
        inputs[0].RequestId.Should().Be(inputs[1].RequestId);
    }

    [Fact]
    public async Task Job_does_not_swallow_admission_rejection()
    {
        var service = new Mock<IAuditRetentionAppService>();
        service
            .Setup(app => app.ExecuteAsync(It.IsAny<AuditRetentionExecuteInput>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuditRetentionException(
                AuditRetentionErrorCodes.IdentityRequired,
                "Audit retention requires an authenticated identity."));
        var job = new AuditRetentionJob(service.Object);

        var act = () => job.ExecuteAsync(Context(Guid.NewGuid()));

        (await act.Should().ThrowAsync<AuditRetentionException>())
            .Which.Code.Should().Be(AuditRetentionErrorCodes.IdentityRequired);
    }

    private static JobExecutionContext<AuditRetentionJobArgs> Context(Guid jobId) => new(
        new AuditRetentionJobArgs
        {
            StoreId = "store-1",
            TenantId = "tenant-1",
            ReasonCode = "retention.scheduled"
        },
        new JobId(jobId),
        null,
        null,
        null,
        ScheduledAt,
        CancellationToken.None);
}
