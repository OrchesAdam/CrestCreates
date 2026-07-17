using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests.Invocation;

public sealed class AgentCapabilityIdempotencyKeyBuilderTests
{
    [Theory]
    [InlineData("tenant-a/user-a/agent-a/arguments-a/origin-explicit")]
    [InlineData("tenant-b/user-a/agent-a/arguments-a/origin-explicit")]
    [InlineData("tenant-a/user-b/agent-a/arguments-a/origin-explicit")]
    [InlineData("tenant-a/user-a/agent-b/arguments-a/origin-explicit")]
    [InlineData("tenant-a/user-a/agent-a/arguments-b/origin-explicit")]
    [InlineData("tenant-a/user-a/agent-a/arguments-a/origin-automatic")]
    public void Build_UsesCompleteAgentFingerprint(string fingerprintValue)
    {
        var builder = new AgentCapabilityIdempotencyKeyBuilder();

        var key = builder.Build(new AgentToolInvocationFingerprint(
            "arguments-hash",
            fingerprintValue));

        key.Should().Be("agent:v1:" + fingerprintValue);
    }

    [Fact]
    public void Build_SameFingerprintPreservesKeyAcrossReleasedRetry()
    {
        var builder = new AgentCapabilityIdempotencyKeyBuilder();
        var fingerprint = new AgentToolInvocationFingerprint(
            "arguments-hash",
            "complete-agent-fingerprint");

        builder.Build(fingerprint).Should().Be(builder.Build(fingerprint));
    }
}
