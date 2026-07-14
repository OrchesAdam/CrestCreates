using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using CrestCreates.CompatibilityProjection.E2E;
using CrestCreates.DynamicApi;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using CrestCreates.Metadata.Abstractions.DescriptorBinding;
using CrestCreates.Metadata.Bootstrap;
using CrestCreates.Metadata.DescriptorCapability;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Register capability runtime (pipeline + dispatcher + resolver)
builder.Services.AddCapabilityRuntime();

// Remove services that have unresolvable dependencies in our minimal test setup.
builder.Services.RemoveAll<ValidationMiddleware>();
builder.Services.RemoveAll<IBootstrapValidator>();
builder.Services.RemoveAll<IDescriptorBindingStatusContributor>();

// Replace pipeline builder with one that includes a test marker middleware.
// This verifies that the pipeline actually executes middleware and that
// InvocationSource.Http is correctly set.
builder.Services.AddSingleton<TestMarkerMiddleware>();
builder.Services.AddSingleton(new CapabilityPipelineBuilder()
    .Use<TestMarkerMiddleware>());

// Register compatibility projection (endpoint infrastructure + result contract)
builder.Services.AddCrestCompatibilityProjection();

// Register the test AppService as scoped
builder.Services.AddScoped<GreetingAppService>();

// Add logging (required by pipeline middleware)
builder.Services.AddLogging();

var app = builder.Build();

// Build capability registry from generated providers BEFORE endpoint mapping.
var capabilityRegistry = (CapabilityRegistry)app.Services.GetRequiredService<ICapabilityRegistry>();
var capabilityProviders = DescriptorProviderRegistry.GetProviders<CapabilityDescriptor>();
capabilityRegistry.Build(capabilityProviders);

// Map capability endpoints (includes compatibility projection endpoints)
app.MapCrestCapabilityEndpoints();

app.Run();

/// <summary>
/// Test middleware that records invocation metadata for assertion in tests.
/// Simply passes through to the next delegate after recording.
/// </summary>
public sealed class TestMarkerMiddleware : ICapabilityPipelineMiddleware
{
    public static bool LastInvocationSeen { get; set; }
    public static InvocationSource? LastInvocationSource { get; set; }

    public Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        LastInvocationSeen = true;
        LastInvocationSource = context.InvocationSource;
        return next(context);
    }

    public static void Reset()
    {
        LastInvocationSeen = false;
        LastInvocationSource = null;
    }
}

public partial class Program;
