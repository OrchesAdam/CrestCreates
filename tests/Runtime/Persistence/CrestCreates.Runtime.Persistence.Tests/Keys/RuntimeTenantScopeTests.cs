using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Runtime.Persistence.Tests.Keys;

public sealed class RuntimeTenantScopeTests
{
    [Fact]
    public void RuntimeTenantScope_Null_ShouldMeanExactHostNotWildcard()
    {
        var host = new RuntimeTenantScope(null);
        var tenant = new RuntimeTenantScope("tenant-a");

        host.IsHost.Should().BeTrue();
        host.Should().NotBe(tenant);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RuntimeTenantScope_ShouldRejectBlankTenantId(string tenantId)
    {
        var act = () => new RuntimeTenantScope(tenantId);

        act.Should().Throw<ArgumentException>();
    }
}
