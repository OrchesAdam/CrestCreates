using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability;
using CrestCreates.Capability.Middleware;
using CrestCreates.Data.Abstractions;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.DataFilter;
using CrestCreates.DynamicApi;
using CrestCreates.Generated.CrestCreates_GeneratedCrud_AotFixture_Feature;
using CrestCreates.GeneratedCrud.AotFixture;
using CrestCreates.GeneratedCrud.AotFixture.Domain;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using CrestCreates.Metadata.Abstractions.DescriptorBinding;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// #128 S4 host: the real Web Host for the generated CRUD fixture. Every CRUD wire contract
// member (DTOs, service, mappings, typed query adapter, capability/endpoint descriptors, invokers,
// typed result envelopes and the source-generated JSON context) comes from the Feature assembly's
// toolchain output; this Program only composes the framework runtime and the memory-era DI seams.
// The Feature assembly is woven (Rougamo.Fody), so the generated [UnitOfWorkMo] write methods run
// under the unique UnitOfWork kernel with the fixture's memory provider (Step B).

var builder = WebApplication.CreateBuilder(args);

// Register capability runtime (pipeline + dispatcher + resolver) — the same composition the proven
// CapabilityEndpoint.AotFixture and CompatibilityProjection.E2E hosts run.
builder.Services.AddCapabilityRuntime();
builder.Services.AddAccountability();

// Drop only the services whose dependencies this minimal, memory-backed host deliberately does not
// carry — mirroring the proven CapabilityEndpoint.AotFixture host shape.
builder.Services.RemoveAll<ValidationMiddleware>();
builder.Services.RemoveAll<IBootstrapValidator>();
builder.Services.RemoveAll<IDescriptorBindingStatusContributor>();

// Bounded pipeline (no middleware chain) for this fixture host.
builder.Services.AddSingleton(new CapabilityPipelineBuilder());

// Endpoint infrastructure + compatibility result contract.
builder.Services.AddCrestCompatibilityProjection();

// §7.2: the wire contract must be answered by the generated CRUD JSON context, never by reflection —
// the resolver chain is cleared here, and the generated GeneratedCrudJsonOptionsConfigurator (part of
// AddGeneratedCrud below) re-adds exactly that context to the host's final Http.Json.JsonOptions.
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(
    options => options.SerializerOptions.TypeInfoResolverChain.Clear());

// The generated per-assembly activation, called exactly as an ordinary consumer would: one scoped
// AppService instance for the interface and the concrete type, the ExplicitHost contribution with
// the five CRUD endpoints, the generated handler module feeding the composed dispatcher resolver,
// and the generated JSON configurator. Nothing here is hand-written CRUD wiring.
builder.Services.AddGeneratedCrud();

// The DI seams the generated WidgetAppService injects: the memory repository and the minimal
// framework seams (permission checker, current user, data-permission filter). The repository is a
// singleton so the HTTP surface and the test host observe one store.
builder.Services.AddSingleton<MemoryWidgetRepository>();
builder.Services.AddSingleton<ICrestRepositoryBase<Widget, Guid>>(
    serviceProvider => serviceProvider.GetRequiredService<MemoryWidgetRepository>());
builder.Services.AddSingleton<IPermissionChecker, FixturePermissionChecker>();
builder.Services.AddSingleton<ICurrentUser, FixtureCurrentUser>();
builder.Services.AddSingleton<IDataPermissionFilter, IdentityDataPermissionFilter>();

// #128 S4 Step B: the unique UnitOfWork assembly entry with the fixture's memory provider binding.
// Capabilities are declared exactly as implemented (transactions yes; no explicit isolation levels;
// prompt termination and discard on abandon) — the kernel fail-closes before business code whenever
// a declared-but-unsupported capability is requested. The provider is bound under OrmProvider.EfCore
// only because that enum is a closed list of ORM families with no memory member.
builder.Services.AddUnitOfWork(OrmProvider.EfCore);
builder.Services.AddUnitOfWorkProvider(
    OrmProvider.EfCore,
    static serviceProvider => serviceProvider.GetRequiredService<MemoryWidgetUnitOfWork>(),
    supportsRequiresNew: false,
    capabilities: new UnitOfWorkProviderCapabilities
    {
        SupportsTransactions = true,
        PromptTerminationOnAbandon = true,
        DiscardUncommittedOnAbandon = true,
    });
builder.Services.AddScoped<MemoryWidgetUnitOfWork>();
builder.Services.AddSingleton<MemoryUnitOfWorkObserver>();

// #128 S4 Step B seam: lets a test make the next response write fail (dropped-connection shape),
// so the "commit vs response-write failure are independent facts" claim is provable.
builder.Services.AddSingleton<ResponseWriteFailureGate>();

// Add logging (required by pipeline middleware)
builder.Services.AddLogging();

var app = builder.Build();

// The response-write failure gate must sit before the endpoint stage; when armed it replaces the
// body stream for exactly the next request with one whose first write throws.
app.Use(async (context, next) =>
{
    if (context.RequestServices.GetRequiredService<ResponseWriteFailureGate>().TryConsumeArm())
    {
        context.Response.Body = new ThrowingResponseBodyStream();
    }

    await next(context);
});

// §7.2: no manual capability/endpoint registry build here — MapCrestCapabilityEndpoints performs the
// single authoritative aggregation (static providers + this host's ExplicitHost contributions) before
// mapping, which is what the generated AddGeneratedCrud + MapCrestCapabilityEndpoints pair promises.
app.MapCrestCapabilityEndpoints();

app.Run();

public partial class Program;
