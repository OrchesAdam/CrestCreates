using CrestCreates.DynamicApi;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Registry;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

public class CapabilityEndpointRegistryTests
{
    [Fact]
    public void Build_Indexes_By_Id_Name_Version_And_Capability()
    {
        var registry = new CapabilityEndpointRegistry(
            new RegistryValidationEngine<CapabilityEndpointDescriptor>(
                Array.Empty<IRegistryValidator<CapabilityEndpointDescriptor>>()));

        var descriptor = CreateEndpoint("books.create.http", "books.create", 1);

        registry.Build(new[] { new TestProvider(descriptor) });

        registry.GetById("books.create.http").Should().BeSameAs(descriptor);
        registry.GetByNameAndVersion("Create Book Endpoint", 1).Should().BeSameAs(descriptor);
        registry.GetActiveVersion("Create Book Endpoint").Should().BeSameAs(descriptor);
        registry.GetByCapability("books.create", 1).Should().ContainSingle().Which.Should().BeSameAs(descriptor);
    }

    [Fact]
    public void GetByCapability_Without_Version_Returns_All_Capability_Endpoints()
    {
        var registry = new CapabilityEndpointRegistry(
            new RegistryValidationEngine<CapabilityEndpointDescriptor>(
                Array.Empty<IRegistryValidator<CapabilityEndpointDescriptor>>()));

        var v1 = CreateEndpoint("books.create.v1.http", "books.create", 1);
        var v2 = CreateEndpoint("books.create.v2.http", "books.create", 2);

        registry.Build(new[] { new TestProvider(v1, v2) });

        registry.GetByCapability("books.create").Should().BeEquivalentTo(new[] { v1, v2 });
    }

    private static CapabilityEndpointDescriptor CreateEndpoint(
        string id,
        string capabilityId,
        int capabilityVersion)
        => new()
        {
            Id = id,
            Name = "Create Book Endpoint",
            Version = capabilityVersion,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>(capabilityId, capabilityVersion),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/books"
        };

    private sealed class TestProvider : ICapabilityEndpointDescriptorProvider
    {
        private readonly IReadOnlyList<CapabilityEndpointDescriptor> _descriptors;

        public TestProvider(params CapabilityEndpointDescriptor[] descriptors)
        {
            _descriptors = descriptors;
        }

        public IReadOnlyList<CapabilityEndpointDescriptor> GetDescriptors() => _descriptors;
    }
}
