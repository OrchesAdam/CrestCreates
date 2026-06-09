using CrestCreates.Capability.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class InMemoryPipelineMetricsTests
{
    [Fact]
    public void GetSnapshot_Empty_ReturnsZeros()
    {
        var metrics = new InMemoryPipelineMetrics();
        var snapshot = metrics.GetSnapshot();

        snapshot.TotalExecutions.Should().Be(0);
        snapshot.SuccessfulExecutions.Should().Be(0);
        snapshot.ByCapability.Should().BeEmpty();
    }

    [Fact]
    public void RecordExecution_TracksCounts()
    {
        var metrics = new InMemoryPipelineMetrics();
        metrics.RecordExecution("test.cap", true, TimeSpan.FromMilliseconds(10));
        metrics.RecordExecution("test.cap", false, TimeSpan.FromMilliseconds(20));
        metrics.RecordExecution("other.cap", true, TimeSpan.FromMilliseconds(5));

        var snapshot = metrics.GetSnapshot();
        snapshot.TotalExecutions.Should().Be(3);
        snapshot.SuccessfulExecutions.Should().Be(2);
        snapshot.FailedExecutions.Should().Be(1);
    }

    [Fact]
    public void GetSnapshot_PerCapabilityMetrics()
    {
        var metrics = new InMemoryPipelineMetrics();
        metrics.RecordExecution("cap.a", true, TimeSpan.FromMilliseconds(100));
        metrics.RecordExecution("cap.a", true, TimeSpan.FromMilliseconds(200));
        metrics.RecordExecution("cap.b", false, TimeSpan.FromMilliseconds(50));

        var snapshot = metrics.GetSnapshot();
        snapshot.ByCapability["cap.a"].Executions.Should().Be(2);
        snapshot.ByCapability["cap.a"].Successes.Should().Be(2);
        snapshot.ByCapability["cap.a"].AverageDurationMs.Should().Be(150);
        snapshot.ByCapability["cap.b"].Failures.Should().Be(1);
    }

    [Fact]
    public void GetSnapshot_AverageDuration_CalculatedCorrectly()
    {
        var metrics = new InMemoryPipelineMetrics();
        metrics.RecordExecution("test", true, TimeSpan.FromMilliseconds(100));
        metrics.RecordExecution("test", true, TimeSpan.FromMilliseconds(200));

        var snapshot = metrics.GetSnapshot();
        snapshot.AverageDurationMs.Should().Be(150);
    }
}