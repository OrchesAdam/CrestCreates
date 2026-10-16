using CrestCreates.Capability;
using CrestCreates.DynamicApi;
using CrestCreates.Generated.CrestCreates_GeneratedCrud_AotFixture_Feature;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.GeneratedCrud.AotFixture.Tests;

public class Review150DefaultHostTests
{
    [Fact]
    public async Task Review150_default_jit_json_options_and_generated_activation_must_map()
    {
        Assert.True(System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault);
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCapabilityRuntime();
        builder.Services.AddCrestCompatibilityProjection();
        builder.Services.AddGeneratedCrud();
        await using var app = builder.Build();
        app.MapCrestCapabilityEndpoints();
    }
}
