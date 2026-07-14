using System.Net;
using System.Reflection;
using System.Text.Json;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace CrestCreates.CompatibilityProjection.E2E;

/// <summary>
/// Custom WebApplicationFactory that sets the content root to the test project directory,
/// required because the test project is in a deeply nested directory structure and the
/// default content root auto-detection fails.
/// </summary>
public sealed class CompatibilityProjectionE2ETestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var projectDir = Path.GetDirectoryName(
            Path.GetDirectoryName(
                Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location)!))!;
        builder.UseContentRoot(projectDir);
    }
}

/// <summary>
/// Factory that injects an authorization-failure middleware into the pipeline
/// for testing failure result mapping.
/// </summary>
public sealed class AuthorizationFailureE2ETestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var projectDir = Path.GetDirectoryName(
            Path.GetDirectoryName(
                Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location)!))!;
        builder.UseContentRoot(projectDir);

        builder.ConfigureServices(services =>
        {
            // Register the rate-limit failure middleware in DI
            services.AddSingleton<TestRateLimitFailureMiddleware>();
            // Replace pipeline with one that always returns RATE_LIMIT_EXCEEDED
            services.AddSingleton(new CapabilityPipelineBuilder()
                .Use<TestRateLimitFailureMiddleware>());
        });
    }
}

/// <summary>
/// Middleware that always returns RATE_LIMIT_EXCEEDED failure, used to test
/// that compatibility result contracts do not swallow pipeline failures.
/// Uses RATE_LIMIT_EXCEEDED instead of UNAUTHORIZED because Results.Forbid()
/// requires authentication middleware which is not available in minimal test setup.
/// </summary>
public sealed class TestRateLimitFailureMiddleware : ICapabilityPipelineMiddleware
{
    public Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        return Task.FromResult(CapabilityExecutionResult.Failure(
            "RATE_LIMIT_EXCEEDED", "Rate limit exceeded for testing.", TimeSpan.FromMilliseconds(1)));
    }
}

public class CompatibilityProjectionE2ETests : IClassFixture<CompatibilityProjectionE2ETestFactory>
{
    private readonly HttpClient _client;

    public CompatibilityProjectionE2ETests(CompatibilityProjectionE2ETestFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    [Fact]
    public async Task GreetAsync_Get_WithQueryParam_Returns200_WithWrappedJson()
    {
        // Act — GreetAsync: GreetingRequest is a query object, Name binds from ?Name=Test
        // Result contract wraps output in DynamicApiResponse<T> envelope.
        var response = await _client.GetAsync("/api/greeting/greet?Name=Test");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("code").GetInt32().Should().Be(200);
        doc.RootElement.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("data").GetProperty("message").GetString().Should().Be("Hello, Test!");
    }

    [Fact]
    public async Task GetGreetingAsync_Get_WithRouteParam_Returns200_WithWrappedJson()
    {
        // Act — GetGreetingAsync: name binds from route {name}
        var response = await _client.GetAsync("/api/greeting/greeting/Test");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("code").GetInt32().Should().Be(200);
        doc.RootElement.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("data").GetProperty("message").GetString().Should().Be("Hello, Test!");
    }

    [Fact]
    public async Task ListGreetingsAsync_Get_NoParams_Returns200_WithWrappedJsonArray()
    {
        // Act — ListGreetingsAsync: no non-CancellationToken parameters.
        // Verifies the generator correctly handles no-param methods (no CS1001).
        var response = await _client.GetAsync("/api/greeting/list-greetings");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("code").GetInt32().Should().Be(200);
        doc.RootElement.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
        var data = doc.RootElement.GetProperty("data");
        data.ValueKind.Should().Be(JsonValueKind.Array);
        data.GetArrayLength().Should().Be(2);
        data[0].GetProperty("message").GetString().Should().Be("Hello, World!");
        data[1].GetProperty("message").GetString().Should().Be("Hello, CrestCreates!");
    }

    [Fact]
    public async Task DeleteGreetingAsync_Delete_WithQueryParam_Returns200_WithWrappedVoid()
    {
        // Act — DeleteGreetingAsync: name binds from query string ?name=Test
        // Void return → WrapVoidResult() returns 200 with DynamicApiResponse (no data).
        var response = await _client.DeleteAsync("/api/greeting/delete-greeting?name=Test");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("code").GetInt32().Should().Be(200);
        doc.RootElement.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
        // Void returns should not have a data field
        doc.RootElement.TryGetProperty("data", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetGreetingAsync_Get_MissingRequiredRouteParam_Returns404()
    {
        // Act — calling without the required route value should return 404
        var response = await _client.GetAsync("/api/greeting/greeting/");

        // Assert — route mismatch results in 404
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GreetAsync_Get_MissingQueryParam_Returns200_WithEmptyName()
    {
        // Act — GreetingRequest.Name defaults to empty string when query param is missing.
        var response = await _client.GetAsync("/api/greeting/greet");

        // Assert — returns 200 with greeting containing empty name, wrapped in envelope
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("code").GetInt32().Should().Be(200);
        doc.RootElement.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("data").GetProperty("message").GetString().Should().Be("Hello, !");
    }

    [Fact]
    public async Task NonExistentRoute_Returns404()
    {
        // Act
        var response = await _client.GetAsync("/api/greeting/non-existent");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PipelineMiddleware_IsExecuted()
    {
        // Arrange — reset marker before test
        TestMarkerMiddleware.Reset();

        // Act
        var response = await _client.GetAsync("/api/greeting/list-greetings");

        // Assert — middleware was executed
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        TestMarkerMiddleware.LastInvocationSeen.Should().BeTrue(
            "TestMarkerMiddleware should be executed in the pipeline");
    }
}

/// <summary>
/// E2E tests that verify pipeline failure results are NOT swallowed by
/// compatibility result contracts. Uses a factory that injects an
/// authorization-failure middleware.
/// </summary>
public class AuthorizationFailureE2ETests : IClassFixture<AuthorizationFailureE2ETestFactory>
{
    private readonly HttpClient _client;

    public AuthorizationFailureE2ETests(AuthorizationFailureE2ETestFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    [Fact]
    public async Task CompatibilityEndpoint_PipelineFailure_NotSwallowed()
    {
        // Act — any endpoint should return 429 when pipeline returns RATE_LIMIT_EXCEEDED
        var response = await _client.GetAsync("/api/greeting/list-greetings");

        // Assert — failure must NOT be swallowed as 200 OK with success envelope
        response.StatusCode.Should().Be((HttpStatusCode)429,
            "RATE_LIMIT_EXCEEDED pipeline failure must produce 429, not 200 OK with DynamicApiResponse envelope");
    }
}
