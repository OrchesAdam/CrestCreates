using CrestCreates.DynamicApi;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

public class GeneratedApiControllerAbstractionsTests
{
    [Fact]
    public void CrestApiController_ShouldNotInheritMvcControllerBase()
    {
        typeof(ControllerBase).IsAssignableFrom(typeof(CrestApiController)).Should().BeFalse();
    }

    [Fact]
    public void GeneratedApiControllerAttribute_ShouldStoreRouteTemplate()
    {
        var attribute = new GeneratedApiControllerAttribute("api/books");

        attribute.RouteTemplate.Should().Be("api/books");
    }

    [Fact]
    public void ApiOverrideAttribute_ShouldStoreCrudAction()
    {
        var attribute = new ApiOverrideAttribute(CrudAction.GetList);

        attribute.Action.Should().Be(CrudAction.GetList);
    }

    [Fact]
    public async Task CrestApiController_Ok_ShouldExecuteOkResult()
    {
        var controller = new TestApiController();

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        using var host = builder.Build();
        var context = new DefaultHttpContext { RequestServices = host.Services };

        var result = controller.Ok("created");

        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    private sealed class TestApiController : CrestApiController
    {
    }
}