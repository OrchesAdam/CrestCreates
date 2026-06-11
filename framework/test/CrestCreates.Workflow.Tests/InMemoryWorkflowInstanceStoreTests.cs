using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class InMemoryWorkflowInstanceStoreTests
{
    private static WorkflowInstance CreateInstance(string instanceId, WorkflowInstanceStatus status)
    {
        return new WorkflowInstance
        {
            InstanceId = instanceId,
            Workflow = new VersionedDescriptorRef<WorkflowDescriptor>("wf_01", 1),
            Status = status
        };
    }

    [Fact]
    public async Task Save_UpdatesConcurrencyStamp()
    {
        var store = new InMemoryWorkflowInstanceStore();
        var instance = CreateInstance("inst-01", WorkflowInstanceStatus.Running);

        // First save
        await store.SaveAsync(instance);
        instance.ConcurrencyStamp.Should().NotBeNullOrEmpty();
        instance.UpdatedAt.Should().NotBeNull();
        var firstStamp = instance.ConcurrencyStamp;
        var firstUpdatedAt = instance.UpdatedAt;

        // Second save — stamp and UpdatedAt should change
        instance.Status = WorkflowInstanceStatus.Suspended;
        await store.SaveAsync(instance);
        instance.ConcurrencyStamp.Should().NotBe(firstStamp);
        instance.UpdatedAt.Should().NotBe(firstUpdatedAt);
    }

    [Fact]
    public async Task Save_Throws_On_StaleConcurrencyStamp()
    {
        var store = new InMemoryWorkflowInstanceStore();
        var instance = CreateInstance("inst-01", WorkflowInstanceStatus.Running);

        // Save once to establish a stamp
        await store.SaveAsync(instance);

        // Read back two copies (both have the same stamp from the first save)
        var copy1 = await store.GetAsync("inst-01");
        var copy2 = await store.GetAsync("inst-01");
        copy1.Should().NotBeNull();
        copy2.Should().NotBeNull();
        copy1!.ConcurrencyStamp.Should().Be(copy2!.ConcurrencyStamp);

        // Modify and save copy1 — this succeeds and generates a new stamp
        copy1.Status = WorkflowInstanceStatus.Suspended;
        await store.SaveAsync(copy1);

        // Try to save copy2 with the old stamp — should fail
        copy2.Status = WorkflowInstanceStatus.Failed;
        await store.Invoking(s => s.SaveAsync(copy2))
            .Should().ThrowAsync<RuntimeConcurrencyException>();
    }

    [Fact]
    public async Task Save_Concurrent_Writes_Detect_Conflict()
    {
        var store = new InMemoryWorkflowInstanceStore();
        var instance = CreateInstance("inst-01", WorkflowInstanceStatus.Running);

        // First save to establish stamp
        await store.SaveAsync(instance);

        // Read back two independent clones (both have stamp A)
        var copy1 = await store.GetAsync("inst-01");
        var copy2 = await store.GetAsync("inst-01");
        copy1.Should().NotBeNull();
        copy2.Should().NotBeNull();

        int successCount = 0;
        int failureCount = 0;
        Exception? failureException = null;

        // Barrier ensures both tasks enter SaveAsync simultaneously
        using var barrier = new Barrier(2);

        var task1 = Task.Run(async () =>
        {
            try
            {
                copy1!.Status = WorkflowInstanceStatus.Suspended;
                barrier.SignalAndWait();
                await store.SaveAsync(copy1);
                Interlocked.Increment(ref successCount);
            }
            catch (RuntimeConcurrencyException ex)
            {
                Interlocked.Increment(ref failureCount);
                failureException = ex;
            }
        });

        var task2 = Task.Run(async () =>
        {
            try
            {
                copy2!.Status = WorkflowInstanceStatus.Failed;
                barrier.SignalAndWait();
                await store.SaveAsync(copy2);
                Interlocked.Increment(ref successCount);
            }
            catch (RuntimeConcurrencyException ex)
            {
                Interlocked.Increment(ref failureCount);
                failureException = ex;
            }
        });

        await Task.WhenAll(task1, task2);

        // Exactly one must succeed, one must fail
        successCount.Should().Be(1);
        failureCount.Should().Be(1);
        failureException.Should().BeOfType<RuntimeConcurrencyException>();

        // Final stored state equals the successful write (no merge, no lost update)
        var final = await store.GetAsync("inst-01");
        final.Should().NotBeNull();
        (final!.Status == WorkflowInstanceStatus.Suspended ||
         final.Status == WorkflowInstanceStatus.Failed).Should().BeTrue();
    }

    [Fact]
    public async Task GetByWaitingHumanTaskId_Returns_SuspendedOnly()
    {
        var store = new InMemoryWorkflowInstanceStore();
        var humansTaskId = "human-task-instance-123";

        var running = CreateInstance("wf-running", WorkflowInstanceStatus.Running);
        running.WaitingHumanTaskId = humansTaskId;
        var suspended = CreateInstance("wf-suspended", WorkflowInstanceStatus.Suspended);
        suspended.WaitingHumanTaskId = humansTaskId;
        var completed = CreateInstance("wf-completed", WorkflowInstanceStatus.Completed);
        completed.WaitingHumanTaskId = humansTaskId;
        var failed = CreateInstance("wf-failed", WorkflowInstanceStatus.Failed);
        failed.WaitingHumanTaskId = humansTaskId;

        await store.SaveAsync(running);
        await store.SaveAsync(suspended);
        await store.SaveAsync(completed);
        await store.SaveAsync(failed);

        var result = await store.GetByWaitingHumanTaskId(humansTaskId);

        result.Should().NotBeNull();
        result!.InstanceId.Should().Be("wf-suspended");
        result.Status.Should().Be(WorkflowInstanceStatus.Suspended);
    }

    [Fact]
    public async Task GetByWaitingHumanTaskId_Throws_When_MultipleSuspendedMatches()
    {
        var store = new InMemoryWorkflowInstanceStore();
        var humansTaskId = "human-task-instance-456";

        var suspended1 = CreateInstance("wf-sus-1", WorkflowInstanceStatus.Suspended);
        suspended1.WaitingHumanTaskId = humansTaskId;
        var suspended2 = CreateInstance("wf-sus-2", WorkflowInstanceStatus.Suspended);
        suspended2.WaitingHumanTaskId = humansTaskId;

        await store.SaveAsync(suspended1);
        await store.SaveAsync(suspended2);

        await store.Invoking(s => s.GetByWaitingHumanTaskId(humansTaskId))
            .Should().ThrowAsync<WorkflowCorrelationException>();
    }
}
