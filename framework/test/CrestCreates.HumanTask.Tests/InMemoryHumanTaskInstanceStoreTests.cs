using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.HumanTask.Tests;

public class InMemoryHumanTaskInstanceStoreTests
{
    [Fact]
    public async Task GetPendingByAssigneeAsync_Returns_Only_Open_Tasks()
    {
        var store = new InMemoryHumanTaskInstanceStore();

        var created = new HumanTaskInstance
        {
            Id = "inst-01", HumanTaskId = "ht_01", HumanTaskVersion = 1,
            Status = HumanTaskInstanceStatus.Created,
            AssigneeUserId = "user-a", CreatedAt = DateTimeOffset.UtcNow
        };
        var assigned = new HumanTaskInstance
        {
            Id = "inst-02", HumanTaskId = "ht_01", HumanTaskVersion = 1,
            Status = HumanTaskInstanceStatus.Assigned,
            AssigneeUserId = "user-a", CreatedAt = DateTimeOffset.UtcNow
        };
        var completed = new HumanTaskInstance
        {
            Id = "inst-03", HumanTaskId = "ht_01", HumanTaskVersion = 1,
            Status = HumanTaskInstanceStatus.Completed,
            AssigneeUserId = "user-a", CreatedAt = DateTimeOffset.UtcNow
        };
        var cancelled = new HumanTaskInstance
        {
            Id = "inst-04", HumanTaskId = "ht_01", HumanTaskVersion = 1,
            Status = HumanTaskInstanceStatus.Cancelled,
            AssigneeUserId = "user-a", CreatedAt = DateTimeOffset.UtcNow
        };
        var otherUser = new HumanTaskInstance
        {
            Id = "inst-05", HumanTaskId = "ht_01", HumanTaskVersion = 1,
            Status = HumanTaskInstanceStatus.Assigned,
            AssigneeUserId = "user-b", CreatedAt = DateTimeOffset.UtcNow
        };

        await store.SaveAsync(created);
        await store.SaveAsync(assigned);
        await store.SaveAsync(completed);
        await store.SaveAsync(cancelled);
        await store.SaveAsync(otherUser);

        var pending = await store.GetPendingByAssigneeAsync("user-a");

        pending.Should().HaveCount(2);
        pending.Should().Contain(i => i.Id == "inst-01");
        pending.Should().Contain(i => i.Id == "inst-02");
        pending.Should().NotContain(i => i.Id == "inst-03");
        pending.Should().NotContain(i => i.Id == "inst-04");
        pending.Should().NotContain(i => i.Id == "inst-05");
    }

    private static HumanTaskInstance CreateInstance(
        string id, HumanTaskInstanceStatus status, string? assigneeUserId = null,
        string? workflowInstanceId = null)
    {
        return new HumanTaskInstance
        {
            Id = id,
            HumanTaskId = "ht_01",
            HumanTaskVersion = 1,
            Status = status,
            AssigneeUserId = assigneeUserId,
            WorkflowInstanceId = workflowInstanceId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    [Fact]
    public async Task Save_UpdatesConcurrencyStamp()
    {
        var store = new InMemoryHumanTaskInstanceStore();
        var instance = CreateInstance("inst-01", HumanTaskInstanceStatus.Created);

        // First save
        await store.SaveAsync(instance);
        instance.ConcurrencyStamp.Should().NotBeNullOrEmpty();
        instance.UpdatedAt.Should().NotBeNull();
        var firstStamp = instance.ConcurrencyStamp;
        var firstUpdatedAt = instance.UpdatedAt;

        // Second save — stamp and UpdatedAt should change
        instance.Status = HumanTaskInstanceStatus.Assigned;
        await store.SaveAsync(instance);
        instance.ConcurrencyStamp.Should().NotBe(firstStamp);
        instance.UpdatedAt.Should().NotBe(firstUpdatedAt);
    }

    [Fact]
    public async Task Save_Throws_On_StaleConcurrencyStamp()
    {
        var store = new InMemoryHumanTaskInstanceStore();
        var instance = CreateInstance("inst-01", HumanTaskInstanceStatus.Created);

        // Save once to establish a stamp
        await store.SaveAsync(instance);

        // Read back two copies
        var copy1 = await store.GetByIdAsync("inst-01");
        var copy2 = await store.GetByIdAsync("inst-01");
        copy1.Should().NotBeNull();
        copy2.Should().NotBeNull();
        copy1!.ConcurrencyStamp.Should().Be(copy2!.ConcurrencyStamp);

        // Modify and save copy1 — succeeds, generates new stamp
        copy1.Status = HumanTaskInstanceStatus.Assigned;
        await store.SaveAsync(copy1);

        // Try to save copy2 with old stamp — fails
        copy2.Status = HumanTaskInstanceStatus.Completed;
        await store.Invoking(s => s.SaveAsync(copy2))
            .Should().ThrowAsync<RuntimeConcurrencyException>();
    }

    [Fact]
    public async Task GetPendingByAssignee_Returns_OpenOnly()
    {
        var store = new InMemoryHumanTaskInstanceStore();

        var created = CreateInstance("inst-01", HumanTaskInstanceStatus.Created, "user-a");
        var assigned = CreateInstance("inst-02", HumanTaskInstanceStatus.Assigned, "user-a");
        var completed = CreateInstance("inst-03", HumanTaskInstanceStatus.Completed, "user-a");
        var cancelled = CreateInstance("inst-04", HumanTaskInstanceStatus.Cancelled, "user-a");
        var otherUser = CreateInstance("inst-05", HumanTaskInstanceStatus.Assigned, "user-b");

        await store.SaveAsync(created);
        await store.SaveAsync(assigned);
        await store.SaveAsync(completed);
        await store.SaveAsync(cancelled);
        await store.SaveAsync(otherUser);

        var pending = await store.GetPendingByAssigneeAsync("user-a");

        pending.Should().HaveCount(2);
        pending.Should().Contain(i => i.Id == "inst-01");
        pending.Should().Contain(i => i.Id == "inst-02");
        pending.Should().NotContain(i => i.Id == "inst-03");
        pending.Should().NotContain(i => i.Id == "inst-04");
        pending.Should().NotContain(i => i.Id == "inst-05");
    }

    [Fact]
    public async Task GetPendingByWorkflow_Returns_OpenOnly()
    {
        var store = new InMemoryHumanTaskInstanceStore();

        var created = CreateInstance("inst-01", HumanTaskInstanceStatus.Created,
            workflowInstanceId: "wf-001");
        var assigned = CreateInstance("inst-02", HumanTaskInstanceStatus.Assigned,
            workflowInstanceId: "wf-001");
        var completed = CreateInstance("inst-03", HumanTaskInstanceStatus.Completed,
            workflowInstanceId: "wf-001");
        var otherWf = CreateInstance("inst-04", HumanTaskInstanceStatus.Created,
            workflowInstanceId: "wf-002");

        await store.SaveAsync(created);
        await store.SaveAsync(assigned);
        await store.SaveAsync(completed);
        await store.SaveAsync(otherWf);

        var pending = await store.GetPendingByWorkflowAsync("wf-001");

        pending.Should().HaveCount(2);
        pending.Should().Contain(i => i.Id == "inst-01");
        pending.Should().Contain(i => i.Id == "inst-02");
        pending.Should().NotContain(i => i.Id == "inst-03");
        pending.Should().NotContain(i => i.Id == "inst-04");
    }
}
