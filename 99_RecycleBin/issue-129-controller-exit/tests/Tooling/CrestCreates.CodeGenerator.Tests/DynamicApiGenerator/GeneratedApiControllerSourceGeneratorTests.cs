using System;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.CodeGenerator.DynamicApiGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.DynamicApiGenerator;

public class GeneratedApiControllerSourceGeneratorTests
{
    [Fact]
    public async Task ShouldGenerateMinimalApiEndpointForGeneratedApiControllerMethod()
    {
        var source = """
            using CrestCreates.DynamicApi;
            using Microsoft.AspNetCore.Mvc;
            using Microsoft.AspNetCore.Http;
            using System.Threading.Tasks;

            namespace Sample;

            [GeneratedApiController("api/books")]
            public partial class BookApi : CrestApiController
            {
                [HttpGet("by-slug/{slug}")]
                public Task<string> GetBySlugAsync(string slug)
                {
                    return Task.FromResult(slug);
                }
            }
            """;

        var result = await SourceGeneratorTestHelper.RunGeneratorAsync<DynamicApiAotSourceGenerator>(
            source,
            additionalSources: new[] { LegacyDynamicApiAotSourceGeneratorTests.BuildDynamicApiStubs(), BuildGeneratedApiControllerStubs() });

        result.HasNoErrors().Should().BeTrue(string.Join(Environment.NewLine, result.GetErrors()));

        result.ContainsFile("GeneratedDynamicApiEndpoints.g.cs").Should().BeTrue();

        var endpointsSource = result.GetSourceByFileName("GeneratedDynamicApiEndpoints.g.cs");
        endpointsSource.Should().NotBeNull();
        endpointsSource!.SourceText
            .Should().Contain("MapMethods")
            .And.Contain("api/books/by-slug/{slug}")
            .And.Contain("GetBySlug");
    }

    [Fact]
    public async Task ShouldGenerateDiRegistrationForGeneratedApiController()
    {
        var source = """
            using CrestCreates.DynamicApi;
            using Microsoft.AspNetCore.Mvc;
            using Microsoft.AspNetCore.Http;
            using System.Threading.Tasks;

            namespace Sample;

            [GeneratedApiController("api/orders")]
            public partial class OrderApi : CrestApiController
            {
                [HttpPost]
                public Task<string> ProcessAsync()
                {
                    return Task.FromResult("done");
                }
            }
            """;

        var result = await SourceGeneratorTestHelper.RunGeneratorAsync<DynamicApiAotSourceGenerator>(
            source,
            additionalSources: new[] { LegacyDynamicApiAotSourceGeneratorTests.BuildDynamicApiStubs(), BuildGeneratedApiControllerStubs() });

        result.HasNoErrors().Should().BeTrue(string.Join(Environment.NewLine, result.GetErrors()));

        result.ContainsFile("GeneratedDynamicApiEndpoints.g.cs").Should().BeTrue();

        var endpointsSource = result.GetSourceByFileName("GeneratedDynamicApiEndpoints.g.cs");
        endpointsSource.Should().NotBeNull();
        endpointsSource!.SourceText
            .Should().Contain("MapMethods")
            .And.Contain("api/orders")
            .And.Contain("GetRequiredService<global::Sample.OrderApi>()")
            .And.Contain("Process");
    }

