using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using SaaSHelpdesk.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace SaaSHelpdesk.Tests.Authorization;

/// <summary>
/// Verifies the /health endpoint returns expected healthy status with the
/// public module health projection after application startup.
/// </summary>
public class HealthEndpointTests : IClassFixture<HelpdeskWebApplicationFactory>
{
    private readonly HelpdeskWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public HealthEndpointTests(HelpdeskWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    /// <summary>
    /// The /health endpoint must return HTTP 200 with status "Healthy"
    /// and include a healthy "modules" check with its stable public code.
    /// Module counts and failure details stay internal to the health check.
    /// </summary>
    [Fact]
    public async Task HealthEndpoint_Should_Return_Healthy_With_Module_Checks()
    {
        // Trigger full app startup via the factory
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        // Log the response body for diagnostics
        var body = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"/health response: {body}");

        // Assert HTTP 200
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"expected 200 OK from /health, got {response.StatusCode}. Body: {body}");

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        // Assert root status is Healthy
        root.TryGetProperty("status", out var statusProp).Should().BeTrue("response must contain 'status'");
        statusProp.GetString().Should().Be("Healthy", "health check must report Healthy");

        // Assert checks array exists
        root.TryGetProperty("checks", out var checksProp).Should().BeTrue("response must contain 'checks' array");

        // Find the "modules" check entry
        JsonElement? modulesCheck = null;
        foreach (var check in checksProp.EnumerateArray())
        {
            if (check.TryGetProperty("name", out var nameProp) && nameProp.GetString() == "modules")
            {
                modulesCheck = check;
                break;
            }
        }

        modulesCheck.Should().NotBeNull("checks must contain a 'modules' entry");

        // Assert modules check is healthy
        modulesCheck!.Value.TryGetProperty("status", out var moduleStatus).Should().BeTrue();
        moduleStatus.GetString().Should().Be("Healthy", "modules check must be Healthy");

        // Assert the stable public code and the sanitized projection
        modulesCheck.Value.TryGetProperty("code", out var code).Should().BeTrue("modules check must have a stable code");
        code.GetString().Should().Be("HEALTH_CHECK_HEALTHY");

        foreach (var sensitiveMember in new[] { "description", "exception", "data" })
        {
            if (modulesCheck.Value.TryGetProperty(sensitiveMember, out var value))
                value.ValueKind.Should().Be(JsonValueKind.Null, $"{sensitiveMember} must not be publicly exposed");
        }
    }
}