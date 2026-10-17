using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Capability.Abstractions;
using CrestCreates.CompatibilityProjection.E2E.WidgetFeature.Contracts;
using CrestCreates.DynamicApi;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace CrestCreates.CompatibilityProjection.E2E.WidgetFeature.Generated;

// #128 S3-7 (J/H/Q, JIT leg): host wiring for the hand-written CRUD-shaped ExplicitHost surface.
// #128 S3-5 shape: the contribution's source-generated context is combined into this host's final
// Http.Json.JsonOptions AFTER whatever resolver the host configured itself, so the host's explicit
// resolvers keep winning for the types they know while the declared roots are answered by the
// contributed context — and the map-time verification (§7.2, CapabilityEndpointRegistryBootstrapper)
// checks exactly that the first answering resolver for every declared root is this context.
//
// The generated-code fixture with real toolchain output is S4's scope (GeneratedCrud.AotFixture):
// there the CrestCreatesCrudJsonSerializerContext, AddGeneratedCrud and the emitted per-assembly
// activation do this wiring for real. Nothing in this file substitutes for that evidence.

/// <summary>
/// The fixture assembly's single source-generated wire context (the hand-written stand-in for the
/// toolchain-emitted <c>CrestCreatesCrudJsonSerializerContext</c>): the DTO/envelope/result roots the
/// contribution declares as required, plus the roots this assembly's other wire surfaces need. The
/// legacy compatibility greeting endpoints also resolve their metadata from here, because this host
/// deliberately runs without the default reflection resolver (see Program.cs).
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WidgetDto))]
[JsonSerializable(typeof(CreateWidgetDto))]
[JsonSerializable(typeof(UpdateWidgetDto))]
[JsonSerializable(typeof(WidgetListRequestDto))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(PagedResultDto<WidgetDto>))]
[JsonSerializable(typeof(WidgetAppService_Update_CompatibilityInput))]
[JsonSerializable(typeof(WidgetAppService_Delete_CompatibilityInput))]
[JsonSerializable(typeof(DynamicApiResponse<WidgetDto>))]
[JsonSerializable(typeof(DynamicApiResponse<PagedResultDto<WidgetDto>>))]
[JsonSerializable(typeof(DynamicApiResponse))]
// Legacy compatibility projection (GreetingAppService) wire roots. This host removes the default
// reflection resolver, so the compat contracts must be answered by the same source-generated
// context; the real toolchain fixture (S4) exercises the emitted per-concern contexts instead.
[JsonSerializable(typeof(GreetingRequest))]
[JsonSerializable(typeof(GreetingResponse))]
[JsonSerializable(typeof(List<GreetingResponse>))]
[JsonSerializable(typeof(DynamicApiResponse<GreetingResponse>))]
[JsonSerializable(typeof(DynamicApiResponse<List<GreetingResponse>>))]
internal partial class WidgetCrudJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Hand-written equivalent of the emitted <c>GeneratedCrudJsonOptionsConfigurator</c>
/// (CrudArtifactEmitter.EmitHostActivation): appends the contribution's source-generated context to
/// the host's resolver chain AFTER any resolver the host already configured, and installs it alone
/// when the chain is empty. Registered via TryAddEnumerable by the generated activation.
/// </summary>
internal sealed class WidgetJsonOptionsConfigurator : IConfigureOptions<JsonOptions>
{
    public void Configure(JsonOptions options)
    {
        var generatedContext = WidgetCrudJsonContext.Default;
        var existing = options.SerializerOptions.TypeInfoResolver;
        options.SerializerOptions.TypeInfoResolver = existing is null
            ? generatedContext
            : JsonTypeInfoResolver.Combine(existing, generatedContext);
    }
}

/// <summary>
/// Host bootstrap bridge #1: the Capability pipeline resolves handler invokers through the composed
/// <see cref="CapabilityHandlerResolver"/> (built from <see cref="ICapabilityHandlerModule"/>
/// registrations — the same composition model as the generated <c>GeneratedCapabilityHandlerModule</c>
/// and the legacy static module), while an ExplicitHost contribution holds its invokers in its
/// <c>Handlers</c> list. This module replays the contribution's own invokers into the host-owned
/// resolver, so dispatch stays host-scoped and never touches a process-global registry. The emitted
/// <c>AddGeneratedCrud</c> currently registers the contribution, the scoped AppService and the JSON
/// configurator only; the generated fixture (S4) must exercise whatever handler wiring the toolchain
/// emits next, and this hand-written module is deliberately the only stand-in for it.
/// </summary>
internal sealed class WidgetGeneratedCrudHandlerModule : ICapabilityHandlerModule
{
    internal static WidgetGeneratedCrudHandlerModule Instance { get; } = new();

    private WidgetGeneratedCrudHandlerModule()
    {
    }

    public string Id => "widget-feature-generated-crud";

    public void Apply(CapabilityHandlerResolver resolver)
    {
        foreach (var handler in new WidgetGeneratedCrudContribution().Handlers)
        {
            resolver.Register(handler.CapabilityId, handler.Invoker);
        }
    }
}

