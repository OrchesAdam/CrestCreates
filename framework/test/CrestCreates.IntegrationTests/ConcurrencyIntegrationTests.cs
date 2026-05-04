using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CrestCreates.AspNetCore.Errors;
using CrestCreates.AspNetCore.Middlewares;
using CrestCreates.Domain.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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

    private static ExceptionHandlingMiddleware CreateMiddleware(RequestDelegate next)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var converter = new DefaultCrestExceptionConverter(services, NullLogger<DefaultCrestExceptionConverter>.Instance);
        return new ExceptionHandlingMiddleware(next, converter, NullLogger<ExceptionHandlingMiddleware>.Instance);
    }

    [Fact]
    public async Task ExceptionHandlingMiddleware_MapsCrestConcurrencyException_To409Conflict()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = CreateMiddleware(
            _ => throw new CrestConcurrencyException("TestEntity", Guid.NewGuid().ToString()));

        // Act
        await middleware.InvokeAsync(context);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        // Assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);

        var errorResponse = JsonSerializer.Deserialize<CrestErrorResponseForTest>(body, JsonOptions);
        errorResponse.Should().NotBeNull();
        errorResponse!.Code.Should().Be("Crest.Concurrency.Conflict");
        errorResponse.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        errorResponse.TraceId.Should().Be(context.TraceIdentifier);
        errorResponse.Message.Should().Be("Concurrency conflict.");
    }

    [Fact]
    public async Task ExceptionHandlingMiddleware_MapsCrestPreconditionRequiredException_To428()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = CreateMiddleware(
            _ => throw new CrestPreconditionRequiredException("TestEntity", Guid.NewGuid().ToString()));

        // Act
        await middleware.InvokeAsync(context);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        // Assert
        context.Response.StatusCode.Should().Be(428);

        var errorResponse = JsonSerializer.Deserialize<CrestErrorResponseForTest>(body, JsonOptions);
        errorResponse.Should().NotBeNull();
        errorResponse!.Code.Should().Be("Crest.Concurrency.PreconditionRequired");
        errorResponse.StatusCode.Should().Be(428);
        errorResponse.TraceId.Should().Be(context.TraceIdentifier);
        errorResponse.Message.Should().Be("Precondition required.");
    }

    private sealed class CrestErrorResponseForTest
    {
        public string Code { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public string? Details { get; set; }

        public string? TraceId { get; set; }

        public int StatusCode { get; set; }
    }
}
