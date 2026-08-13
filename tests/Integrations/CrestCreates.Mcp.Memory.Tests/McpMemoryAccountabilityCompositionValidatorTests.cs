using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Mcp.Memory.Security;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Mcp.Memory.Tests;

public class McpMemoryAccountabilityCompositionValidatorTests
{
    [Fact]
    public void Validate_MissingAccessor_ShouldReportAccessorMissing()
    {
        var validator = new McpMemoryAccountabilityCompositionValidator();

        var report = validator.Validate();

        report.HasErrors.Should().BeTrue();
        var issue = report.Issues.Should().ContainSingle().Subject;
        issue.Code!.Value.RequireValue().Should().Be("MCP_MEMORY_ACCOUNTABILITY_ACCESSOR_MISSING");
        issue.Message.Should().Contain("IAuditOperationContextAccessor");
    }

    [Fact]
    public async Task StartAsync_MissingAccessor_ShouldThrowAccessorMissing()
    {
        var validator = new McpMemoryAccountabilityCompositionValidator();

        await validator.Invoking(service => service.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*MCP_MEMORY_ACCOUNTABILITY_ACCESSOR_MISSING*")
            .WithMessage("*IAuditOperationContextAccessor*");
    }

    [Fact]
    public void Validate_PresentAccessor_ShouldPass()
    {
        var accessor = new Mock<IAuditOperationContextAccessor>();
        var validator = new McpMemoryAccountabilityCompositionValidator(accessor.Object);

        var report = validator.Validate();

        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_PresentAccessor_ShouldNotThrow()
    {
        var accessor = new Mock<IAuditOperationContextAccessor>();
        var validator = new McpMemoryAccountabilityCompositionValidator(accessor.Object);

        await validator.Invoking(service => service.StartAsync(CancellationToken.None))
            .Should().NotThrowAsync();
    }
}
