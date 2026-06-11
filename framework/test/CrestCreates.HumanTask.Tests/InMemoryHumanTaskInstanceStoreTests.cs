using CrestCreates.HumanTask.Abstractions;
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
}
