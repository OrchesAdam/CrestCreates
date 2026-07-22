using CrestCreates.Agent.Memory.Projection.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Projection.Tests;

public class AgentMemoryAccessPrincipalTests
{
    [Fact]
    public void Principal_RecordEquality_RequiresAllFields()
    {
        var principal1 = new AgentMemoryAccessPrincipal
        {
            TenantId = "t1",
            UserId = "u1",
            CallerKind = AgentMemoryCallerKind.AgentTool,
            CallerId = "host1",
            SecurityContextId = "session1"
        };

        var principal2 = new AgentMemoryAccessPrincipal
        {
            TenantId = "t1",
            UserId = "u1",
            CallerKind = AgentMemoryCallerKind.AgentTool,
            CallerId = "host1",
            SecurityContextId = "session1"
        };

        principal1.Should().Be(principal2);
        (principal1 == principal2).Should().BeTrue();
        principal1.GetHashCode().Should().Be(principal2.GetHashCode());
    }

    [Fact]
    public void Principal_DifferentUserId_NotEqual()
    {
        var principal1 = new AgentMemoryAccessPrincipal
        {
            TenantId = "t1",
            UserId = "u1",
            CallerKind = AgentMemoryCallerKind.AgentTool,
            CallerId = "host1",
            SecurityContextId = "session1"
        };

        var principal2 = new AgentMemoryAccessPrincipal
        {
            TenantId = "t1",
            UserId = "u2",
            CallerKind = AgentMemoryCallerKind.AgentTool,
            CallerId = "host1",
            SecurityContextId = "session1"
        };

        principal1.Should().NotBe(principal2);
    }

    [Fact]
    public void Principal_DifferentCallerKind_NotEqual()
    {
        var principal1 = new AgentMemoryAccessPrincipal
        {
            TenantId = "t1",
            UserId = "u1",
            CallerKind = AgentMemoryCallerKind.AgentTool,
            CallerId = "host1",
            SecurityContextId = "session1"
        };

        var principal2 = new AgentMemoryAccessPrincipal
        {
            TenantId = "t1",
            UserId = "u1",
            CallerKind = AgentMemoryCallerKind.Mcp,
            CallerId = "host1",
            SecurityContextId = "session1"
        };

        principal1.Should().NotBe(principal2);
    }

    [Fact]
    public void Principal_DifferentSecurityContextId_NotEqual()
    {
        var principal1 = new AgentMemoryAccessPrincipal
        {
            TenantId = "t1",
            UserId = "u1",
            CallerKind = AgentMemoryCallerKind.AgentTool,
            CallerId = "host1",
            SecurityContextId = "session1"
        };

        var principal2 = new AgentMemoryAccessPrincipal
        {
            TenantId = "t1",
            UserId = "u1",
            CallerKind = AgentMemoryCallerKind.AgentTool,
            CallerId = "host1",
            SecurityContextId = "session2"
        };

        principal1.Should().NotBe(principal2);
    }
}
