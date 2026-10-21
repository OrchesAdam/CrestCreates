using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using CrestCreates.CapabilityEndpoint.AotFixture;
using CrestCreates.DynamicApi;
using CrestCreates.Generated.HttpProjection;
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
builder.Services.AddGeneratedAppServiceHttp();

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

// The generated contribution owns its source-generated context; install it ahead of the
// application-owned context before the map-time JSON verification examines the final chain.
var generatedContexts = app.Services.GetServices<CapabilityEndpointContribution>()
    .Select(contribution => contribution.JsonContext)
    .Where(context => context is not null)
    .ToArray();
if (generatedContexts.Length > 0)
{
    var httpJsonOptions = app.Services
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
        .Value.SerializerOptions;
    foreach (var context in generatedContexts.Reverse())
    {
        httpJsonOptions.TypeInfoResolverChain.Remove(context!);
        httpJsonOptions.TypeInfoResolverChain.Insert(0, context!);
    }
}

// Map capability endpoints (the map is the single authoritative registry aggregation point,
// including this host's ExplicitHost contributions, followed by JSON contract validation).
app.MapCrestCapabilityEndpoints();

app.Run();

public partial class Program;
