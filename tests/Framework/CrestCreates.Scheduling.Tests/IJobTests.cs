using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Scheduling.Jobs;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Scheduling.Tests;

public class IJobTests
{
    [Fact]
    public void NoArgs_ShouldImplementIJobArgs()
    {
        // Arrange & Act
        var noArgs = new NoArgs();

        // Assert
        noArgs.Should().BeAssignableTo<IJobArgs>();
    }

    [Fact]
    public async Task IJob_ImplementingClass_ShouldBeUsable()
    {
        // Arrange
        var job = new TestJob();
        var context = new JobExecutionContext<NoArgs>(
            Args: new NoArgs(),
            JobId: JobId.New(),
            TenantId: null,
            OrganizationId: null,
            UserId: null,
            ScheduledAt: DateTimeOffset.UtcNow,
            CancellationToken: CancellationToken.None
        );

        // Act
        var task = job.ExecuteAsync(context);

        // Assert
        await task;
        task.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task IJobOfT_ShouldAcceptTypedArgs()
    {
        // Arrange
        var job = new TypedJob();
        var args = new TypedJobArgs { Value = 42 };
        var context = new JobExecutionContext<TypedJobArgs>(
            Args: args,
            JobId: JobId.New(),
            TenantId: null,
            OrganizationId: null,
            UserId: null,
            ScheduledAt: DateTimeOffset.UtcNow,
            CancellationToken: CancellationToken.None
        );

        // Act
        await job.ExecuteAsync(context);

        // Assert
        job.ReceivedArgs.Should().Be(args);
    }

    private class TestJob : IJob
    {
        public Task ExecuteAsync(JobExecutionContext<NoArgs> context, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private class TypedJob : IJob<TypedJobArgs>
    {
        public TypedJobArgs? ReceivedArgs { get; private set; }

        public Task ExecuteAsync(JobExecutionContext<TypedJobArgs> context, CancellationToken ct = default)
        {
            ReceivedArgs = context.Args;
            return Task.CompletedTask;
        }
    }

    private record TypedJobArgs : IJobArgs
    {
        public int Value { get; init; }
    }
}
