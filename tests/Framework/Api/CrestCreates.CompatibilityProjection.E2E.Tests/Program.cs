using CrestCreates.Capability;
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
// AddCapabilityRuntime also registers internal types like DefaultCapabilityResolver
// that we cannot register from outside the assembly.
builder.Services.AddCapabilityRuntime();

// Remove services that have unresolvable dependencies in our minimal test setup.
// These are not needed for basic endpoint dispatch validation.
builder.Services.RemoveAll<ValidationMiddleware>();
builder.Services.RemoveAll<IBootstrapValidator>();
builder.Services.RemoveAll<IDescriptorBindingStatusContributor>();

// Replace pipeline builder with a clean one (no middleware — direct handler invocation).
// AddCapabilityRuntime registers a builder with 7 middleware types, but some of those
// middleware have unresolvable dependencies. We just want pass-through.
builder.Services.AddSingleton(new CapabilityPipelineBuilder());

// Stable hash (required by CapabilityPipeline)
builder.Services.AddDescriptorStableHash();

// Register compatibility projection (endpoint infrastructure + result contract)
builder.Services.AddCrestCompatibilityProjection();

// Register the test AppService as scoped
builder.Services.AddScoped<GreetingAppService>();

// Add logging (required by pipeline middleware)
builder.Services.AddLogging();

var app = builder.Build();

// Build capability registry from generated providers BEFORE endpoint mapping.
// The endpoint validator checks ICapabilityRegistry for referenced capabilities.
var capabilityRegistry = (CapabilityRegistry)app.Services.GetRequiredService<ICapabilityRegistry>();
var capabilityProviders = DescriptorProviderRegistry.GetProviders<CapabilityDescriptor>();
capabilityRegistry.Build(capabilityProviders);

// Map capability endpoints (includes compatibility projection endpoints)
app.MapCrestCapabilityEndpoints();

app.Run();

public partial class Program;
