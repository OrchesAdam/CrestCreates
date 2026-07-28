using System;
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
        public string? EndpointId { get; init; }
        public int EndpointVersion { get; init; }
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
        public string? TargetProperty { get; init; }
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
        public string? EndpointId { get; init; }
        public int EndpointVersion { get; init; }
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
        public string? EndpointId { get; init; }
        public int EndpointVersion { get; init; }
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
        public string? EndpointId { get; init; }
        public int EndpointVersion { get; init; }
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

    // ================================================================
    // CEP017: EndpointId contains whitespace
    // ================================================================

    [Fact]
    public void CEP017_Fires_When_EndpointId_Contains_Whitespace()
    {
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSpecs]
    public static partial class TestEndpoints
    {
        [CapabilityEndpointSpec(""books"", CapabilityEndpointHttpMethod.Get, ""/books"", EndpointId = ""my endpoint"")]
        public sealed class BooksSpec { }
    }
}
";

        var result = Run(source);
        result.Diagnostics.Should().Contain(d => d.Id == "CEP017");
    }

    [Fact]
    public void CEP020_Fires_When_EndpointVersion_Is_Negative()
    {
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSpecs]
    public static partial class TestEndpoints
    {
        [CapabilityEndpointSpec(""books"", CapabilityEndpointHttpMethod.Get, ""/books"", EndpointVersion = -1)]
        public sealed class BooksSpec { }
    }
}
";

        var result = Run(source);
        result.Diagnostics.Should().Contain(d => d.Id == "CEP020");
    }

    [Fact]
    public void EndpointId_NoWhitespace_NoDiagnostic()
    {
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSpecs]
    public static partial class TestEndpoints
    {
        [CapabilityEndpointSpec(""books"", CapabilityEndpointHttpMethod.Get, ""/books"", EndpointId = ""admin-books"")]
        public sealed class BooksSpec { }
    }
}
";

        var result = Run(source);
        result.Diagnostics.Should().NotContain(d => d.Id == "CEP017");
    }

    [Fact]
    public void EndpointVersion_Zero_FallsBack_To_CapabilityVersion()
    {
        // EndpointVersion = 0 uses CapabilityVersion fallback — no error
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSpecs]
    public static partial class TestEndpoints
    {
        [CapabilityEndpointSpec(""books"", CapabilityEndpointHttpMethod.Get, ""/books"", EndpointVersion = 0)]
        public sealed class BooksSpec { }
    }
}
";

        var result = Run(source);
        result.Diagnostics.Should().NotContain(d => d.Id == "CEP020");
    }

    // ================================================================
    // CEP018: TargetProperty does not exist on body type
    // ================================================================

    [Fact]
    public void CEP018_Fires_When_TargetProperty_Missing_On_Body()
    {
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    public sealed class SomeDto
    {
        public string ValidProp { get; set; } = string.Empty;
    }

    [CapabilityEndpointSpecs]
    public static partial class TestEndpoints
    {
        [CapabilityEndpointSpec(""test.spec"", CapabilityEndpointHttpMethod.Get, ""items/{id}"")]
        [CapabilityEndpointInput(typeof(SomeDto), Name = ""body"", Source = CapabilityEndpointParameterSource.Body)]
        [CapabilityEndpointInput(typeof(int), Name = ""id"", Source = CapabilityEndpointParameterSource.Route, TargetProperty = ""NonExistentProp"")]
        public sealed class MySpec { }
    }
}
";

        var result = Run(source);
        result.Diagnostics.Should().Contain(d => d.Id == "CEP018");
    }

    // ================================================================
    // CEP019: TargetProperty is not a valid C# identifier
    // ================================================================

    [Fact]
    public void CEP019_Fires_When_TargetProperty_Contains_Dot()
    {
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSpecs]
    public static partial class TestEndpoints
    {
        [CapabilityEndpointSpec(""test.spec"", CapabilityEndpointHttpMethod.Get, ""items/{id}"")]
        [CapabilityEndpointInput(typeof(int), Name = ""id"", Source = CapabilityEndpointParameterSource.Route, TargetProperty = ""Address.City"")]
        public sealed class MySpec { }
    }
}
";

        var result = Run(source);
        result.Diagnostics.Should().Contain(d => d.Id == "CEP019");
    }

    [Fact]
    public void CEP019_Fires_When_TargetProperty_Contains_Dash()
    {
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSpecs]
    public static partial class TestEndpoints
    {
        [CapabilityEndpointSpec(""test.spec"", CapabilityEndpointHttpMethod.Get, ""items/{id}"")]
        [CapabilityEndpointInput(typeof(int), Name = ""id"", Source = CapabilityEndpointParameterSource.Route, TargetProperty = ""my-prop"")]
        public sealed class MySpec { }
    }
}
";

        var result = Run(source);
        result.Diagnostics.Should().Contain(d => d.Id == "CEP019");
    }

    [Fact]
    public void TargetProperty_Valid_NoDiagnostic()
    {
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    public sealed class SomeDto
    {
        public string ValidProp { get; set; } = string.Empty;
    }

    [CapabilityEndpointSpecs]
    public static partial class TestEndpoints
    {
        [CapabilityEndpointSpec(""test.spec"", CapabilityEndpointHttpMethod.Get, ""items/{id}"")]
        [CapabilityEndpointInput(typeof(SomeDto), Name = ""body"", Source = CapabilityEndpointParameterSource.Body)]
        [CapabilityEndpointInput(typeof(int), Name = ""id"", Source = CapabilityEndpointParameterSource.Route, TargetProperty = ""ValidProp"")]
        public sealed class MySpec { }
    }
}
";

        var result = Run(source);
        result.Diagnostics.Should().NotContain(d => d.Id == "CEP018");
        result.Diagnostics.Should().NotContain(d => d.Id == "CEP019");
    }

    // ================================================================
    // CEP013: Multiple scalar inputs without body → Error
    // ================================================================

    [Fact]
    public void CEP013_Is_Error_Not_Warning()
    {
        var descriptor = CapabilityEndpointDiagnostics.MultipleRouteParamsWithoutBody;
        descriptor.DefaultSeverity.Should().Be(DiagnosticSeverity.Error);
    }

    [Fact]
    public void CEP013_Fires_For_Route_Plus_Route_Without_Body()
    {
        // Two route tokens, no Body → CEP013 Error
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSet]
    public static partial class TestApi
    {
        [Get(""test.get"", ""items/{id}/{subId}"")]
        public sealed partial class GetItem { }
    }
}
";

        var result = Run(source);
        result.Diagnostics.Should().Contain(d => d.Id == "CEP013"
            && d.Severity == DiagnosticSeverity.Error);
    }

    // Note: Route+Query, Query+Header, Header+Header CEP013 tests removed for Level 2.
    // Level 2 does not read class-level [CapabilityEndpointInput] for binding generation,
    // so these combinations are not diagnosed. Level 1 covers these cases.

    [Fact]
    public void CEP013_DoesNotFire_For_Single_Scalar_Route()
    {
        // Single route token, no Body → no CEP013
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSet]
    public static partial class TestApi
    {
        [Get(""test.get"", ""items/{id}"")]
        public sealed partial class GetItem { }
    }
}
";

        var result = Run(source);
        result.Diagnostics.Should().NotContain(d => d.Id == "CEP013");
    }

    [Fact]
    public void CEP013_DoesNotFire_For_Single_Scalar_Query()
    {
        // Single Query input, no Body → no CEP013
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSet]
    public static partial class TestApi
    {
        [Get(""test.get"", ""items"")]
        [CapabilityEndpointInput(typeof(int), Name = ""q"", Source = CapabilityEndpointParameterSource.Query)]
        public sealed partial class GetItem { }
    }
}
";

        var result = Run(source);
        result.Diagnostics.Should().NotContain(d => d.Id == "CEP013");
    }

    [Fact]
    public void CEP013_DoesNotFire_For_Body_Plus_Multiple_Scalars()
    {
        // Body + multiple Route/Query/Header → no CEP013
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    public sealed class CreateDto
    {
        public string Name { get; set; } = string.Empty;
        public int Id { get; set; }
    }

    [CapabilityEndpointSet]
    public static partial class TestApi
    {
        [Post(""test.post"", ""items/{id}"", Body = typeof(CreateDto))]
        [CapabilityEndpointInput(typeof(int), Name = ""q"", Source = CapabilityEndpointParameterSource.Query)]
        public sealed partial class CreateItem { }
    }
}
";

        var result = Run(source);
        result.Diagnostics.Should().NotContain(d => d.Id == "CEP013");
    }

    [Fact]
    public void CEP013_DoesNotFire_For_Single_RouteToken_Plus_Explicit_Input()
    {
        // One route token + explicit Input on the HTTP method attribute, no Body → no CEP013.
        // Level 2 Input binds the route token's type — it is not an additional scalar input.
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSet]
    public static partial class TestApi
    {
        [Get(""test.get"", ""items/{id}"", Input = typeof(string))]
        public sealed partial class GetItem { }
    }
}
";

        var result = Run(source);
        result.Diagnostics.Should().NotContain(d => d.Id == "CEP013");
    }

    [Fact]
    public void CEP013_Fires_For_Multiple_RouteTokens_Plus_Explicit_Input()
    {
        // Two route tokens + explicit Input, no Body → CEP013 Error.
        // Input cannot unambiguously bind to one of multiple route tokens.
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSet]
    public static partial class TestApi
    {
        [Get(""test.get"", ""items/{id}/sub/{subId}"", Input = typeof(string))]
        public sealed partial class GetItem { }
    }
}
";

        var result = Run(source);
        result.Diagnostics.Should().Contain(d => d.Id == "CEP013"
            && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void CEP021_Fires_For_Input_Without_RouteToken()
    {
        // Input with no route tokens → CEP021 Error.
        // Level 2 Input requires at least one route token to bind to.
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSet]
    public static partial class TestApi
    {
        [Get(""test.get"", ""items"", Input = typeof(string))]
        public sealed partial class GetItem { }
    }
}
";

        var result = Run(source);
        result.Diagnostics.Should().Contain(d => d.Id == "CEP021"
            && d.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("public sealed record PositionalInput(Guid Id);", "PositionalInput")]
    [InlineData("public sealed class PrivateCtorInput { private PrivateCtorInput() { } public Guid Id { get; set; } }", "PrivateCtorInput")]
    [InlineData("public abstract class AbstractInput { public Guid Id { get; set; } }", "AbstractInput")]
    [InlineData("public interface InterfaceInput { Guid Id { get; set; } }", "InterfaceInput")]
    public void CEP022_Fires_When_OptionalBody_CannotBeMaterialized(
        string typeDeclaration,
        string typeName)
    {
        var source = $@"
using System;
using CrestCreates.DynamicApi;

namespace TestNs
{{
    {typeDeclaration}

    [CapabilityEndpointSpecs]
    public static partial class TestEndpoints
    {{
        [CapabilityEndpointSpec(""test.get"", CapabilityEndpointHttpMethod.Get, ""items/{{id}}"")]
        [CapabilityEndpointInput(typeof({typeName}), Name = ""body"", Source = CapabilityEndpointParameterSource.Body, Required = false)]
        [CapabilityEndpointInput(typeof(Guid), Name = ""id"", Source = CapabilityEndpointParameterSource.Route, TargetProperty = nameof({typeName}.Id))]
        public sealed class GetItem {{ }}
    }}
}}
";

        var result = Run(source);

        result.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CEP022"
            && diagnostic.Severity == DiagnosticSeverity.Error);
        var binding = result.GeneratedSources.Single(item => item.FileName.EndsWith("_Bindings.g.cs"))
            .SourceText;
        binding.Should().Contain("CEP022: Optional body type");
        binding.Should().NotContain($"new global::TestNs.{typeName}()");
    }

    // ================================================================
    // BindingEmitter: Multi-scalar no body generates throw, not Dictionary
    // ================================================================

    [Fact]
    public void BindingEmitter_MultiScalar_NoBody_Generates_Throw_Not_Dictionary()
    {
        // Multi scalar inputs without body should generate a throw, not a Dictionary.
        // Uses Level 1 [CapabilityEndpointSpec] + [CapabilityEndpointInput] since Level 2
        // normalization only extracts inputs from HTTP method attributes (not class-level inputs).
        var source = @"
using CrestCreates.DynamicApi;

namespace TestNs
{
    [CapabilityEndpointSpecs]
    public static partial class TestEndpoints
    {
        [CapabilityEndpointSpec(""test.get"", CapabilityEndpointHttpMethod.Get, ""{id}"")]
        [CapabilityEndpointInput(typeof(int), Name = ""id"", Source = CapabilityEndpointParameterSource.Route)]
        [CapabilityEndpointInput(typeof(int), Name = ""q"", Source = CapabilityEndpointParameterSource.Query)]
        public sealed class GetItem { }
    }
}
";

        var result = Run(source);

        // Get the binding source
        var bindingSource = result.GeneratedSources
            .FirstOrDefault(s => s.FileName.EndsWith("_Bindings.g.cs"));
        bindingSource.Should().NotBeNull("binding source should be generated");

        var code = bindingSource!.SourceText;

        // Must contain throw with CEP013
        code.Should().Contain("throw new InvalidOperationException");
        code.Should().Contain("CEP013");

        // Must NOT contain Dictionary
        code.Should().NotContain("Dictionary<string, object?>");
        code.Should().NotContain("Dictionary<string,object?>");
        code.Should().NotContain("new System.Collections.Generic.Dictionary");
        code.Should().NotContain("dict[");
    }
}