    [Fact]
    public async Task ShouldGenerateMetadataForCommonApiControllerAttributes()
    {
        var source = """
            using CrestCreates.DynamicApi;
            using Microsoft.AspNetCore.Authorization;
            using Microsoft.AspNetCore.Mvc;
            using System.Threading.Tasks;

            namespace Sample;

            [GeneratedApiController]
            [Authorize("Books.Read")]
            [Route("api/[controller]")]
            public partial class BookApi : CrestApiController
            {
                [HttpPost("publish/{id}")]
                [ProducesResponseType(typeof(string), 200)]
                [Produces("application/json")]
                [Consumes("application/json")]
                public Task<string> PublishAsync(string id, PublishBookDto input)
                {
                    return Task.FromResult(id);
                }

                [HttpGet("ping")]
                [AllowAnonymous]
                public Task<string> PingAsync()
                {
                    return Task.FromResult("pong");
                }
            }

            public sealed class PublishBookDto
            {
                public string Name { get; set; } = string.Empty;
            }
            """;

        var result = await SourceGeneratorTestHelper.RunGeneratorAsync<DynamicApiAotSourceGenerator>(
            source,
            additionalSources: new[] { LegacyDynamicApiAotSourceGeneratorTests.BuildDynamicApiStubs(), BuildGeneratedApiControllerStubs() });

        result.HasNoErrors().Should().BeTrue(string.Join(Environment.NewLine, result.GetErrors()));
        var endpointsSource = result.GetSourceByContent("MapMethods");
        endpointsSource.Should().NotBeNull();
        endpointsSource!.SourceText.Should().Contain("api/book/publish/{id}");
        endpointsSource.SourceText.Should().Contain("api/book/ping");
        endpointsSource.SourceText.Should().Contain("new global::Microsoft.AspNetCore.Mvc.RouteAttribute(\"api/book/publish/{id}\")");
        endpointsSource.SourceText.Should().Contain("new global::Microsoft.AspNetCore.Authorization.AuthorizeAttribute() { Policy = \"Books.Read\" }");
        endpointsSource.SourceText.Should().Contain("new global::Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute(typeof(global::System.String), 200)");
        endpointsSource.SourceText.Should().Contain("new global::Microsoft.AspNetCore.Mvc.ProducesAttribute(\"application/json\")");
        endpointsSource.SourceText.Should().Contain("new global::Microsoft.AspNetCore.Mvc.ConsumesAttribute(\"application/json\")");
        endpointsSource.SourceText.Should().Contain("new global::Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute()");
        endpointsSource.SourceText.Should().Contain("await DynamicApiGeneratedRuntime.EnsurePermissionAsync(context, permissionChecker, permission_controller_0_0.Permissions);");
        endpointsSource.SourceText.Should().NotContain("await DynamicApiGeneratedRuntime.EnsurePermissionAsync(context, permissionChecker, permission_controller_0_1.Permissions);");
    }

    [Fact]
    public async Task ShouldUseApiOverrideToReplaceDefaultGetAllEndpoint()
    {
        var source = """
            using CrestCreates.Domain.Shared.Attributes;
            using CrestCreates.DynamicApi;
            using Microsoft.AspNetCore.Mvc;
            using System.Threading.Tasks;

            namespace Sample;

            public interface IBookAppService
            {
                Task<string> GetAllAsync();
            }

            [CrestService]
            public sealed class BookAppService : IBookAppService
            {
                public Task<string> GetAllAsync() => Task.FromResult("default");
            }

            [GeneratedApiController("api/book")]
            public partial class BookApi : CrestApiController
            {
                [HttpGet("all")]
                [ApiOverride(CrudAction.GetList)]
                public Task<string> GetAllAsync()
                {
                    return Task.FromResult("custom");
                }
            }
            """;

        var result = await SourceGeneratorTestHelper.RunGeneratorAsync<DynamicApiAotSourceGenerator>(
            source,
            additionalSources: new[] { LegacyDynamicApiAotSourceGeneratorTests.BuildDynamicApiStubs(), BuildGeneratedApiControllerStubs() });

        result.HasNoErrors().Should().BeTrue(string.Join(Environment.NewLine, result.GetErrors()));

        var generated = result.GeneratedSources.First(s => s.FileName.Contains("GeneratedDynamicApiEndpoints.g.cs"));
        generated.Should().NotBeNull();

        generated.SourceText.Should().Contain("BookApi");
        generated.SourceText.Should().Contain("GetAllAsync()");
        generated.SourceText.Should().Contain("api/book/all");
        // The default service endpoint for the same CRUD action should NOT be generated
        // (the controller's override suppresses it)
        generated.SourceText.Should().NotContain("IBookAppService.GetAll");
    }

