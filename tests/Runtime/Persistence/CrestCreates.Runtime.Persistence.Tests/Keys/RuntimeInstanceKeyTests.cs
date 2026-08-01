using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Runtime.Persistence.Tests.Keys;

public sealed class RuntimeInstanceKeyTests
{
    [Fact]
    public void RuntimeInstanceKey_Should_RequireExplicitTenantScope()
    {
        var host = new RuntimeInstanceKey(null, "same-id");
        var tenant = new RuntimeInstanceKey("tenant-a", "same-id");

        host.Should().NotBe(tenant);
        host.TenantId.Should().BeNull();
        tenant.TenantId.Should().Be("tenant-a");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RuntimeInstanceKey_Should_RejectBlankInstanceId(string instanceId)
    {
        var act = () => new RuntimeInstanceKey(null, instanceId);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RuntimeInstanceKey_DefaultValue_ShouldBeRejectedAtStoreBoundary()
    {
        var key = default(RuntimeInstanceKey);

        var act = key.EnsureValid;

        act.Should().Throw<ArgumentException>();
    }
}
