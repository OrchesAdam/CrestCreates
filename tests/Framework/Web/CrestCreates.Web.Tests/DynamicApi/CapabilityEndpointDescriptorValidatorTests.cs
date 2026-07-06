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

    [Fact]
    public void Validate_Duplicate_Method_And_Route_Fails()
    {
        var validator = CreateValidator();
        var endpoint1 = ValidEndpoint("books.create");
        var endpoint2 = ValidEndpoint("books.update");
        // Same method + same route pattern → conflict
        endpoint2 = CopyEndpoint(endpoint2, routePattern: endpoint1.RoutePattern);

        var report = validator.Validate(new[] { endpoint1, endpoint2 });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase)
                                            && i.Message.Contains("POST", StringComparison.OrdinalIgnoreCase)
                                            && i.Message.Contains("/api/books", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Same_Route_Different_Method_Passes()
    {
        var listCap = new CapabilityDescriptor
        {
            Id = "books.list", Name = "List Books", Version = 1, RiskLevel = CapabilityRiskLevel.Low
        };
        var createCap = new CapabilityDescriptor
        {
            Id = "books.create", Name = "Create Book", Version = 1, RiskLevel = CapabilityRiskLevel.Medium
        };
        var validator = CreateValidator(listCap, createCap);
        var getEndpoint = new CapabilityEndpointDescriptor
        {
            Id = "books.list.http",
            Name = "List Books",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("books.list", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Get,
            RoutePattern = "/api/books",
            OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 200 }
        };
        var postEndpoint = ValidEndpoint("books.create");

        var report = validator.Validate(new[] { getEndpoint, postEndpoint });

        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void Validate_Same_Method_Different_Route_Passes()
    {
        var createCap = new CapabilityDescriptor
        {
            Id = "books.create", Name = "Create Book", Version = 1, RiskLevel = CapabilityRiskLevel.Medium
        };
        var updateCap = new CapabilityDescriptor
        {
            Id = "books.update", Name = "Update Book", Version = 1, RiskLevel = CapabilityRiskLevel.Medium
        };
        var validator = CreateValidator(createCap, updateCap);
        var endpoint1 = ValidEndpoint("books.create");
        var endpoint2 = new CapabilityEndpointDescriptor
        {
            Id = "books.update.http",
            Name = "Update Book Endpoint",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("books.update", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/books/{id}",
            InputBindings = new[] { new CapabilityEndpointInputBinding { Name = "id", Source = CapabilityEndpointParameterSource.Route } },
            OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 200 }
        };

        var report = validator.Validate(new[] { endpoint1, endpoint2 });

        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void Validate_Duplicate_Route_With_Constraint_Normalized_Fails()
    {
        // /api/books/{id:int} and /api/books/{id} should be treated as the same route
        var getCap = new CapabilityDescriptor
        {
            Id = "books.get", Name = "Get Book", Version = 1, RiskLevel = CapabilityRiskLevel.Low
        };
        var validator = CreateValidator(getCap);
        var endpoint1 = ValidEndpoint("books.get");
        endpoint1 = new CapabilityEndpointDescriptor
        {
            Id = "books.get.http",
            Name = "Get Book",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("books.get", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Get,
            RoutePattern = "/api/books/{id:int}",
            InputBindings = new[] { new CapabilityEndpointInputBinding { Name = "id", Source = CapabilityEndpointParameterSource.Route } },
            OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 200 }
        };
        var endpoint2 = new CapabilityEndpointDescriptor
        {
            Id = "books.get2.http",
            Name = "Get Book Alt",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("books.get", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Get,
            RoutePattern = "/api/books/{id}",
            InputBindings = new[] { new CapabilityEndpointInputBinding { Name = "id", Source = CapabilityEndpointParameterSource.Route } },
            OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 200 }
        };

        var report = validator.Validate(new[] { endpoint1, endpoint2 });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Null_InputBindings_Fails()
    {
        var validator = CreateValidator();
        var descriptor = new CapabilityEndpointDescriptor
        {
            Id = "test.http",
            Name = "Test",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("books.create", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/test",
            InputBindings = null!,
            OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 200 }
        };

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("InputBindings", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Null_OutputMapping_Fails()
    {
        var validator = CreateValidator();
        var descriptor = new CapabilityEndpointDescriptor
        {
            Id = "test.http",
            Name = "Test",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("books.create", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/test",
            OutputMapping = null!
        };

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("OutputMapping", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Null_Projection_Fails()
    {
        var validator = CreateValidator();
        var descriptor = new CapabilityEndpointDescriptor
        {
            Id = "test.http",
            Name = "Test",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("books.create", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/test",
            OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 200 },
            Projection = null!
        };

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("Projection", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Empty_InputBinding_Name_Fails()
    {
        var validator = CreateValidator();
        var descriptor = CopyEndpoint(ValidEndpoint(), inputBindings: new[]
        {
            new CapabilityEndpointInputBinding { Name = "", Source = CapabilityEndpointParameterSource.Route }
        });

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("Name must not be empty", StringComparison.Ordinal));
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

    private static CapabilityEndpointDescriptorValidator CreateValidator(
        params CapabilityDescriptor[] capabilities)
    {
        var registry = new Mock<ICapabilityRegistry>();
        foreach (var cap in capabilities)
        {
            var c = cap;
            registry.Setup(r => r.GetByVersion(c.Id, c.Version)).Returns(c);
        }

        return new CapabilityEndpointDescriptorValidator(registry.Object);
    }

    [Fact]
    public void Validate_InheritCapability_HighRisk_NoPermissions_Fails()
    {
        var highRiskCap = new CapabilityDescriptor
        {
            Id = "dangerous.op", Name = "Dangerous Op", Version = 1,
            RiskLevel = CapabilityRiskLevel.High, Permissions = []
        };
        var validator = CreateValidator(highRiskCap);
        var descriptor = new CapabilityEndpointDescriptor
        {
            Id = "dangerous.http",
            Name = "Dangerous Op Endpoint",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("dangerous.op", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/dangerous",
            AuthorizationMode = CapabilityEndpointAuthorizationMode.InheritCapability,
            OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 200 }
        };

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i =>
            i.Message.Contains("InheritCapability", StringComparison.Ordinal)
            && i.Message.Contains("unguarded", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_InheritCapability_HighRisk_WithPermissions_Passes()
    {
        var highRiskCap = new CapabilityDescriptor
        {
            Id = "dangerous.op", Name = "Dangerous Op", Version = 1,
            RiskLevel = CapabilityRiskLevel.High,
            Permissions = ["dangerous.op.execute"]
        };
        var validator = CreateValidator(highRiskCap);
        var descriptor = new CapabilityEndpointDescriptor
        {
            Id = "dangerous.http",
            Name = "Dangerous Op Endpoint",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("dangerous.op", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/dangerous",
            AuthorizationMode = CapabilityEndpointAuthorizationMode.InheritCapability,
            OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 200 }
        };

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void Validate_InheritCapability_LowRisk_NoPermissions_Passes()
    {
        var lowRiskCap = new CapabilityDescriptor
        {
            Id = "safe.op", Name = "Safe Op", Version = 1,
            RiskLevel = CapabilityRiskLevel.Low, Permissions = []
        };
        var validator = CreateValidator(lowRiskCap);
        var descriptor = new CapabilityEndpointDescriptor
        {
            Id = "safe.http",
            Name = "Safe Op Endpoint",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("safe.op", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Get,
            RoutePattern = "/api/safe",
            AuthorizationMode = CapabilityEndpointAuthorizationMode.InheritCapability,
            OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 200 }
        };

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void Validate_Duplicate_Route_Trailing_Slash_Normalized_Fails()
    {
        var cap = new CapabilityDescriptor
        {
            Id = "books.list", Name = "List Books", Version = 1, RiskLevel = CapabilityRiskLevel.Low
        };
        var validator = CreateValidator(cap);
        var endpoint1 = new CapabilityEndpointDescriptor
        {
            Id = "books.list.http",
            Name = "List Books",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("books.list", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Get,
            RoutePattern = "/api/books",
            OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 200 }
        };
        var endpoint2 = new CapabilityEndpointDescriptor
        {
            Id = "books.list2.http",
            Name = "List Books Alt",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("books.list", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Get,
            RoutePattern = "/api/books/",
            OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 200 }
        };

        var report = validator.Validate(new[] { endpoint1, endpoint2 });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RequireAuthenticated_HighRisk_Passes()
    {
        var highRiskCap = new CapabilityDescriptor
        {
            Id = "dangerous.op", Name = "Dangerous Op", Version = 1,
            RiskLevel = CapabilityRiskLevel.High, Permissions = []
        };
        var validator = CreateValidator(highRiskCap);
        var descriptor = new CapabilityEndpointDescriptor
        {
            Id = "dangerous.http",
            Name = "Dangerous Op Endpoint",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("dangerous.op", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/dangerous",
            AuthorizationMode = CapabilityEndpointAuthorizationMode.RequireAuthenticated,
            OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 200 }
        };

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeFalse();
    }
}
