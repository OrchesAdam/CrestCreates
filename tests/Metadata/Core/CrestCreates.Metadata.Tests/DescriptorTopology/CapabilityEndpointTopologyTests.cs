using CrestCreates.DynamicApi;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.DescriptorRelationship;
using CrestCreates.Metadata.DescriptorTopology;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests.DescriptorTopology;

public class CapabilityEndpointTopologyTests
{
    [Fact]
    public void Build_Connects_Endpoint_To_Capability()
    {
        var capability = new CapabilityDescriptor
        {
            Id = "book.crt",
            Name = "Create Book",
            Version = 1
        };
        var endpoint = CreateEndpoint();
        var builder = CreateBuilder();

        var snapshot = builder.Build([capability, endpoint]);

        var endpointRef = new DescriptorRef("dynamic-api-endpoint", "book.http", 1);
        var capabilityRef = new DescriptorRef("capability", "book.crt", 1);
        snapshot.GetDirectDependencies(endpointRef)
            .Should().ContainSingle(n => n.Ref == capabilityRef);
        snapshot.GetDirectDependents(capabilityRef)
            .Should().ContainSingle(n => n.Ref == endpointRef);
    }

    [Fact]
    public void Build_Missing_Capability_Reports_Missing_Target()
    {
        var builder = CreateBuilder();

        var snapshot = builder.Build([CreateEndpoint()]);

        snapshot.Diagnostics.All.Should().Contain(d =>
            d.Code.Value == "MISSING_TARGET"
            && d.Message.Contains("capability.book.crt", StringComparison.Ordinal));
    }

    private static CapabilityEndpointDescriptor CreateEndpoint()
        => new()
        {
            Id = "book.http",
            Name = "Create Book Endpoint",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("book.crt", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/books"
        };

    private static DescriptorTopologyBuilder CreateBuilder()
    {
        var provider = new DefaultDescriptorRelationshipProvider(new IDescriptorRelationshipExtractor[]
        {
            new CapabilityEndpointRelationshipExtractor()
        });
        var hashComputer = new DefaultCanonicalHashComputer();
        var hashBuilder = new DescriptorStableHashBuilder(hashComputer);
        return new DescriptorTopologyBuilder(provider, hashBuilder);
    }
}
