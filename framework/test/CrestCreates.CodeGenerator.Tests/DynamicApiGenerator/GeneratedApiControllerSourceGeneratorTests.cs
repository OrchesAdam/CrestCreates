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
