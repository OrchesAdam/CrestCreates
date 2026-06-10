using CrestCreates.Capability.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class InMemoryCapabilityAuditStoreTests
{
    [Fact]
    public async Task RecordAsync_EnqueuesRecord()
    {
        var store = new InMemoryCapabilityAuditStore();
        var record = new CapabilityExecutionRecord
        {
            ExecutionId = "exec_01",
            CapabilityId = "test.cap",
            CapabilityName = "Test Cap",
            IsSuccess = true
        };

        await store.RecordAsync(record);

        store.GetRecords().Should().ContainSingle()
            .Which.ExecutionId.Should().Be("exec_01");
    }

    [Fact]
    public async Task GetRecords_ReturnsAllRecordedItems()
    {
        var store = new InMemoryCapabilityAuditStore();
        var record1 = new CapabilityExecutionRecord { ExecutionId = "exec_01", CapabilityId = "cap_a" };
        var record2 = new CapabilityExecutionRecord { ExecutionId = "exec_02", CapabilityId = "cap_b" };

        await store.RecordAsync(record1);
        await store.RecordAsync(record2);

        store.GetRecords().Should().HaveCount(2);
    }

    [Fact]
    public void Clear_EmptiesStore()
    {
        var store = new InMemoryCapabilityAuditStore();
        store.RecordAsync(new CapabilityExecutionRecord { ExecutionId = "exec_01" });

        store.Clear();

        store.GetRecords().Should().BeEmpty();
    }
}
