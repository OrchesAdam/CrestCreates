using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using CrestCreates.CapabilityEndpoint.TrimmingFixture;
using CrestCreates.DynamicApi;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using FluentAssertions;

namespace CrestCreates.CapabilityEndpoint.TrimmingFixture.Tests;

public class TrimmingFixtureTests : IClassFixture<TrimmingFixtureTestFactory>
{
    private readonly HttpClient _client;

    public TrimmingFixtureTests(TrimmingFixtureTestFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task ProcessGreetingEndpoint_PostBody_ReturnsSuccess()
    {
        // ProcessGreetingAsync maps to POST by convention ("Process" prefix → POST).
        // This is the key trimming-safe body binding test — the full chain:
        //   HTTP POST → generated binding → CapabilityEndpointJsonTypeInfoResolver.Resolve<GreetingRequest>()
        //   → CapabilityEndpointBodyReader.ReadCompatibilityBodyAsync()
        //   → generated invoker → GreetingAppService.ProcessGreetingAsync()
        //   → result contract → DynamicApiResponse envelope
        var response = await _client.PostAsJsonAsync(
            "/api/greeting/process-greeting",
            new GreetingRequest { Name = "World" },
            ApplicationApiJsonContext.Default.GreetingRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Hello, World!",
            "POST body binding should deserialize GreetingRequest and invoke the service method");
    }

    [Fact]
    public async Task ListEndpoint_ReturnsSuccess()
    {
        var response = await _client.GetAsync("/api/greeting/list-greetings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void JsonTypeInfo_ResolvesFromApplicationOptions()
    {
        // Verify that the application's JsonSerializerContext provides
        // JsonTypeInfo for the body types used by generated binding code.
        // This is the core trimming-safety validation — no stubs, no mocks.
        var jsonTypeInfo = ApplicationApiJsonContext.Default.GreetingRequest;
        jsonTypeInfo.Should().NotBeNull(
            "STJ source generator should produce JsonTypeInfo for GreetingRequest");

        var responseTypeInfo = ApplicationApiJsonContext.Default.GreetingResponse;
        responseTypeInfo.Should().NotBeNull(
            "STJ source generator should produce JsonTypeInfo for GreetingResponse");
    }
}

public class TrimmingFixtureTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var projectDir = GetProjectDirectory();
        builder.UseContentRoot(projectDir);
        builder.UseEnvironment("Testing");
    }

    private static string GetProjectDirectory()
    {
        // Walk up from test assembly location to find the host project directory.
        // Test bin: .../TrimmingFixture.Tests/bin/Debug/net10.0/
        // Host:     .../TrimmingFixture/
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var dir = Path.GetDirectoryName(assemblyLocation)!;
        for (int i = 0; i < 5; i++)
        {
            var candidate = Path.Combine(dir, "CrestCreates.CapabilityEndpoint.TrimmingFixture");
            if (Directory.Exists(candidate))
                return candidate;
            var parent = Path.GetDirectoryName(dir);
            if (parent is null) break;
            dir = parent;
        }
        throw new DirectoryNotFoundException(
            $"Could not find TrimmingFixture host project directory from {assemblyLocation}");
    }
}
