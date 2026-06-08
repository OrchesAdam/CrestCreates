using CrestCreates.Capability.Abstractions;
using CrestCreates.DynamicApi;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Exposure.Tests;

public class CapabilityEndpointDescriptorTests
{
    [Fact]
    public void Endpoint_References_Capability_By_VersionedRef()
    {
        var endpoint = new CapabilityEndpointDescriptor
        {
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1),
            RoutePattern = "/api/customers"
        };

        endpoint.Capability.Id.Should().Be("cap_01");
        endpoint.RoutePattern.Should().Be("/api/customers");
    }

    [Fact]
    public void DeriveHttpMethod_Query_Returns_Get()
    {
        var method = CapabilityEndpointDescriptor.DeriveHttpMethod(CapabilityKind.Query);
        method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public void DeriveHttpMethod_Command_Returns_Post()
    {
        var method = CapabilityEndpointDescriptor.DeriveHttpMethod(CapabilityKind.Command);
        method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public void Endpoint_Defaults_RequireAuthorization_To_True()
    {
        var endpoint = new CapabilityEndpointDescriptor
        {
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1),
            RoutePattern = "/api/test"
        };

        endpoint.RequireAuthorization.Should().BeTrue();
    }
}
