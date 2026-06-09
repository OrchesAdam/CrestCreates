using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class TenantMiddlewareTests
{
    private sealed class TestTenantContext : ITenantContext
    {
        public string? CurrentTenantId { get; set; }
    }

    [Fact]
    public async Task SetsTenantId_OnContext()
    {
        var tenantCtx = new TestTenantContext { CurrentTenantId = "tenant_01" };
        var middleware = new TenantMiddleware(tenantCtx);
        var context = new CapabilityExecutionContext
        {
            CapabilityName = "test", CapabilityVersion = 1, CapabilityContractHash = "abc"
        };

        await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success("ok", TimeSpan.Zero)));

        context.TenantId.Should().Be("tenant_01");
    }

    [Fact]
    public async Task Passthrough_WhenNoTenantContext()
    {
        var middleware = new TenantMiddleware(null);
        var context = new CapabilityExecutionContext
        {
            CapabilityName = "test", CapabilityVersion = 1, CapabilityContractHash = "abc"
        };

        var result = await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success("ok", TimeSpan.Zero)));

        result.IsSuccess.Should().BeTrue();
        context.TenantId.Should().BeNull();
    }
}