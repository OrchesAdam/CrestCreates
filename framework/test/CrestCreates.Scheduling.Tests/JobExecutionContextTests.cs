using System;
using System.Threading;
using CrestCreates.Scheduling.Jobs;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Scheduling.Tests;

public class JobExecutionContextTests
{
    [Fact]
    public void Context_ShouldStoreAllProperties()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var scheduledAt = DateTimeOffset.UtcNow;
        var cts = new CancellationToken();
        var jobId = JobId.New();
        var args = new TestJobArgs { Value = 42 };

        // Act
        var context = new JobExecutionContext<TestJobArgs>(
            Args: args,
            JobId: jobId,
            TenantId: tenantId,
            OrganizationId: orgId,
            UserId: userId,
            ScheduledAt: scheduledAt,
            CancellationToken: cts
        );

        // Assert
        context.Args.Should().Be(args);
        context.JobId.Should().Be(jobId);
        context.TenantId.Should().Be(tenantId);
        context.OrganizationId.Should().Be(orgId);
        context.UserId.Should().Be(userId);
        context.ScheduledAt.Should().Be(scheduledAt);
        context.CancellationToken.Should().Be(cts);
    }

    [Fact]
    public void Context_WithNullTenantId_ShouldAllowNull()
    {
        // Arrange
        var jobId = JobId.New();
        var args = new TestJobArgs { Value = 0 };

        // Act
        var context = new JobExecutionContext<TestJobArgs>(
            Args: args,
            JobId: jobId,
            TenantId: null,
            OrganizationId: null,
            UserId: null,
            ScheduledAt: DateTimeOffset.UtcNow,
            CancellationToken: CancellationToken.None
        );

        // Assert
        context.TenantId.Should().BeNull();
        context.OrganizationId.Should().BeNull();
        context.UserId.Should().BeNull();
    }

    [Fact]
    public void Context_WithNoArgs_ShouldUseNoArgs()
    {
        // Arrange
        var jobId = JobId.New();

        // Act
        var context = new JobExecutionContext<NoArgs>(
            Args: new NoArgs(),
            JobId: jobId,
            TenantId: null,
            OrganizationId: null,
            UserId: null,
            ScheduledAt: DateTimeOffset.UtcNow,
            CancellationToken: CancellationToken.None
        );

        // Assert
        context.Args.Should().Be(new NoArgs());
    }

    [Fact]
    public void Context_ShouldBeRecord_AndEqualWhenSameValues()
    {
        // Arrange
        var jobId = JobId.New();
        var args = new TestJobArgs { Value = 100 };
        var scheduledAt = DateTimeOffset.UtcNow;

        // Act
        var context1 = new JobExecutionContext<TestJobArgs>(args, jobId, null, null, null, scheduledAt, CancellationToken.None);
        var context2 = new JobExecutionContext<TestJobArgs>(args, jobId, null, null, null, scheduledAt, CancellationToken.None);

        // Assert
        context1.Should().Be(context2);
    }

    private record TestJobArgs : IJobArgs
    {
        public int Value { get; init; }
    }
}
