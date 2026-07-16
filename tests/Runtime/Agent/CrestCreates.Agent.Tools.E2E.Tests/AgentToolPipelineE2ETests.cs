using CrestCreates.Agent.Tools.AotFixture;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.E2E.Tests;

public sealed class AgentToolPipelineE2ETests
{
    [Fact]
    public async Task Generated_tool_runs_through_governance_dispatcher_and_completed_replay()
    {
        var exitCode = await AgentToolFixtureRunner.RunAsync();

        exitCode.Should().Be(0);
    }
}
