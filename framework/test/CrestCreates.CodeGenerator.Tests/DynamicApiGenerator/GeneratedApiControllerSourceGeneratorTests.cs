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
            additionalSources: new[] { DynamicApiAotSourceGeneratorTests.BuildDynamicApiStubs(), BuildGeneratedApiControllerStubs() });

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
            additionalSources: new[] { DynamicApiAotSourceGeneratorTests.BuildDynamicApiStubs(), BuildGeneratedApiControllerStubs() });

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
    public async Task ShouldUseApiOverrideToReplaceDefaultGetListEndpoint()
    {
        var source = """
            using CrestCreates.Domain.Shared.Attributes;
            using CrestCreates.DynamicApi;
            using System.Threading.Tasks;

            namespace Sample;

            public interface IBookAppService
            {
                Task<string> GetListAsync();
            }

            [CrestService]
            public sealed class BookAppService : IBookAppService
            {
                public Task<string> GetListAsync() => Task.FromResult("default");
            }

            [GeneratedApiController("api/book")]
            public partial class BookApi : CrestApiController
            {
                [ApiOverride(CrudAction.GetList)]
                public Task<string> GetListAsync()
                {
                    return Task.FromResult("custom");
                }
            }
            """;

        var result = await SourceGeneratorTestHelper.RunGeneratorAsync<DynamicApiAotSourceGenerator>(
            source,
            additionalSources: new[] { DynamicApiAotSourceGeneratorTests.BuildDynamicApiStubs(), BuildGeneratedApiControllerStubs() });

        result.HasNoErrors().Should().BeTrue(string.Join(Environment.NewLine, result.GetErrors()));

        var generated = result.GeneratedSources.First(s => s.FileName.Contains("GeneratedDynamicApiEndpoints.g.cs"));
        generated.Should().NotBeNull();

        generated.SourceText.Should().Contain("BookApi");
        generated.SourceText.Should().Contain("GetListAsync()");
        // The default service endpoint for the same CRUD action should NOT be generated
        // (the controller's override suppresses it)
        generated.SourceText.Should().NotContain("IBookAppService.GetList");
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
            }
            """;
    }
}
