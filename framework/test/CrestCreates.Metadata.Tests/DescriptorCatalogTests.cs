using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorCatalogTests
{
    [Fact]
    public void FindDependents_Returns_Through_Graph_And_Registry()
    {
        var globalRegistry = new GlobalDescriptorRegistry();
        globalRegistry.Register(new SchemaDescriptor { Id = "schema_01", Name = "CustomerInput", Version = 1 });
        globalRegistry.Register(new Capability.Abstractions.CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "crm.customer.create",
            Version = 1
        });

        var graph = new DescriptorDependencyGraph();
        graph.AddEdge("cap_01", "schema_01", DescriptorDependencyKind.Uses);

        var catalog = new DescriptorCatalog(globalRegistry, graph);

        var dependents = catalog.FindDependents("schema_01").ToList();

        dependents.Should().HaveCount(1);
        dependents[0].Id.Should().Be("cap_01");
    }

    [Fact]
    public void AnalyzeImpact_Delegates_To_Graph()
    {
        var globalRegistry = new GlobalDescriptorRegistry();
        var graph = new DescriptorDependencyGraph();
        graph.AddEdge("cap_01", "schema_01", DescriptorDependencyKind.Uses);

        var catalog = new DescriptorCatalog(globalRegistry, graph);

        var report = catalog.AnalyzeImpact("schema_01", 1, 2);

        report.AffectedDependents.Should().HaveCount(1);
        report.IsBreaking.Should().BeTrue();
    }
}