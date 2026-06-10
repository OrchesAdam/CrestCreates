using CrestCreates.Capability.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityExecutionContextTests
{
    [Fact]
    public void Context_Defaults_CorrelationId_To_New_Guid()
    {
        var ctx = new CapabilityExecutionContext
        {
            CapabilityName = "test.cap",
            CapabilityVersion = 1,
            CapabilityContractHash = "abc123"
        };

        ctx.CorrelationId.Should().NotBeNullOrEmpty();
        ctx.CorrelationId.Length.Should().Be(32);
    }

    [Fact]
    public void Context_Defaults_IdempotencyKey_To_New_Guid()
    {
        var ctx = new CapabilityExecutionContext
        {
            CapabilityName = "test.cap",
            CapabilityVersion = 1,
            CapabilityContractHash = "abc123"
        };

        ctx.IdempotencyKey.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Context_Items_Bag_Is_Mutable()
    {
        var ctx = new CapabilityExecutionContext
        {
            CapabilityName = "test.cap",
            CapabilityVersion = 1,
            CapabilityContractHash = "abc123"
        };

        ctx.Items["key"] = "value";
        ctx.Items["key"].Should().Be("value");
    }

    [Fact]
    public void Context_ConfigureContext_Overrides_Defaults()
    {
        var ctx = new CapabilityExecutionContext
        {
            CapabilityName = "test.cap",
            CapabilityVersion = 1,
            CapabilityContractHash = "abc123"
        };

        Action<CapabilityExecutionContext> configure = c =>
        {
            c.UserId = "user_01";
            c.TenantId = "tenant_01";
            c.CausationId = "cause_01";
        };
        configure(ctx);

        ctx.UserId.Should().Be("user_01");
        ctx.TenantId.Should().Be("tenant_01");
        ctx.CausationId.Should().Be("cause_01");
    }

    [Fact]
    public void Has_CapabilityId_and_InvocationSource_fields()
    {
        var ctx = new CapabilityExecutionContext
        {
            CapabilityId = "customer.create",
            CapabilityName = "Create Customer",
            InvocationSource = InvocationSource.Workflow
        };

        ctx.CapabilityId.Should().Be("customer.create");
        ctx.InvocationSource.Should().Be(InvocationSource.Workflow);
    }

    [Fact]
    public void Context_StartedAt_Is_Set_On_Creation()
    {
        var before = DateTimeOffset.UtcNow;
        var ctx = new CapabilityExecutionContext
        {
            CapabilityName = "test.cap",
            CapabilityVersion = 1,
            CapabilityContractHash = "abc123"
        };
        var after = DateTimeOffset.UtcNow;

        ctx.StartedAt.Should().BeOnOrAfter(before);
        ctx.StartedAt.Should().BeOnOrBefore(after);
    }
}
