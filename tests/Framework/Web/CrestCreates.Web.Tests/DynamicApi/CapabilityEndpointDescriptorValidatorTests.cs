using CrestCreates.DynamicApi;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.DescriptorCapability;
using FluentAssertions;
using Moq;
using Xunit;

#pragma warning disable CC1001 // Test fixture uses unregistered descriptor refs — no real registry available in unit tests

namespace CrestCreates.Web.Tests.DynamicApi;

public class CapabilityEndpointDescriptorValidatorTests
{
    [Fact]
    public void Validate_Default_Capability_Ref_Fails()
    {
        var validator = CreateValidator();
        var descriptor = new CapabilityEndpointDescriptor
        {
            Id = "test.http",
            Name = "Test",
            Version = 1,
            Capability = default,
            HttpMethod = CapabilityEndpointHttpMethod.Get,
            RoutePattern = "/api/test"
        };

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("Capability", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Route_Without_Leading_Slash_Fails()
    {
        var validator = CreateValidator();
        var descriptor = CopyEndpoint(ValidEndpoint(), routePattern: "api/books");

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("RoutePattern", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AllowAnonymous_With_Permissions_Fails()
    {
        var capability = new CapabilityDescriptor
        {
            Id = "books.create",
            Name = "Create Book",
            Version = 1,
            Permissions = new[] { "Books.Create" }
        };
        var validator = CreateValidator(capability);
        var descriptor = CopyEndpoint(
            ValidEndpoint(),
            authorizationMode: CapabilityEndpointAuthorizationMode.AllowAnonymous);

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("AllowAnonymous", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AllowAnonymous_With_High_Risk_Fails()
    {
        var capability = new CapabilityDescriptor
        {
            Id = "books.delete",
            Name = "Delete Book",
            Version = 1,
            RiskLevel = CapabilityRiskLevel.High
        };
        var validator = CreateValidator(capability);
        var descriptor = CopyEndpoint(
            ValidEndpoint("books.delete"),
            authorizationMode: CapabilityEndpointAuthorizationMode.AllowAnonymous);

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("high-risk", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Two_Body_Bindings_Fails()
    {
        var validator = CreateValidator();
        var descriptor = CopyEndpoint(
            ValidEndpoint(),
            inputBindings: new[]
            {
                new CapabilityEndpointInputBinding { Name = "a", Source = CapabilityEndpointParameterSource.Body },
                new CapabilityEndpointInputBinding { Name = "b", Source = CapabilityEndpointParameterSource.Body }
            });

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("body", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Route_Token_Without_Binding_Fails()
    {
        var validator = CreateValidator();
        var descriptor = CopyEndpoint(ValidEndpoint(), routePattern: "/api/books/{id}");

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("id", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Route_Binding_Without_Token_Fails()
    {
        var validator = CreateValidator();
        var descriptor = CopyEndpoint(
            ValidEndpoint(),
            inputBindings: new[]
            {
                new CapabilityEndpointInputBinding
                {
                    Name = "id",
                    Source = CapabilityEndpointParameterSource.Route
                }
            });

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("id", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Valid_Endpoint_Passes()
    {
        var validator = CreateValidator();

        var report = validator.Validate(new[] { ValidEndpoint() });

        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void Validate_Missing_Id_Fails()
    {
        var validator = CreateValidator();
        var bad = new CapabilityEndpointDescriptor
        {
            Id = "",
            Name = "Test",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("books.create", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/books"
        };

        var report = validator.Validate(new[] { bad });

        report.HasErrors.Should().BeTrue();
    }

    [Fact]
    public void Validate_Zero_Version_Fails()
    {
        var validator = CreateValidator();
        var bad = new CapabilityEndpointDescriptor
        {
            Id = "test.http",
            Name = "Test",
            Version = 0,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("books.create", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/books"
        };

        var report = validator.Validate(new[] { bad });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("Version", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_CatchAll_Route_Token_Matches_Binding()
    {
        var validator = CreateValidator();
        var descriptor = CopyEndpoint(
            ValidEndpoint(),
            routePattern: "/api/files/{**path}",
            inputBindings: new[]
            {
                new CapabilityEndpointInputBinding
                {
                    Name = "path",
                    Source = CapabilityEndpointParameterSource.Route
                }
            });

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void Validate_Optional_Route_Token_Matches_Binding()
    {
        var validator = CreateValidator();
        var descriptor = CopyEndpoint(
            ValidEndpoint(),
            routePattern: "/api/books/{id?}",
            inputBindings: new[]
            {
                new CapabilityEndpointInputBinding
                {
                    Name = "id",
                    Source = CapabilityEndpointParameterSource.Route
                }
            });

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void Validate_None_HttpMethod_Fails()
    {
        var validator = CreateValidator();
        var descriptor = CopyEndpoint(ValidEndpoint(), httpMethod: CapabilityEndpointHttpMethod.None);

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("HttpMethod", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Invalid_SuccessStatusCode_Fails()
    {
        var validator = CreateValidator();
        var descriptor = CopyEndpoint(ValidEndpoint(), outputMapping: new CapabilityEndpointOutputMapping { SuccessStatusCode = 404 });

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("status code", StringComparison.OrdinalIgnoreCase));
    }

    private static CapabilityEndpointDescriptor ValidEndpoint(string capabilityId = "books.create")
        => new()
        {
            Id = capabilityId + ".http",
            Name = "Create Book Endpoint",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>(capabilityId, 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/books",
            OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 200 }
        };

    private static CapabilityEndpointDescriptor CopyEndpoint(
        CapabilityEndpointDescriptor descriptor,
        string? routePattern = null,
        CapabilityEndpointAuthorizationMode? authorizationMode = null,
        IReadOnlyList<CapabilityEndpointInputBinding>? inputBindings = null,
        CapabilityEndpointOutputMapping? outputMapping = null,
        CapabilityEndpointHttpMethod? httpMethod = null)
        => new()
        {
            Id = descriptor.Id,
            Name = descriptor.Name,
            Version = descriptor.Version,
            Capability = descriptor.Capability,
            HttpMethod = httpMethod ?? descriptor.HttpMethod,
            RoutePattern = routePattern ?? descriptor.RoutePattern,
            AuthorizationMode = authorizationMode ?? descriptor.AuthorizationMode,
            InputBindings = inputBindings ?? descriptor.InputBindings,
            OutputMapping = outputMapping ?? descriptor.OutputMapping,
            Projection = descriptor.Projection
        };

    private static CapabilityEndpointDescriptorValidator CreateValidator(
        CapabilityDescriptor? capability = null)
    {
        capability ??= new CapabilityDescriptor
        {
            Id = "books.create",
            Name = "Create Book",
            Version = 1,
            RiskLevel = CapabilityRiskLevel.Medium
        };

        var registry = new Mock<ICapabilityRegistry>();
        registry
            .Setup(r => r.GetByVersion(capability.Id, capability.Version))
            .Returns(capability);

        return new CapabilityEndpointDescriptorValidator(registry.Object);
    }
}
