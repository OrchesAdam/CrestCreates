using CrestCreates.DynamicApi;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests.DescriptorStableHash;

public class CapabilityEndpointStableHashTests
{
    private readonly ICanonicalHashComputer _hashComputer = new DefaultCanonicalHashComputer();

    [Fact]
    public void RoutePattern_Change_Changes_ContractHash()
    {
        var first = CreateEndpoint(route: "/api/books");
        var second = CreateEndpoint(route: "/api/library-books");

        Hash(first).Should().NotBe(Hash(second));
    }

    [Fact]
    public void Capability_Version_Change_Changes_ContractHash()
    {
        var first = CreateEndpoint(capabilityVersion: 1);
        var second = CreateEndpoint(capabilityVersion: 2);

        Hash(first).Should().NotBe(Hash(second));
    }

    [Fact]
    public void OperationId_Change_Changes_ContractHash()
    {
        var first = CreateEndpoint(operationId: "Books_Create");
        var second = CreateEndpoint(operationId: "Books_Create_V2");

        Hash(first).Should().NotBe(Hash(second));
    }

    [Fact]
    public void Summary_Change_Changes_DefinitionHash_Not_ContractHash()
    {
        var first = CreateEndpoint(summary: "Create a book");
        var second = CreateEndpoint(summary: "Creates one library book");

        _hashComputer.ComputeContractHash(first, CanonicalHashScope.InternalFull).Value
            .Should().Be(_hashComputer.ComputeContractHash(second, CanonicalHashScope.InternalFull).Value);
        _hashComputer.ComputeDefinitionHash(first, CanonicalHashScope.InternalFull).Value
            .Should().NotBe(_hashComputer.ComputeDefinitionHash(second, CanonicalHashScope.InternalFull).Value);
    }

    private string Hash(CapabilityEndpointDescriptor descriptor)
        => _hashComputer.ComputeContractHash(descriptor, CanonicalHashScope.InternalFull).Value;

    private static CapabilityEndpointDescriptor CreateEndpoint(
        string route = "/api/books",
        int capabilityVersion = 1,
        string? operationId = "Books_Create",
        string? summary = null)
        => new()
        {
            Id = "book.http",
            Name = "Create Book Endpoint",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("book.crt", capabilityVersion),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = route,
            AuthorizationMode = CapabilityEndpointAuthorizationMode.InheritCapability,
            InputBindings = new[]
            {
                new CapabilityEndpointInputBinding
                {
                    Name = "body",
                    Source = CapabilityEndpointParameterSource.Body,
                    CapabilityInputPath = "$"
                }
            },
            OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 201 },
            Projection = new CapabilityEndpointProjectionMetadata
            {
                OperationId = operationId,
                Summary = summary,
                Tags = new[] { "Books" }
            }
        };
}
