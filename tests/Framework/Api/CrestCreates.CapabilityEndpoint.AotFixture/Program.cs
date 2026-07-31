using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using CrestCreates.CapabilityEndpoint.AotFixture;
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
builder.Services.AddAccountability();

// Remove services that have unresolvable dependencies in our minimal test setup.
builder.Services.RemoveAll<ValidationMiddleware>();
builder.Services.RemoveAll<IBootstrapValidator>();
builder.Services.RemoveAll<IDescriptorBindingStatusContributor>();

// Register a simple pass-through pipeline
builder.Services.AddSingleton(new CapabilityPipelineBuilder());

// Register compatibility projection (endpoint infrastructure + result contract)
builder.Services.AddCrestCompatibilityProjection();

// Register the test AppService as scoped
builder.Services.AddScoped<GreetingAppService>();

// Configure JSON options with the application's JsonSerializerContext.
// This is the key AOT-safe pattern: the application owns the context,
// and CapabilityEndpointJsonTypeInfoResolver resolves JsonTypeInfo from it.
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(
    options => options.SerializerOptions.TypeInfoResolverChain.Add(ApplicationApiJsonContext.Default));

// Add logging (required by pipeline middleware)
builder.Services.AddLogging();

var app = builder.Build();

// Build capability registry from generated providers BEFORE endpoint mapping.
var capabilityRegistry = (CapabilityRegistry)app.Services.GetRequiredService<ICapabilityRegistry>();
var capabilityProviders = DescriptorProviderRegistry.GetProviders<CapabilityDescriptor>();
capabilityRegistry.Build(capabilityProviders);

// Map capability endpoints (includes startup validation of JSON contracts)
app.MapCrestCapabilityEndpoints();

app.Run();

public partial class Program;
