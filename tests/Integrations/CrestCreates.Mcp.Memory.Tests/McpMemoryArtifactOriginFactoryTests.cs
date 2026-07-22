using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Mcp.Memory.Security;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Mcp.Memory.Tests;

public class McpMemoryArtifactOriginFactoryTests
{
    private readonly McpMemoryArtifactOriginFactory _factory = new();

    private static CapabilityExecutionContext CreateContext(
        string? tenantId = "tenant-1",
        string? userId = "user-1",
        string hostId = "host-1",
        string sessionId = "session-1",
        string invocationId = "inv-1",
        string requestId = "req-1",
        string toolDescriptorId = "td-1",
        int toolDescriptorVersion = 1,
        string capabilityId = "cap-1",
        int capabilityVersion = 1)
    {
        var context = new CapabilityExecutionContext
        {
            TenantId = tenantId,
            UserId = userId,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            Items = new Dictionary<string, object?>
            {
                [McpCapabilityContextItemNames.HostId] = hostId,
                [McpCapabilityContextItemNames.SessionId] = sessionId,
                [McpCapabilityContextItemNames.InvocationId] = invocationId,
                [McpCapabilityContextItemNames.RequestId] = requestId,
                [McpCapabilityContextItemNames.ToolDescriptorId] = toolDescriptorId,
                [McpCapabilityContextItemNames.ToolDescriptorVersion] = toolDescriptorVersion,
                [McpCapabilityContextItemNames.CapabilityId] = capabilityId,
                [McpCapabilityContextItemNames.CapabilityVersion] = capabilityVersion,
            },
        };
        return context;
    }

    [Fact]
    public void CreatePrincipal_ReadsTenantId_FromContextProperty()
    {
        var context = CreateContext(tenantId: "t-1");
        var principal = _factory.CreatePrincipal(context);
        principal.TenantId.Should().Be("t-1");
    }

    [Fact]
    public void CreatePrincipal_ReadsUserId_FromContextProperty()
    {
        var context = CreateContext(userId: "u-1");
        var principal = _factory.CreatePrincipal(context);
        principal.UserId.Should().Be("u-1");
    }

    [Fact]
    public void CreatePrincipal_SetsCallerKind_ToMcp()
    {
        var context = CreateContext();
        var principal = _factory.CreatePrincipal(context);
        principal.CallerKind.Should().Be(AgentMemoryCallerKind.Mcp);
    }

    [Fact]
    public void CreatePrincipal_ReadsCallerId_FromHostIdItem()
    {
        var context = CreateContext(hostId: "host-x");
        var principal = _factory.CreatePrincipal(context);
        principal.CallerId.Should().Be("host-x");
    }

    [Fact]
    public void CreatePrincipal_ReadsSecurityContextId_FromSessionIdItem()
    {
        var context = CreateContext(sessionId: "sess-x");
        var principal = _factory.CreatePrincipal(context);
        principal.SecurityContextId.Should().Be("sess-x");
    }

    [Fact]
    public void CreatePrincipal_Throws_WhenTenantIdIsNull()
    {
        var context = CreateContext(tenantId: null);
        var act = () => _factory.CreatePrincipal(context);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TenantId*");
    }

    [Fact]
    public void CreatePrincipal_Throws_WhenUserIdIsNull()
    {
        var context = CreateContext(userId: null);
        var act = () => _factory.CreatePrincipal(context);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*UserId*");
    }

    [Fact]
    public void CreateInvocationOrigin_UsesMcpCapabilityContextItemNames_Constants()
    {
        var context = CreateContext();
        var origin = _factory.CreateInvocationOrigin(context);

        origin.Kind.Should().Be(AgentMemoryArtifactOriginKind.McpInvocation);
        origin.OperationId.Should().Be("inv-1");
        origin.BindingHash.Should().NotBeNull();
        origin.BindingHash.Value.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CreateInvocationOrigin_BindingHash_Changes_WithDifferentTenantId()
    {
        var ctx1 = CreateContext(tenantId: "t-a");
        var ctx2 = CreateContext(tenantId: "t-b");

        var hash1 = _factory.CreateInvocationOrigin(ctx1).BindingHash.Value;
        var hash2 = _factory.CreateInvocationOrigin(ctx2).BindingHash.Value;

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void CreateInvocationOrigin_BindingHash_Changes_WithDifferentInvocationId()
    {
        var ctx1 = CreateContext(invocationId: "inv-a");
        var ctx2 = CreateContext(invocationId: "inv-b");

        var hash1 = _factory.CreateInvocationOrigin(ctx1).BindingHash.Value;
        var hash2 = _factory.CreateInvocationOrigin(ctx2).BindingHash.Value;

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void CreateInvocationOrigin_Throws_WhenTenantIdIsMissing()
    {
        var context = CreateContext(tenantId: null);
        var act = () => _factory.CreateInvocationOrigin(context);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TenantId*");
    }

    [Fact]
    public void CreateInvocationOrigin_Throws_WhenHostIdIsMissing()
    {
        var context = CreateContext();
        context.Items.Remove(McpCapabilityContextItemNames.HostId);
        var act = () => _factory.CreateInvocationOrigin(context);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*McpHostId*");
    }

    [Fact]
    public void CreateInvocationOrigin_Throws_WhenToolDescriptorVersionIsZero()
    {
        var context = CreateContext(toolDescriptorVersion: 0);
        var act = () => _factory.CreateInvocationOrigin(context);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*positive integer*");
    }

    [Fact]
    public void CreateInvocationOrigin_Throws_WhenCapabilityVersionIsNegative()
    {
        var context = CreateContext(capabilityVersion: -1);
        var act = () => _factory.CreateInvocationOrigin(context);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*positive integer*");
    }

    [Fact]
    public void CreateSessionOperationOrigin_GeneratesDeterministicBindingHash()
    {
        var context = CreateContext();
        var principal = _factory.CreatePrincipal(context);

        var origin1 = _factory.CreateSessionOperationOrigin(principal, "op-1");
        var origin2 = _factory.CreateSessionOperationOrigin(principal, "op-1");

        origin1.BindingHash.Value.Should().Be(origin2.BindingHash.Value);
    }

    [Fact]
    public void CreateSessionOperationOrigin_DifferentSessionOperationId_GeneratesDifferentHash()
    {
        var context = CreateContext();
        var principal = _factory.CreatePrincipal(context);

        var origin1 = _factory.CreateSessionOperationOrigin(principal, "op-a");
        var origin2 = _factory.CreateSessionOperationOrigin(principal, "op-b");

        origin1.BindingHash.Value.Should().NotBe(origin2.BindingHash.Value);
    }

    [Fact]
    public void CreateSessionOperationOrigin_Throws_WhenSessionOperationIdIsNull()
    {
        var context = CreateContext();
        var principal = _factory.CreatePrincipal(context);
        var act = () => _factory.CreateSessionOperationOrigin(principal, null!);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*sessionOperationId*");
    }

    [Fact]
    public void CreateSessionOperationOrigin_Throws_WhenSessionOperationIdIsWhitespace()
    {
        var context = CreateContext();
        var principal = _factory.CreatePrincipal(context);
        var act = () => _factory.CreateSessionOperationOrigin(principal, "  ");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*sessionOperationId*");
    }

    [Fact]
    public void BindingHash_IsAlwaysSha256()
    {
        var context = CreateContext();
        var origin = _factory.CreateInvocationOrigin(context);
        origin.BindingHash.Algorithm.Should().Be("SHA-256");
        origin.BindingHash.Value.Should().HaveLength(64); // 256-bit hex
    }
}
