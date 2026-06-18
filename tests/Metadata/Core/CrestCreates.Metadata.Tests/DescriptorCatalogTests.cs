using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorCatalogTests
{
    [Fact]
    public void FindDependents_Returns_Through_Graph_And_Registry()
    {
        var globalRegistry = new GlobalDescriptorRegistry();
        globalRegistry.Register(new SchemaDescriptor { Id = "schema_01", Name = "CustomerInput", Version = 1 });
        globalRegistry.Register(new CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "crm.customer.create",
            Version = 1
        });

        var graphMock = new Mock<IDescriptorDependencyGraph>();
        graphMock.Setup(g => g.GetDependents("schema_01"))
            .Returns(new[]
            {
                new DependencyEdge { SourceId = "cap_01", TargetId = "schema_01", Kind = DescriptorDependencyKind.Uses }
            });

        var catalog = new DescriptorCatalog(globalRegistry, graphMock.Object);

        var dependents = catalog.FindDependents("schema_01").ToList();

        dependents.Should().HaveCount(1);
        dependents[0].Id.Should().Be("cap_01");
    }

    [Fact]
    public void AnalyzeImpact_Delegates_To_Graph()
    {
        var globalRegistry = new GlobalDescriptorRegistry();
        var graphMock = new Mock<IDescriptorDependencyGraph>();
        graphMock.Setup(g => g.AnalyzeImpact("schema_01", 1, 2))
            .Returns(new ImpactReport
            {
                DescriptorId = "schema_01",
                FromVersion = 1,
                ToVersion = 2,
                AffectedDependents = new[]
                {
                    new DependencyEdge
                    {
                        SourceId = "cap_01",
                        TargetId = "schema_01",
                        Kind = DescriptorDependencyKind.Uses
                    }
                }
            });

        var catalog = new DescriptorCatalog(globalRegistry, graphMock.Object);

        var report = catalog.AnalyzeImpact("schema_01", 1, 2);

        report.AffectedDependents.Should().HaveCount(1);
        report.IsBreaking.Should().BeTrue();
    }
}