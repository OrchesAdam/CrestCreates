using CrestCreates.DynamicApi;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using FluentAssertions;
using Xunit;

#pragma warning disable CC1001 // Test fixture uses unregistered descriptor refs — no real registry available in unit tests

namespace CrestCreates.Web.Tests.DynamicApi;

public class CapabilityEndpointRelationshipExtractorTests
{
    [Fact]
    public void Extract_Returns_Strong_Reference_To_Capability()
    {
        var descriptor = new CapabilityEndpointDescriptor
        {
            Id = "books.create.http",
            Name = "Create Book Endpoint",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("books.create", 3),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/books"
        };
        var extractor = new CapabilityEndpointRelationshipExtractor();

        var relationships = extractor.Extract(descriptor);

        var relationship = relationships.Should().ContainSingle().Subject;
        relationship.From.Should().Be(new DescriptorRef("dynamic-api-endpoint", "books.create.http", 1));
        relationship.To.Should().Be(new DescriptorRef("capability", "books.create", 3));
        relationship.Kind.Should().Be(RelationshipKind.References);
        relationship.Role.Should().Be("Capability");
        relationship.SourcePath.Should().Be(nameof(CapabilityEndpointDescriptor.Capability));
        relationship.Strength.Should().Be(RelationshipStrength.Strong);
        relationship.IsRuntimeBinding.Should().BeFalse();
    }

    [Fact]
    public void SupportedKind_Is_DynamicApiEndpoint()
    {
        new CapabilityEndpointRelationshipExtractor()
            .SupportedKind.Should().Be(DescriptorKind.DynamicApiEndpoint);
    }
}
