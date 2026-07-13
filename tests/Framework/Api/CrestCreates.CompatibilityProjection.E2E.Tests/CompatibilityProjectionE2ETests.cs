using System.Net;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
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
        // Set content root to the directory containing the test project
        var projectDir = Path.GetDirectoryName(
            Path.GetDirectoryName(
                Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location)!))!;
        builder.UseContentRoot(projectDir);
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
        // The TryGetQueryString pattern in generated bindings gracefully handles missing optional params.
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
}
