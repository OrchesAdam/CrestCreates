using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Mcp.Memory.Security;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Mcp.Memory.Tests;

public class McpMemoryScopeProviderValidatorTests
{
    [Fact]
    public void Validate_Succeeds_WhenScopeProviderSupportsMcp()
    {
        var mockProvider = new Mock<IAgentMemoryAccessScopeProvider>();
        mockProvider.As<IAgentMemoryAccessScopeProviderCapabilities>()
            .Setup(c => c.Supports(AgentMemoryCallerKind.Mcp))
            .Returns(true);

        var validator = new McpMemoryScopeProviderValidator(mockProvider.Object);
        var report = validator.Validate();

        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void Validate_Fails_WhenScopeProviderDoesNotSupportMcp()
    {
        var mockProvider = new Mock<IAgentMemoryAccessScopeProvider>();
        mockProvider.As<IAgentMemoryAccessScopeProviderCapabilities>()
            .Setup(c => c.Supports(AgentMemoryCallerKind.Mcp))
            .Returns(false);

        var validator = new McpMemoryScopeProviderValidator(mockProvider.Object);
        var report = validator.Validate();

        report.HasErrors.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenScopeProviderHasNoCapabilities()
    {
        // Provider doesn't implement IAgentMemoryAccessScopeProviderCapabilities — fail-closed
        var mockProvider = new Mock<IAgentMemoryAccessScopeProvider>();

        var validator = new McpMemoryScopeProviderValidator(mockProvider.Object);
        var report = validator.Validate();

        report.HasErrors.Should().BeTrue();
    }
}
