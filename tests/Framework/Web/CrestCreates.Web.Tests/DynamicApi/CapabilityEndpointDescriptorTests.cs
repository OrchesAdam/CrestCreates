using System.Linq;
using System.Reflection;
using CrestCreates.DynamicApi;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

#pragma warning disable CC1001 // Test fixture uses unregistered descriptor refs — no real registry available in unit tests

namespace CrestCreates.Web.Tests.DynamicApi;

public class CapabilityEndpointDescriptorTests
{
    [Fact]
    public void Descriptor_Implements_VersionedDescriptor()
    {
        var descriptor = new CapabilityEndpointDescriptor
        {
            Id = "books.create.http",
            Name = "Create Book HTTP Endpoint",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("books.create", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/books"
        };

        descriptor.Should().BeAssignableTo<IDescriptor>();
        descriptor.Should().BeAssignableTo<IVersionedDescriptor>();
        descriptor.Namespace.Should().Be("dynamic-api-endpoint");
        descriptor.Kind.Should().Be(DescriptorKind.DynamicApiEndpoint);
        ((IDescriptor)descriptor).FullId.Should().Be("dynamic-api-endpoint.books.create.http");
    }

    [Fact]
    public void DescriptorKindNames_Maps_DynamicApiEndpoint()
    {
        DescriptorKindNames.DynamicApiEndpoint.Should().Be("DynamicApiEndpoint");
        DescriptorKindNames.ToCanonicalString(DescriptorKind.DynamicApiEndpoint)
            .Should().Be("DynamicApiEndpoint");
    }

    [Fact]
    public void Descriptor_Does_Not_Expose_Capability_Authority_Fields()
    {
        var properties = typeof(CapabilityEndpointDescriptor)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name)
            .ToArray();

        properties.Should().NotContain("InputSchema");
        properties.Should().NotContain("OutputSchema");
        properties.Should().NotContain("Permissions");
        properties.Should().NotContain("Handler");
        properties.Should().NotContain("Invoker");
        properties.Should().NotContain("ServiceMethod");
        properties.Should().NotContain("EndpointDelegate");
    }
}
