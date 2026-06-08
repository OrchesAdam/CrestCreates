using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorDependencyGraphTests
{
    [Fact]
    public void AddEdge_And_GetDependents()
    {
        var graph = new DescriptorDependencyGraph();
        graph.AddEdge("cap_01", "schema_01", DescriptorDependencyKind.Uses);

        var dependents = graph.GetDependents("schema_01");

        dependents.Should().HaveCount(1);
        dependents[0].SourceId.Should().Be("cap_01");
        dependents[0].Kind.Should().Be(DescriptorDependencyKind.Uses);
    }

    [Fact]
    public void GetDependencies_Returns_Edges_From_Source()
    {
        var graph = new DescriptorDependencyGraph();
        graph.AddEdge("cap_01", "schema_01", DescriptorDependencyKind.Uses);
        graph.AddEdge("cap_01", "schema_02", DescriptorDependencyKind.Uses);

        var deps = graph.GetDependencies("cap_01");

        deps.Should().HaveCount(2);
    }

    [Fact]
    public void AnalyzeImpact_Returns_Dependents()
    {
        var graph = new DescriptorDependencyGraph();
        graph.AddEdge("cap_01", "schema_01", DescriptorDependencyKind.Uses);
        graph.AddEdge("wf_01", "schema_01", DescriptorDependencyKind.Triggers);

        var report = graph.AnalyzeImpact("schema_01", 1, 2);

        report.AffectedDependents.Should().HaveCount(2);
        report.IsBreaking.Should().BeTrue();
    }

    [Fact]
    public void Empty_Graph_Returns_Empty_Results()
    {
        var graph = new DescriptorDependencyGraph();

        var deps = graph.GetDependencies("nonexistent");
        var dependents = graph.GetDependents("nonexistent");

        deps.Should().BeEmpty();
        dependents.Should().BeEmpty();
    }
}