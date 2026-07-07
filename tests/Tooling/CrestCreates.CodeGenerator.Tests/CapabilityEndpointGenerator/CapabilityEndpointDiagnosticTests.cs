using System.Linq;
using CrestCreates.CodeGenerator.CapabilityEndpointGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.CapabilityEndpointGenerator;

public sealed class CapabilityEndpointDiagnosticTests
{
    #region Stubs

    /// <summary>
    /// Provides minimal stub types for diagnostic validation:
    /// Level 1 [CapabilityEndpointSpec], Level 2 [Post]/[Get]/[Put]/[CapabilityEndpointSet],
    /// [CrestService], and [DynamicApiRoute].
    /// </summary>
    private static string BuildDiagnosticStubs()
    {
        return @"
using System;

namespace CrestCreates.DynamicApi
{
    public enum CapabilityEndpointHttpMethod
    {
        None = 0,
        Get,
        Post,
        Put,
        Patch,
        Delete
    }

    public enum CapabilityEndpointAuthorizationMode
    {
        InheritCapability,
        RequireAuthenticated,
        AllowAnonymous
    }

    public enum CapabilityEndpointParameterSource
    {
        Route,
        Query,
        Header,
        Body
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CapabilityEndpointSpecAttribute : Attribute
    {
        public CapabilityEndpointSpecAttribute(
            string capabilityId,
            CapabilityEndpointHttpMethod httpMethod,
            string routePattern)
        {
            CapabilityId = capabilityId;
            HttpMethod = httpMethod;
            RoutePattern = routePattern;
        }

        public string CapabilityId { get; }
        public CapabilityEndpointHttpMethod HttpMethod { get; }
        public string RoutePattern { get; }

        public int CapabilityVersion { get; init; }
        public CapabilityEndpointAuthorizationMode AuthorizationMode { get; init; }
            = CapabilityEndpointAuthorizationMode.InheritCapability;
        public int SuccessStatusCode { get; init; }
        public string? OperationId { get; init; }
        public string? GroupName { get; init; }
        public string[]? Tags { get; init; }
        public string? Summary { get; init; }
        public string? Description { get; init; }
        public bool Deprecated { get; init; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CapabilityEndpointSpecsAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class CapabilityEndpointInputAttribute : Attribute
    {
        public CapabilityEndpointInputAttribute(Type type)
        {
            Type = type;
        }

        public Type Type { get; }

        public string Name { get; init; } = string.Empty;
        public CapabilityEndpointParameterSource Source { get; init; }
            = CapabilityEndpointParameterSource.Body;
        public bool Required { get; init; } = true;
        public string? CapabilityInputPath { get; init; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CapabilityEndpointSetAttribute : Attribute
    {
        public string? RoutePrefix { get; init; }
        public string? GroupName { get; init; }
        public string[]? Tags { get; init; }
        public string? Summary { get; init; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class PostAttribute : Attribute
    {
        public PostAttribute(string capabilityId, string route = """")
        {
            CapabilityId = capabilityId;
            Route = route;
        }

        public string CapabilityId { get; }
        public string Route { get; }

        public Type? Body { get; init; }
        public int CapabilityVersion { get; init; }
        public CapabilityEndpointAuthorizationMode Auth { get; init; }
            = CapabilityEndpointAuthorizationMode.InheritCapability;
        public int SuccessStatusCode { get; init; }
        public string? OperationId { get; init; }
        public string? Summary { get; init; }
        public string? Description { get; init; }
        public bool Deprecated { get; init; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class GetAttribute : Attribute
    {
        public GetAttribute(string capabilityId, string route = """")
        {
            CapabilityId = capabilityId;
            Route = route;
        }

        public string CapabilityId { get; }
        public string Route { get; }

        public Type? Input { get; init; }
        public string? InputName { get; init; }
        public int CapabilityVersion { get; init; }
        public CapabilityEndpointAuthorizationMode Auth { get; init; }
            = CapabilityEndpointAuthorizationMode.InheritCapability;
        public string? OperationId { get; init; }
        public string? Summary { get; init; }
        public string? Description { get; init; }
        public bool Deprecated { get; init; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class PutAttribute : Attribute
    {
        public PutAttribute(string capabilityId, string route = """")
        {
            CapabilityId = capabilityId;
            Route = route;
        }

        public string CapabilityId { get; }
        public string Route { get; }

        public Type? Body { get; init; }
        public Type? Input { get; init; }
        public string? InputName { get; init; }
        public int CapabilityVersion { get; init; }
        public CapabilityEndpointAuthorizationMode Auth { get; init; }
            = CapabilityEndpointAuthorizationMode.InheritCapability;
        public int SuccessStatusCode { get; init; }
        public string? OperationId { get; init; }
        public string? Summary { get; init; }
        public string? Description { get; init; }
        public bool Deprecated { get; init; }
    }
}

namespace CrestCreates.Domain.Shared.Attributes
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CrestServiceAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class DynamicApiRouteAttribute : Attribute
    {
        public string? Route { get; init; }
    }
}
";
    }

    #endregion

    private static SourceGeneratorResult Run(string source)
    {
        return SourceGeneratorTestHelper.RunGenerator<CodeGenerator.CapabilityEndpointGenerator.CapabilityEndpointGenerator>(
            source,
            additionalSources: new[] { BuildDiagnosticStubs() });
    }

    // ================================================================
    // CEP001: [CapabilityEndpointSpec] must be on a sealed nested class
    // ================================================================

    [Fact]
    public void CEP001_NonSealedSpecClass_EmitsError()
    {
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSpecs]
    public static partial class MyContainer
    {
        [CapabilityEndpointSpec(""test"", CapabilityEndpointHttpMethod.Get, ""items"")]
        public class NonSealedSpec { }
    }
}
";

        var result = Run(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CEP001");
    }

    [Fact]
    public void CEP001_NonNestedSpecClass_EmitsError()
    {
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSpec(""test"", CapabilityEndpointHttpMethod.Get, ""items"")]
    public sealed class TopLevelSpec { }
}
";

        var result = Run(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CEP001");
    }

    // ================================================================
    // CEP002: Container must have [CapabilityEndpointSpecs]
    // ================================================================

    [Fact]
    public void CEP002_MissingSpecsMarkerOnContainer_EmitsError()
    {
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    // Container without [CapabilityEndpointSpecs]
    public static partial class MyContainer
    {
        [CapabilityEndpointSpec(""test"", CapabilityEndpointHttpMethod.Get, ""items"")]
        public sealed class MySpec { }
    }
}
";

        var result = Run(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CEP002");
    }

    // ================================================================
    // CEP003: Spec class cannot have methods or constructors with params
    // ================================================================

    [Fact]
    public void CEP003_SpecWithMethod_EmitsError()
    {
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSpecs]
    public static partial class MyContainer
    {
        [CapabilityEndpointSpec(""test"", CapabilityEndpointHttpMethod.Get, ""items"")]
        public sealed class MySpec
        {
            public void DoSomething() { }
        }
    }
}
";

        var result = Run(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CEP003");
    }

    // ================================================================
    // CEP004: Spec inside [CrestService]
    // ================================================================

    [Fact]
    public void CEP004_SpecInsideCrestService_EmitsError()
    {
        var source = @"
using CrestCreates.DynamicApi;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNs
{
    [CrestService]
    public sealed class MyService
    {
        [CapabilityEndpointSpec(""test"", CapabilityEndpointHttpMethod.Get, ""items"")]
        public sealed class MySpec { }
    }
}
";

        var result = Run(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CEP004");
    }

    // ================================================================
    // CEP005: Spec cannot coexist with [DynamicApiRoute]
    // ================================================================

    [Fact]
    public void CEP005_SpecWithDynamicApiRoute_EmitsError()
    {
        var source = @"
using CrestCreates.DynamicApi;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNs
{
    [CapabilityEndpointSpecs]
    public static partial class MyContainer
    {
        [CapabilityEndpointSpec(""test"", CapabilityEndpointHttpMethod.Get, ""items"")]
        [DynamicApiRoute]
        public sealed class MySpec { }
    }
}
";

        var result = Run(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CEP005");
    }

    // ================================================================
    // CEP008: Route+Body DTO missing settable property
    // ================================================================

    [Fact]
    public void CEP008_RouteBodyDtoMissingProperty_EmitsError()
    {
        // Post with route "items/{id}" but body DTO has no "Id" settable property
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    public sealed class CreateBody
    {
        public string Name { get; set; } = string.Empty;
        // Missing: public int Id { get; set; }
    }

    [CapabilityEndpointSet]
    public static partial class ItemApi
    {
        [Post(""create-item"", ""items/{id}"", Body = typeof(CreateBody))]
        public sealed partial class CreateItem { }
    }
}
";

        var result = Run(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CEP008");
    }

    // ================================================================
    // CEP009: [CapabilityEndpointSet] on non-static class
    // ================================================================

    [Fact]
    public void CEP009_SetOnNonStaticClass_EmitsError()
    {
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSet]
    public partial class NonStaticContainer { }
}
";

        var result = Run(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CEP009");
    }

    // ================================================================
    // CEP010: HTTP method attribute on non-sealed partial nested class
    // ================================================================

    [Fact]
    public void CEP010_NonPartialPostClass_EmitsError()
    {
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSet]
    public static partial class ItemApi
    {
        // Missing 'partial' keyword
        [Post(""create-item"", ""items"")]
        public sealed class CreateItem { }
    }
}
";

        var result = Run(source);
        var errors = result.GetErrors().ToList();

        Assert.Contains(errors, e => e.Id == "CEP010");
    }

    // ================================================================
    // CEP011: [Post] without Body (warning)
    // ================================================================

    [Fact]
    public void CEP011_PostWithoutBody_EmitsWarning()
    {
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSet]
    public static partial class ItemApi
    {
        [Post(""create-item"", ""items"")]
        public sealed partial class CreateItem { }
    }
}
";

        var result = Run(source);
        var warnings = result.GetWarnings().ToList();

        Assert.Contains(warnings, w => w.Id == "CEP011");
    }

    // ================================================================
    // Valid specs do not emit specific errors
    // ================================================================

    [Fact]
    public void Valid_Level1_EmptySpec_DoesNotEmit_CEP003()
    {
        // An empty sealed nested class (with only implicit default constructor)
        // should NOT trigger CEP003
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSpecs]
    public static partial class TestEndpoints
    {
        [CapabilityEndpointSpec(""test.empty"", CapabilityEndpointHttpMethod.Get, ""/api/test"")]
        public sealed class EmptySpec { }
    }
}
";

        var result = Run(source);
        result.Diagnostics.Should().NotContain(d => d.Id == "CEP003");
    }

    // ================================================================
    // CEP016: Level 2 HTTP method attribute without [CapabilityEndpointSet]
    // ================================================================

    [Fact]
    public void Level2HttpMethod_WithoutCapabilityEndpointSet_Emits_CEP016()
    {
        // [Get] on a class nested in a container WITHOUT [CapabilityEndpointSet] should emit CEP016
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    public static partial class SomeContainer
    {
        [Get(""test.get"", ""{id}"", Input = typeof(string))]
        public sealed partial class GetTest { }
    }
}
";

        var result = Run(source);
        result.Diagnostics.Should().Contain(d => d.Id == "CEP016");
    }
}
