using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using CrestCreates.AspNetCore.Middlewares;
using CrestCreates.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrestCreates.IntegrationTests;

/// <summary>
/// Integration tests for the concurrency control exception-to-HTTP mapping.
///
/// Note: Full end-to-end tests (stale ConcurrencyStamp via API -> 409) require
/// the entity DTOs to carry ConcurrencyStamp. Currently the generated DTOs do not
/// include it. Once that is added, this test class should be extended with:
///   - Create a resource, get its stamp, submit update with stale stamp -> 409
///
/// Until then, these tests verify the middleware mapping layer directly.
/// </summary>
public class ConcurrencyIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task ExceptionHandlingMiddleware_MapsCrestConcurrencyException_To409Conflict()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = new ExceptionHandlingMiddleware(
            next: _ => throw new CrestConcurrencyException("TestEntity", Guid.NewGuid().ToString()),
            logger: NullLogger<ExceptionHandlingMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        // Assert
        Assert.Equal((int)HttpStatusCode.Conflict, context.Response.StatusCode);

        var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(body, JsonOptions);
        Assert.NotNull(errorResponse);
        Assert.Equal((int)HttpStatusCode.Conflict, errorResponse.Code);
        Assert.Contains("数据已被其他用户修改", errorResponse.Message);
    }

    [Fact]
    public async Task ExceptionHandlingMiddleware_MapsCrestPreconditionRequiredException_To428()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = new ExceptionHandlingMiddleware(
            next: _ => throw new CrestPreconditionRequiredException("TestEntity", Guid.NewGuid().ToString()),
            logger: NullLogger<ExceptionHandlingMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        // Assert
        Assert.Equal(428, context.Response.StatusCode);

        var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(body, JsonOptions);
        Assert.NotNull(errorResponse);
        Assert.Equal(428, errorResponse.Code);
        Assert.Contains("If-Match", errorResponse.Message);
    }
}
