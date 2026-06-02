using System.Text.Json;
using CrestCreates.AspNetCore.Errors;
using CrestCreates.AspNetCore.Middlewares;
using CrestCreates.AspNetCore.Serialization;
using CrestCreates.Domain.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrestCreates.Web.Tests.Middlewares;

public class ConcurrencyExceptionIntegrationTests
{
    [Fact]
    public async Task InvokeAsync_WithConcurrencyException_Returns409()
    {
        var middleware = CreateMiddleware(
            _ => throw new CrestConcurrencyException("Book", "book-123"));
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "trace-concurrency"
        };
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);

        var response = await DeserializeResponseAsync(context);
        response.Code.Should().Be("Crest.Concurrency.Conflict");
        response.StatusCode.Should().Be(409);
        response.Details.Should().Contain("Book");
        response.Details.Should().Contain("book-123");
        response.TraceId.Should().Be("trace-concurrency");
    }

    [Fact]
    public async Task InvokeAsync_WithConcurrencyException_ReturnsWarningLogLevel()
    {
        var logger = new TestLogger<ExceptionHandlingMiddleware>();
        var middleware = CreateMiddleware(
            _ => throw new CrestConcurrencyException("Book", "book-123"),
            logger);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning);
    }

    private static readonly CrestExceptionLocalizationResources _emptyResources
        = new(new Dictionary<string, IReadOnlyDictionary<string, string>>());

    private static ExceptionHandlingMiddleware CreateMiddleware(
        RequestDelegate next,
        TestLogger<ExceptionHandlingMiddleware>? logger = null)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var converter = new DefaultCrestExceptionConverter(
            services,
            _emptyResources,
            NullLogger<DefaultCrestExceptionConverter>.Instance);
        var jsonContext = new CrestErrorResponseJsonContext();
        return new ExceptionHandlingMiddleware(next, converter, logger ?? new TestLogger<ExceptionHandlingMiddleware>(), jsonContext);
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
