using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public sealed class DescriptorKindNamesTests
{
    [Fact]
    public void McpTool_has_stable_canonical_name()
        => DescriptorKindNames.ToCanonicalString(DescriptorKind.McpTool)
            .Should().Be("McpTool");
}
