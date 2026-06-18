using CrestCreates.Capability.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class NullCapabilityAuditStoreTests
{
    [Fact]
    public async Task RecordAsync_DoesNotThrow()
    {
        var store = new NullCapabilityAuditStore();

        var act = async () => await store.RecordAsync(new CapabilityExecutionRecord
        {
            ExecutionId = "exec_01",
            CapabilityId = "test.cap"
        });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RecordAsync_CompletesSuccessfully()
    {
        var store = new NullCapabilityAuditStore();

        await store.RecordAsync(new CapabilityExecutionRecord
        {
            ExecutionId = "exec_01",
            CapabilityId = "test.cap",
            IsSuccess = true,
            Duration = TimeSpan.FromMilliseconds(5)
        });
    }
}
