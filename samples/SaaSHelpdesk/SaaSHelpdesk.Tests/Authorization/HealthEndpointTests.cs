using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using SaaSHelpdesk.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace SaaSHelpdesk.Tests.Authorization;

/// <summary>
/// Verifies the /health endpoint returns expected healthy status
/// with module diagnostics data after application startup.
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
    /// and include a "modules" check that reports module and phase counts.
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

        // Assert data contains module counts and phase counts
        modulesCheck.Value.TryGetProperty("data", out var data).Should().BeTrue("modules check must have data");

        data.TryGetProperty("totalModules", out var totalModules).Should().BeTrue("data must contain totalModules");
        totalModules.GetInt32().Should().BeGreaterThan(0, "there must be at least one module");

        data.TryGetProperty("failedModules", out var failedModules).Should().BeTrue("data must contain failedModules");
        failedModules.GetInt32().Should().Be(0, "no modules should have failed");

        data.TryGetProperty("totalPhases", out var totalPhases).Should().BeTrue("data must contain totalPhases");
        totalPhases.GetInt32().Should().BeGreaterThan(0, "there must be at least one phase");

        data.TryGetProperty("failedPhases", out var failedPhases).Should().BeTrue("data must contain failedPhases");
        failedPhases.GetInt32().Should().Be(0, "no phases should have failed");

        data.TryGetProperty("modules", out var modulesList).Should().BeTrue("data must contain modules list");
        modulesList.GetArrayLength().Should().BeGreaterThan(0, "modules list must not be empty");
    }
}