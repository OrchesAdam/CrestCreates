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

public class PreconditionRequiredExceptionIntegrationTests
{
    [Fact]
    public async Task InvokeAsync_WithPreconditionRequiredException_Returns428()
    {
        var middleware = CreateMiddleware(
            _ => throw new CrestPreconditionRequiredException("Book", "book-456"));
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "trace-precondition"
        };
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(428);

        var response = await DeserializeResponseAsync(context);
        response.Code.Should().Be("Crest.Concurrency.PreconditionRequired");
        response.StatusCode.Should().Be(428);
        response.Details.Should().Contain("Book");
        response.Details.Should().Contain("book-456");
        response.Details.Should().Contain("If-Match");
        response.TraceId.Should().Be("trace-precondition");
    }

    [Fact]
    public async Task InvokeAsync_WithPreconditionRequiredException_ReturnsWarningLogLevel()
    {
        var logger = new TestLogger<ExceptionHandlingMiddleware>();
        var middleware = CreateMiddleware(
            _ => throw new CrestPreconditionRequiredException("Book", "book-456"),
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
