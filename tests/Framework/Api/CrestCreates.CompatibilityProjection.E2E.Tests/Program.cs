using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using CrestCreates.CompatibilityProjection.E2E;
using CrestCreates.CompatibilityProjection.E2E.WidgetFeature;
using CrestCreates.CompatibilityProjection.E2E.WidgetFeature.Generated;
using CrestCreates.CompatibilityProjection.E2E.WidgetFeature.Services;
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
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Register capability runtime (pipeline + dispatcher + resolver)
builder.Services.AddCapabilityRuntime();
builder.Services.AddAccountability();

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
builder.Services.AddGeneratedAppServiceHttp();

// Register the test AppService as scoped
builder.Services.AddScoped<GreetingAppService>();

// #128 S3-7 (J/H/Q, JIT leg): the CRUD-shaped ExplicitHost activation of the WidgetFeature probe
// surface, following the generated AddGeneratedCrud() shape — the per-assembly contribution, one
// scoped AppService instance serving both the interface and the concrete type, and the S3-5 JSON
// configurator that appends the contribution's source-generated context to the host's resolver
// chain. The generated-code fixture with real toolchain output is S4's scope (GeneratedCrud.AotFixture).
builder.Services.AddCrestCapabilityEndpointContribution(new WidgetGeneratedCrudContribution());
builder.Services.AddSingleton<WidgetCallRecorder>();
builder.Services.TryAddScoped<WidgetAppService>();
builder.Services.TryAddScoped<IWidgetAppService>(serviceProvider =>
    serviceProvider.GetRequiredService<WidgetAppService>());

// Dispatch bridge for the contribution's own invokers: the Capability pipeline resolves handlers
// through the composed ICapabilityHandlerResolver (modules registered in DI — the same composition
// model the generator emits for capability handlers), so the hand-written module replays the
// contribution's Handlers into the host-owned resolver. The generated activation must eventually
// emit this wiring itself; see WidgetHostWiring.cs.
builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<ICapabilityHandlerModule>(WidgetGeneratedCrudHandlerModule.Instance));

// §7.2: this host must not let JsonSerializerIsReflectionEnabledByDefault behaviour answer the wire
// contract. The default reflection resolver is dropped and the contribution's source-generated
// context is combined into the final Http.Json.JsonOptions afterwards by the configurator (the
// generated GeneratedCrudJsonOptionsConfigurator shape), so the map-time verification finds the
// declared context as the first answering resolver for every required root.
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
    options.SerializerOptions.TypeInfoResolverChain.Clear());
builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<
    IConfigureOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>, WidgetJsonOptionsConfigurator>());

// Add logging (required by pipeline middleware)
builder.Services.AddLogging();

var app = builder.Build();

// The generated Greeting contribution owns its source-generated JSON context; install it before the
// map-time JSON verification examines the final resolver chain.
var greetingContexts = app.Services.GetServices<CapabilityEndpointContribution>()
    .Select(contribution => contribution.JsonContext)
    .Where(context => context is not null)
    .ToArray();
if (greetingContexts.Length > 0)
{
    var httpJsonOptions = app.Services
        .GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
        .Value.SerializerOptions;
    foreach (var context in greetingContexts.Reverse())
    {
        httpJsonOptions.TypeInfoResolverChain.Remove(context!);
        httpJsonOptions.TypeInfoResolverChain.Insert(0, context!);
    }
}

// §7.2: no manual capability-registry build here — MapCrestCapabilityEndpoints performs the single
// authoritative aggregation (static providers + this host's ExplicitHost contributions) before
// mapping, which is what the generated AddGeneratedCrud + MapCrestCapabilityEndpoints pair promises.
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