    [Fact]
    public async Task ShouldGenerateControllerOnlyEndpointsWithoutCrestService()
    {
        var source = """
            using CrestCreates.DynamicApi;
            using Microsoft.AspNetCore.Mvc;
            using System.Threading.Tasks;

            namespace Sample;

            [GeneratedApiController("api/ping")]
            public partial class PingApi : CrestApiController
            {
                [HttpGet]
                public Task<string> GetAsync()
                {
                    return Task.FromResult("pong");
                }
            }
            """;

        var result = await SourceGeneratorTestHelper.RunGeneratorAsync<DynamicApiAotSourceGenerator>(
            source,
            additionalSources: new[] { LegacyDynamicApiAotSourceGeneratorTests.BuildDynamicApiStubs(), BuildGeneratedApiControllerStubs() });

        result.HasNoErrors().Should().BeTrue(string.Join(Environment.NewLine, result.GetErrors()));

        result.ContainsFile("GeneratedDynamicApiRegistry.g.cs").Should().BeTrue();
        result.ContainsFile("GeneratedDynamicApiEndpoints.g.cs").Should().BeTrue();
        result.ContainsFile("GeneratedDynamicApiControllerRegistrations.g.cs").Should().BeTrue();

        var registrySource = result.GetSourceByFileName("GeneratedDynamicApiRegistry.g.cs");
        registrySource.Should().NotBeNull();
        registrySource!.SourceText
            .Should().Contain("EndpointDescriptors")
            .And.Contain("\"Ping\"")
            .And.Contain("\"Get\"");

        var endpointsSource = result.GetSourceByFileName("GeneratedDynamicApiEndpoints.g.cs");
        endpointsSource.Should().NotBeNull();
        endpointsSource!.SourceText
            .Should().Contain("MapMethods")
            .And.Contain("api/ping")
            .And.Contain("GetRequiredService<global::Sample.PingApi>()");
    }

    private static string BuildGeneratedApiControllerStubs()
    {
        return """
            using System;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Mvc;

            namespace CrestCreates.DynamicApi
            {
                [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
                public sealed class GeneratedApiControllerAttribute : Attribute
                {
                    public GeneratedApiControllerAttribute() { }
                    public GeneratedApiControllerAttribute(string routeTemplate) { RouteTemplate = routeTemplate; }
                    public string? RouteTemplate { get; }
                }

                [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
                public sealed class ApiOverrideAttribute : Attribute
                {
                    public ApiOverrideAttribute(CrudAction action) { Action = action; }
                    public CrudAction Action { get; }
                }

                public enum CrudAction
                {
                    Get = 0,
                    GetList = 1,
                    Create = 2,
                    Update = 3,
                    Delete = 4
                }

                public abstract class CrestApiController
                {
                    protected IResult Ok<T>(T value) { return Results.Ok(value); }
                    protected IResult NotFound() { return Results.NotFound(null); }
                }
            }

            namespace Microsoft.AspNetCore.Authorization
            {
                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
                public sealed class AuthorizeAttribute : Attribute
                {
                    public AuthorizeAttribute() { }
                    public AuthorizeAttribute(string policy) { Policy = policy; }
                    public string? Policy { get; set; }
                    public string? Roles { get; set; }
                    public string? AuthenticationSchemes { get; set; }
                }

                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
                public sealed class AllowAnonymousAttribute : Attribute
                {
                }
            }

            namespace Microsoft.AspNetCore.Mvc
            {
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
                public sealed class HttpGetAttribute : Attribute
                {
                    public HttpGetAttribute() { }
                    public HttpGetAttribute(string template) { Template = template; }
                    public string? Template { get; }
                }

                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
                public sealed class HttpPostAttribute : Attribute
                {
                    public HttpPostAttribute() { }
                    public HttpPostAttribute(string template) { Template = template; }
                    public string? Template { get; }
                }

                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
                public sealed class HttpPutAttribute : Attribute
                {
                    public HttpPutAttribute() { }
                    public HttpPutAttribute(string template) { Template = template; }
                    public string? Template { get; }
                }

                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
                public sealed class HttpDeleteAttribute : Attribute
                {
                    public HttpDeleteAttribute() { }
                    public HttpDeleteAttribute(string template) { Template = template; }
                    public string? Template { get; }
                }

                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
                public sealed class RouteAttribute : Attribute
                {
                    public RouteAttribute(string template) { Template = template; }
                    public string Template { get; }
                }

                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
                public sealed class ProducesResponseTypeAttribute : Attribute
                {
                    public ProducesResponseTypeAttribute(int statusCode) { StatusCode = statusCode; }
                    public ProducesResponseTypeAttribute(Type type, int statusCode)
                    {
                        Type = type;
                        StatusCode = statusCode;
                    }

                    public Type? Type { get; }
                    public int StatusCode { get; }
                }

                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
                public sealed class ProducesAttribute : Attribute
                {
                    public ProducesAttribute(params string[] contentTypes) { ContentTypes = contentTypes; }
                    public string[] ContentTypes { get; }
                }

                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
                public sealed class ConsumesAttribute : Attribute
                {
                    public ConsumesAttribute(params string[] contentTypes) { ContentTypes = contentTypes; }
                    public string[] ContentTypes { get; }
                }
            }
            """;
    }
}
