using System.Collections.Frozen;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests;

public sealed class AgentToolRuntimeSnapshotProviderTests
{
    [Fact]
    public void GetRequired_NeverReturnsAnUnpublishedEmptyFallback()
    {
        var provider = new AgentToolRuntimeSnapshotProvider();

        var action = provider.GetRequired;

        action.Should().Throw<AgentToolConfigurationException>()
            .Which.Code.Should().Be(AgentToolStartupDiagnosticCodes.SnapshotPublicationFailure);
    }

    [Fact]
    public void Publish_IsExactlyOnceAndReturnsSameImmutableSnapshot()
    {
        var provider = new AgentToolRuntimeSnapshotProvider();
        var snapshot = new AgentToolRuntimeSnapshot(
            Array.Empty<KeyValuePair<string, AgentToolRuntimeEntry>>()
                .ToFrozenDictionary(StringComparer.Ordinal));

        provider.Publish(snapshot);

        provider.GetRequired().Should().BeSameAs(snapshot);
        provider.Invoking(instance => instance.Publish(snapshot))
            .Should().Throw<AgentToolConfigurationException>()
            .Which.Code.Should().Be(AgentToolStartupDiagnosticCodes.SnapshotPublicationFailure);
    }

    [Fact]
    public void FailedBuild_IsStickyAndBlocksPublication()
    {
        var provider = new AgentToolRuntimeSnapshotProvider();
        provider.MarkFailed(new InvalidOperationException("failed"));
        var snapshot = new AgentToolRuntimeSnapshot(
            Array.Empty<KeyValuePair<string, AgentToolRuntimeEntry>>()
                .ToFrozenDictionary(StringComparer.Ordinal));

        provider.Invoking(instance => instance.Publish(snapshot))
            .Should().Throw<AgentToolConfigurationException>();
        provider.Invoking(instance => instance.GetRequired())
            .Should().Throw<AgentToolConfigurationException>();
    }
}
