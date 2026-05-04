using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using CrestCreates.AspNetCore.Errors;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.AspNetCore.Middlewares;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrestCreates.Web.Tests.Middlewares;

public class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ShouldReturnUnauthorized_ForUnauthorizedAccessException()
    {
        var logger = new TestLogger<ExceptionHandlingMiddleware>();
        var middleware = CreateMiddleware(_ => throw new UnauthorizedAccessException(), logger);
        var context = new DefaultHttpContext { TraceIdentifier = "trace-401" };
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning);

        var response = await DeserializeResponseAsync(context);
        response.Code.Should().Be("Crest.Auth.Unauthorized");
        response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        response.TraceId.Should().Be("trace-401");
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnForbidden_ForPermissionException()
    {
        var logger = new TestLogger<ExceptionHandlingMiddleware>();
        var middleware = CreateMiddleware(
            _ => throw new CrestPermissionException("Books.Delete"),
            logger);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        var response = await DeserializeResponseAsync(context);
        response.Code.Should().Be("Crest.Auth.Forbidden");
        response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        response.Message.Should().Be("没有权限执行当前操作。");
        response.Details.Should().Be("Books.Delete");
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnBadRequest_ForValidationException()
    {
        var logger = new TestLogger<ExceptionHandlingMiddleware>();
        var middleware = CreateMiddleware(
            _ => throw new ValidationException("validation failed"),
            logger);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var response = await DeserializeResponseAsync(context);
        response.Code.Should().Be("Crest.Validation.Failed");
        response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        response.Message.Should().Be("数据验证失败。");
        response.Details.Should().Be("validation failed");
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnInternalServerError_ForUnhandledException()
    {
        var logger = new TestLogger<ExceptionHandlingMiddleware>();
        var middleware = CreateMiddleware(
            _ => throw new Exception("boom"),
            logger);
        var context = new DefaultHttpContext { TraceIdentifier = "trace-500" };
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error);
        var response = await DeserializeResponseAsync(context);
        response.Code.Should().Be("Crest.InternalError");
        response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        response.Message.Should().Be("服务器内部错误。");
        response.Details.Should().BeNull();
        response.TraceId.Should().Be("trace-500");
    }

    private static ExceptionHandlingMiddleware CreateMiddleware(RequestDelegate next, TestLogger<ExceptionHandlingMiddleware> logger)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var converter = new DefaultCrestExceptionConverter(services, NullLogger<DefaultCrestExceptionConverter>.Instance);
        return new ExceptionHandlingMiddleware(next, converter, logger);
    }

    private static async Task<CrestErrorResponse> DeserializeResponseAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var response = await JsonSerializer.DeserializeAsync<CrestErrorResponse>(context.Response.Body, options);
        response.Should().NotBeNull();
        return response!;
    }
}
