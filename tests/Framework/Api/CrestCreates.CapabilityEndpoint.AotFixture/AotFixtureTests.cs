using System.Net;
using System.Reflection;
using System.Text.Json;
using CrestCreates.CapabilityEndpoint.AotFixture;
using CrestCreates.DynamicApi;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using FluentAssertions;

namespace CrestCreates.CapabilityEndpoint.AotFixture.Tests;

public class AotFixtureTests : IClassFixture<AotFixtureTestFactory>
{
    private readonly HttpClient _client;

    public AotFixtureTests(AotFixtureTestFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task GreetEndpoint_ReturnsSuccess()
    {
        // GreetAsync is mapped as GET by convention analyzer (verb prefix "Greet" → GET)
        var response = await _client.GetAsync("/api/greeting/greet?Name=World");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListEndpoint_ReturnsSuccess()
    {
        var response = await _client.GetAsync("/api/greeting/list-greetings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void JsonTypeInfo_ResolvesSuccessfully()
    {
        // Verify that the application's JsonSerializerContext provides
        // JsonTypeInfo for the body types used by generated binding code.
        // This is the core AOT-safety validation — no stubs, no mocks.
        var jsonTypeInfo = ApplicationApiJsonContext.Default.GreetingRequest;
        jsonTypeInfo.Should().NotBeNull(
            "STJ source generator should produce JsonTypeInfo for GreetingRequest");

        var responseTypeInfo = ApplicationApiJsonContext.Default.GreetingResponse;
        responseTypeInfo.Should().NotBeNull(
            "STJ source generator should produce JsonTypeInfo for GreetingResponse");
    }
}

public class AotFixtureTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var projectDir = Path.GetDirectoryName(
            Path.GetDirectoryName(
                Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location)!))!;
        builder.UseContentRoot(projectDir);
        builder.UseEnvironment("Testing");
    }
}
