using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CrestCreates.DynamicApi.Tests;

#pragma warning disable CC1001 // RefValidation: descriptor not registered — tests use mock registries

/// <summary>
/// Probe DTOs: <see cref="CrudWireProbeDto"/> exists ONLY in the generated-equivalent context, and
/// <see cref="HostOnlyProbeDto"/> ONLY in the host's own resolver, so a passing resolution can be
/// attributed to exactly one resolver.
/// </summary>
internal sealed class CrudWireProbeDto
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class HostOnlyProbeDto
{
    public string Name { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CrudWireProbeDto))]
[JsonSerializable(typeof(DynamicApiResponse<CrudWireProbeDto>))]
[JsonSerializable(typeof(DynamicApiResponse))]
internal partial class CrudWireProbeJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HostOnlyProbeDto))]
internal partial class HostOnlyProbeJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Hand-written equivalent of the generated <c>GeneratedCrudJsonOptionsConfigurator</c> (see
/// <c>CrudArtifactEmitter.EmitHostActivation</c>): appends the generated context to the host's
/// resolver chain AFTER any resolver the host already configured, and installs it alone when the
/// chain is empty. The generated activation registers exactly this shape from <c>AddGeneratedCrud</c>.
/// </summary>
internal sealed class WireProbeJsonOptionsConfigurator : IConfigureOptions<JsonOptions>
{
    public void Configure(JsonOptions options)
    {
        var generatedContext = CrudWireProbeJsonContext.Default;
        var existing = options.SerializerOptions.TypeInfoResolver;
        options.SerializerOptions.TypeInfoResolver = existing is null
            ? generatedContext
            : JsonTypeInfoResolver.Combine(existing, generatedContext);
    }
}

/// <summary>
/// A custom host resolver that answers only for <see cref="HostOnlyProbeDto"/> (delegating to the
/// host's own source-generated context) and records that it was consulted.
/// </summary>
internal sealed class TrackingHostResolver : IJsonTypeInfoResolver
{
    private readonly IJsonTypeInfoResolver _inner = HostOnlyProbeJsonContext.Default;

    public int HostTypeQueries { get; private set; }

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        if (type == typeof(HostOnlyProbeDto))
        {
            HostTypeQueries++;
        }

        return _inner.GetTypeInfo(type, options);
    }
}

/// <summary>
/// #128 S3 step C: the host wiring and the map-time verification of the CRUD JSON contract
/// (§7.1/§7.2). The generated contribution carries the assembly's source-generated context and the
/// roots it requires; the generated configurator appends that context to the host's final
/// Http.Json.JsonOptions after any resolver the host configured itself; the map-time verification
/// requires every required root to be answered by the contributed context, so a host that only has
/// <c>DefaultJsonTypeInfoResolver</c> fails deterministically even though reflection could serialize
/// the type. Contributions are hand-built here; the generated wiring shape is asserted by the
/// generator baselines.
/// </summary>
[Collection("DynamicApiStaticState")]
public sealed class CapabilityEndpointJsonHostWiringTests : IDisposable
{
    public CapabilityEndpointJsonHostWiringTests()
    {
        ResetStaticState();
    }

    public void Dispose()
    {
        ResetStaticState();
    }

    private static void ResetStaticState()
    {
        CapabilityEndpointBindingRegistry.Reset();
        CapabilityEndpointResultContractRegistration.Reset();
        CapabilityHandlerResolverProvider.Reset();
        CapabilityEndpointJsonContractRegistry.Reset();
        ClearDescriptorProviderRegistry();
    }

    private static void ClearDescriptorProviderRegistry()
    {
        var field = typeof(DescriptorProviderRegistry).GetField("_providers",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var providers = (ConcurrentBag<object>)field!.GetValue(null)!;
        providers.Clear();
    }

    [Fact]
    public void Generated_context_registration_must_make_the_final_options_answer_a_generated_only_root()
    {
        // WITH the generated-equivalent registration: the host's final options answer the root that
        // only the generated context knows, and they answer it from that context — never from a
        // reflection resolver.
        var wired = ResolveSerializerOptions(registerGeneratedContext: true);

        wired.GetTypeInfo(typeof(CrudWireProbeDto)).Should().NotBeNull(
            "the generated wiring must expose the CRUD contract through the host's own JsonOptions");
        FirstAnsweringResolver(wired, typeof(CrudWireProbeDto)).Should().BeSameAs(
            CrudWireProbeJsonContext.Default,
            "the metadata must be answered by the generated context, so the contract cannot have come from reflection");

        // WITHOUT the registration the same root is not resolvable at all: the host resolver is
        // source-generated only, so nothing can have answered it — this pair is the discriminating
        // evidence that the wiring, not a reflection resolver, provides the contract.
        var unwired = ResolveSerializerOptions(registerGeneratedContext: false);

        var act = () => unwired.GetTypeInfo(typeof(CrudWireProbeDto));

        act.Should().Throw<NotSupportedException>(
            "without the generated registration the CRUD contract is absent from the host's final options");
    }

    [Fact]
    public void Host_resolver_must_keep_winning_for_the_types_it_already_knows()
    {
        var hostResolver = new TrackingHostResolver();
        var services = new ServiceCollection();
        services.Configure<JsonOptions>(options =>
            options.SerializerOptions.TypeInfoResolver = hostResolver);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<JsonOptions>, WireProbeJsonOptionsConfigurator>());
        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<JsonOptions>>()
            .Value.SerializerOptions;

        var metadata = options.GetTypeInfo(typeof(HostOnlyProbeDto));

        metadata.Should().NotBeNull(
            "the host's own resolver configuration must keep working after the generated activation");
        hostResolver.HostTypeQueries.Should().BeGreaterThan(0,
            "the host's explicit resolver must still be the one consulted");
        FirstAnsweringResolver(options, typeof(HostOnlyProbeDto)).Should().BeSameAs(hostResolver,
            "the generated context is appended after the host's resolver and never before it");

        // The host's precedence does not hide the generated contract: a generated-only root is still
        // answered by the generated context.
        FirstAnsweringResolver(options, typeof(CrudWireProbeDto)).Should().BeSameAs(
            CrudWireProbeJsonContext.Default);
    }

    [Fact]
    public void Required_json_root_missing_from_the_contribution_context_must_fail_map_with_owner_action_and_type()
    {
        // The contribution declares the root but its own declared context does not carry it, so the
        // first half of §7.2 fails no matter what the host's options contain.
        var capability = CreateCapability();
        var contribution = new MissingJsonRootContribution
        {
            OwnerAssemblyName = "LibraryFeature.Generated",
            EndpointDescriptors = [CreateEndpoint("ep-json-missing", route: "/api/v1/json-missing")],
            Bindings = [CreateBinding("ep-json-missing")],
            Handlers = [CreateHandler("test-cap")],
            ResultMappers = [CreateResultBinding("ep-json-missing", "json-missing")],
            JsonContext = HostOnlyProbeJsonContext.Default,
            RequiredJsonRootTypes =
            [
                new CapabilityEndpointJsonRootType(typeof(CrudWireProbeDto), "Create", "Body")
            ]
        };

        var (_, endpoints) = BuildEndpointHost(capability, contribution, registerGeneratedContext: true);

        var act = () => endpoints.MapCrestCapabilityEndpoints();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*LibraryFeature.Generated*")
            .WithMessage("*Create*")
            .WithMessage($"*{typeof(CrudWireProbeDto).FullName}*")
            .WithMessage("*does not carry metadata*");
        endpoints.DataSources.Should().BeEmpty("the whole map fails before any route is registered");

        // A contribution that declares required roots but no context at all fails the same way.
        var noContextContribution = new NoJsonContextContribution
        {
            OwnerAssemblyName = "LibraryFeature.Generated",
            EndpointDescriptors = [CreateEndpoint("ep-json-missing", route: "/api/v1/json-missing")],
            Bindings = [CreateBinding("ep-json-missing")],
            Handlers = [CreateHandler("test-cap")],
            ResultMappers = [CreateResultBinding("ep-json-missing", "json-missing")],
            RequiredJsonRootTypes =
            [
                new CapabilityEndpointJsonRootType(typeof(CrudWireProbeDto), "Search", "Payload")
            ]
        };

        var (_, endpointsWithoutContext) = BuildEndpointHost(
            capability, noContextContribution, registerGeneratedContext: true);

        var actWithoutContext = () => endpointsWithoutContext.MapCrestCapabilityEndpoints();

        actWithoutContext.Should().Throw<InvalidOperationException>()
            .WithMessage("*declares no source-generated JsonContext*");
        endpointsWithoutContext.DataSources.Should().BeEmpty();
    }

    [Fact]
    public void Host_with_only_the_default_reflection_resolver_must_fail_map_even_though_reflection_can_serialize()
    {
        var capability = CreateCapability();

        // The contributed context DOES carry the root: the metadata is genuinely available from the
        // declared source-generated contract. Only the host's final options fail to answer it from
        // there, which is exactly the "do not trust reflection" clause of §7.2.
        static CapabilityEndpointContribution CreateContribution() => new ReflectionShadowedContribution
        {
            OwnerAssemblyName = "LibraryFeature.Generated",
            EndpointDescriptors = [CreateEndpoint("ep-reflection", route: "/api/v1/reflection")],
            Bindings = [CreateBinding("ep-reflection")],
            Handlers = [CreateHandler("test-cap")],
            ResultMappers = [CreateResultBinding("ep-reflection", "reflection")],
            JsonContext = CrudWireProbeJsonContext.Default,
            RequiredJsonRootTypes =
            [
                new CapabilityEndpointJsonRootType(typeof(CrudWireProbeDto), "Create", "Body")
            ]
        };

        // (1) Host configures DefaultJsonTypeInfoResolver AND the generated wiring appended the context
        //     after it: reflection answers first, so the generated contract is shadowed and must fail.
        var (hostWithWiring, endpointsWithWiring) = BuildEndpointHost(
            capability,
            CreateContribution(),
            configureJson: options => options.SerializerOptions.TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            registerGeneratedContext: true);

        var wiredOptions = hostWithWiring.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        wiredOptions.GetTypeInfo(typeof(CrudWireProbeDto)).Should().NotBeNull(
            "the reflection resolver really can serialize the type, so the check must not be satisfiable by it");
        FirstAnsweringResolver(wiredOptions, typeof(CrudWireProbeDto)).Should().NotBeSameAs(
            CrudWireProbeJsonContext.Default,
            "the reflection resolver shadows the generated context — the map must reject that");

        var actWithWiring = () => endpointsWithWiring.MapCrestCapabilityEndpoints();

        actWithWiring.Should().Throw<InvalidOperationException>()
            .WithMessage("*LibraryFeature.Generated*")
            .WithMessage("*Create*")
            .WithMessage($"*{typeof(CrudWireProbeDto).FullName}*")
            .WithMessage("*DefaultJsonTypeInfoResolver*")
            .WithMessage("*never resolved through a reflection resolver*");
        endpointsWithWiring.DataSources.Should().BeEmpty();

        // (2) A host whose options contain ONLY DefaultJsonTypeInfoResolver (the generated wiring was
        //     never activated) also fails, even though reflection could serialize the type.
        var (hostWithoutWiring, endpointsWithoutWiring) = BuildEndpointHost(
            capability,
            CreateContribution(),
            configureJson: options => options.SerializerOptions.TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            registerGeneratedContext: false);

        var unwiredOptions = hostWithoutWiring.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        unwiredOptions.GetTypeInfo(typeof(CrudWireProbeDto)).Should().NotBeNull(
            "reflection-only options do resolve the type, which is exactly why the map must not accept them");

        var actWithoutWiring = () => endpointsWithoutWiring.MapCrestCapabilityEndpoints();

        actWithoutWiring.Should().Throw<InvalidOperationException>()
            .WithMessage("*DefaultJsonTypeInfoResolver*")
            .WithMessage("*never resolved through a reflection resolver*");
        endpointsWithoutWiring.DataSources.Should().BeEmpty();
    }

    [Fact]
    public void Fully_declared_json_contract_must_map_when_the_generated_context_is_wired()
    {
        // Positive control: the same activation shape with every required root answered by the
        // contributed context maps normally and serves the typed accessor the generated binders use.
        var capability = CreateCapability();
        var contribution = new WiredContractContribution
        {
            OwnerAssemblyName = "LibraryFeature.Generated",
            EndpointDescriptors = [CreateEndpoint("ep-wired", route: "/api/v1/wired")],
            Bindings = [CreateBinding("ep-wired")],
            Handlers = [CreateHandler("test-cap")],
            ResultMappers = [CreateResultBinding("ep-wired", "wired")],
            RequiredIdentityIds = [new CapabilityEndpointRequiredIdentity("ep-wired", "", "Create", "Product", 1)],
            JsonContext = CrudWireProbeJsonContext.Default,
            RequiredJsonRootTypes =
            [
                new CapabilityEndpointJsonRootType(typeof(CrudWireProbeDto), "Create", "Body"),
                new CapabilityEndpointJsonRootType(
                    typeof(DynamicApiResponse<CrudWireProbeDto>), "Create", "ResultEnvelope"),
                new CapabilityEndpointJsonRootType(typeof(DynamicApiResponse), "Create", "VoidEnvelope")
            ]
        };

        // DynamicApiResponse is always resolvable through the envelope root of the probe context.
        var (sp, endpoints) = BuildEndpointHost(capability, contribution, registerGeneratedContext: true);

        endpoints.MapCrestCapabilityEndpoints();

        GetEndpoints(endpoints).Should().ContainSingle()
            .Which.RoutePattern.RawText.Should().Be("/api/v1/wired");
        CapabilityEndpointJsonTypeInfoResolver.Require<CrudWireProbeDto>(sp).Should().NotBeNull(
            "the generated binder accessor must resolve through the host's final options");
    }

    // ─────────────────────────────
    //  Host/wiring helpers
    // ─────────────────────────────

    /// <summary>
    /// Builds the final Http.Json.JsonOptions the way a real host does: <c>Configure&lt;JsonOptions&gt;</c>
    /// plus, when requested, the generated-equivalent configurator registration. The host's own
    /// resolver is a source-generated context, mirroring an AOT-first host.
    /// </summary>
    [Fact]
    public void Review150_two_explicit_contributions_with_common_void_root_must_map()
    {
        var capA = CreateCapability("feature-a.cap");
        var capB = CreateCapability("feature-b.cap");
        var epA = CreateEndpoint("feature-a.endpoint", capA.Id, "/feature-a");
        var epB = CreateEndpoint("feature-b.endpoint", capB.Id, "/feature-b");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(CreateCapabilityRegistryMock(capA, capB).Object);
        services.AddSingleton(CreateDispatcherMock().Object);
        services.AddCrestCapabilityEndpoints();
        services.Configure<JsonOptions>(options => options.SerializerOptions.TypeInfoResolver =
            JsonTypeInfoResolver.Combine(CrudWireProbeJsonContext.Default, Review150SecondContext.Default));
        services.AddCrestCapabilityEndpointContribution(new Review150FirstContribution
        {
            OwnerAssemblyName = "FeatureA",
            CapabilityDescriptors = new[] { capA }, EndpointDescriptors = new[] { epA },
            Bindings = new[] { CreateBinding(epA.Id) },
            Handlers = new[] { CreateHandler(capA.Id) },
            ResultMappers = new[] { CreateResultBinding(epA.Id, "a") },
            JsonContext = CrudWireProbeJsonContext.Default,
            RequiredJsonRootTypes = new[] { new CapabilityEndpointJsonRootType(typeof(DynamicApiResponse), "Delete", "VoidEnvelope") },
        });
        services.AddCrestCapabilityEndpointContribution(new Review150SecondContribution
        {
            OwnerAssemblyName = "FeatureB",
            CapabilityDescriptors = new[] { capB }, EndpointDescriptors = new[] { epB },
            Bindings = new[] { CreateBinding(epB.Id) },
            Handlers = new[] { CreateHandler(capB.Id) },
            ResultMappers = new[] { CreateResultBinding(epB.Id, "b") },
            JsonContext = Review150SecondContext.Default,
            RequiredJsonRootTypes = new[] { new CapabilityEndpointJsonRootType(typeof(DynamicApiResponse), "Delete", "VoidEnvelope") },
        });
        using var sp = services.BuildServiceProvider();
        var endpoints = new DefaultEndpointRouteBuilder(sp);
        endpoints.MapCrestCapabilityEndpoints();
        GetEndpoints(endpoints).Should().HaveCount(2);
    }

    private sealed class Review150CapabilityProvider(CapabilityDescriptor descriptor) : IDescriptorProvider<CapabilityDescriptor>
    {
        public IReadOnlyList<CapabilityDescriptor> GetDescriptors() => new[] { descriptor };
    }
    private sealed class Review150FirstContribution : CapabilityEndpointContribution;
    private sealed class Review150SecondContribution : CapabilityEndpointContribution;

    [Fact]
    public void Review150_prebuilt_capability_with_changed_permissions_must_reject()
    {
        var expected = new CapabilityDescriptor { Id = "owned.cap", Name = "Expected", Version = 1,
            State = DescriptorState.Active, Permissions = new[] { "Product.Create" } };
        var actual = new CapabilityDescriptor { Id = expected.Id, Name = expected.Name, Version = expected.Version, State = DescriptorState.Active, Permissions = Array.Empty<string>() };
        var endpoint = CreateEndpoint("owned.endpoint", expected.Id);
        var services = new ServiceCollection();
        services.AddLogging();
        var registry = new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>(Array.Empty<IRegistryValidator<CapabilityDescriptor>>()));
        registry.Build(new[] { new Review150CapabilityProvider(actual) });
        registry.State.Should().Be(RegistryState.Built);
        services.AddSingleton<ICapabilityRegistry>(registry);
        services.AddSingleton(CreateDispatcherMock().Object);
        services.AddCrestCapabilityEndpoints();
        services.AddCrestCapabilityEndpointContribution(new Review150FirstContribution
        {
            OwnerAssemblyName = "FeatureA", CapabilityDescriptors = new[] { expected },
            EndpointDescriptors = new[] { endpoint }, Bindings = new[] { CreateBinding(endpoint.Id) },
            Handlers = new[] { CreateHandler(expected.Id) },
            ResultMappers = new[] { CreateResultBinding(endpoint.Id, "a") },
        });
        using var sp = services.BuildServiceProvider();
        var endpoints = new DefaultEndpointRouteBuilder(sp);
        Action map = () => endpoints.MapCrestCapabilityEndpoints();
        map.Should().Throw<InvalidOperationException>("a prebuilt descriptor with different authority is not the contributed contract");
    }

    // PR150-R1-CRUD128-03 acceptance: the equivalence rule accepts a fully equivalent prebuilt
    // entry and rejects version / state / projection differences with deterministic diagnostics.

    [Fact]
    public void CrudR1_prebuilt_registry_with_the_equivalent_capability_must_map()
    {
        var authoritative = CreateOwnedCapability();
        var contributed = CreateOwnedCapability();
        var (_, endpoints) = BuildPrebuiltRegistryHost(authoritative, contributed);

        Action map = () => endpoints.MapCrestCapabilityEndpoints();

        map.Should().NotThrow("a value-equivalent authority is the contributed execution contract");
        GetEndpoints(endpoints).Should().HaveCount(1);
    }

    [Fact]
    public void CrudR1_prebuilt_registry_with_a_different_version_must_reject()
    {
        var authoritative = CreateOwnedCapability(version: 2);
        var contributed = CreateOwnedCapability();
        var (_, endpoints) = BuildPrebuiltRegistryHost(authoritative, contributed);

        Action map = () => endpoints.MapCrestCapabilityEndpoints();

        map.Should().Throw<InvalidOperationException>()
            .WithMessage("*owned.cap*")
            .WithMessage("*exact version is not present*")
            .WithMessage("*v2*");
    }

    [Fact]
    public void CrudR2_exact_v1_with_a_non_equivalent_contract_must_reject()
    {
        // The exact version exists, but its execution contract differs — v1/v2 coexistence must not
        // degrade into accepting a non-equivalent v1.
        var authoritative = CreateOwnedCapability(permissions: Array.Empty<string>());
        var contributed = CreateOwnedCapability();
        var (_, endpoints) = BuildPrebuiltRegistryHost(authoritative, contributed);

        Action map = () => endpoints.MapCrestCapabilityEndpoints();

        map.Should().Throw<InvalidOperationException>()
            .WithMessage("*owned.cap*")
            .WithMessage("*permissions*");
    }

    [Fact]
    public void CrudR2_different_risk_level_must_reject_in_both_directions()
    {
        // The reviewer probe covers registry-Low vs contributed-Medium; this is the reverse direction.
        var authoritative = CreateOwnedCapability(risk: CapabilityRiskLevel.High);
        var contributed = CreateOwnedCapability();
        var (_, endpoints) = BuildPrebuiltRegistryHost(authoritative, contributed);

        Action map = () => endpoints.MapCrestCapabilityEndpoints();

        map.Should().Throw<InvalidOperationException>()
            .WithMessage("*owned.cap*")
            .WithMessage("*risk level*High*Medium*");
    }

    [Fact]
    public void CrudR1_prebuilt_registry_with_a_different_state_must_reject()
    {
        var authoritative = CreateOwnedCapability(state: DescriptorState.Deprecated);
        var contributed = CreateOwnedCapability();
        var (_, endpoints) = BuildPrebuiltRegistryHost(authoritative, contributed);

        Action map = () => endpoints.MapCrestCapabilityEndpoints();

        map.Should().Throw<InvalidOperationException>()
            .WithMessage("*owned.cap*")
            .WithMessage("*state*Deprecated*Active*");
    }

    [Fact]
    public void CrudR1_prebuilt_registry_with_a_different_projection_must_reject()
    {
        var authoritative = CreateOwnedCapability(projection: CapabilityProjectionKind.AppServiceCompatibility);
        var contributed = CreateOwnedCapability();
        var (_, endpoints) = BuildPrebuiltRegistryHost(authoritative, contributed);

        Action map = () => endpoints.MapCrestCapabilityEndpoints();

        map.Should().Throw<InvalidOperationException>()
            .WithMessage("*owned.cap*")
            .WithMessage("*projection*AppServiceCompatibility*Native*");
    }

    private static CapabilityDescriptor CreateOwnedCapability(
        int version = 1,
        DescriptorState state = DescriptorState.Active,
        CapabilityProjectionKind projection = CapabilityProjectionKind.Native,
        IReadOnlyList<string>? permissions = null,
        CapabilityRiskLevel risk = CapabilityRiskLevel.Medium)
        => new()
        {
            Id = "owned.cap",
            Name = "Expected",
            Version = version,
            State = state,
            ProjectionKind = projection,
            Permissions = permissions ?? new[] { "Product.Create" },
            RiskLevel = risk,
        };

    /// <summary>Prebuilds a real, frozen registry from the authoritative descriptor and activates the
    /// contributed descriptor as this host's feature contract, mirroring the reviewed defect setup.</summary>
    private static (IServiceProvider ServiceProvider, IEndpointRouteBuilder Endpoints) BuildPrebuiltRegistryHost(
        CapabilityDescriptor authoritative,
        CapabilityDescriptor contributed)
    {
        var endpoint = CreateEndpoint("owned.endpoint", contributed.Id);
        var services = new ServiceCollection();
        services.AddLogging();
        var registry = new CapabilityRegistry(
            new RegistryValidationEngine<CapabilityDescriptor>(Array.Empty<IRegistryValidator<CapabilityDescriptor>>()));
        registry.Build(new IDescriptorProvider<CapabilityDescriptor>[] { new Review150CapabilityProvider(authoritative) });
        services.AddSingleton<ICapabilityRegistry>(registry);
        services.AddSingleton(CreateDispatcherMock().Object);
        services.AddCrestCapabilityEndpoints();
        services.AddCrestCapabilityEndpointContribution(new Review150FirstContribution
        {
            OwnerAssemblyName = "FeatureA",
            CapabilityDescriptors = new[] { contributed },
            EndpointDescriptors = new[] { endpoint },
            Bindings = new[] { CreateBinding(endpoint.Id) },
            Handlers = new[] { CreateHandler(contributed.Id) },
            ResultMappers = new[] { CreateResultBinding(endpoint.Id, "a") },
        });

        var sp = services.BuildServiceProvider();
        return (sp, new DefaultEndpointRouteBuilder(sp));
    }

    [Fact]
    public void Review150R2_matching_exact_v1_must_map_when_v2_also_exists()
    {
        var expected = CreateOwnedCapability(version: 1);
        var newer = CreateOwnedCapability(version: 2);
        var registry = new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>(Array.Empty<IRegistryValidator<CapabilityDescriptor>>()));
        registry.Build(new[] { new Review150CapabilityProvider(expected), new Review150CapabilityProvider(newer) });
        registry.GetByVersion(expected.Id, 1).Should().BeSameAs(expected);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICapabilityRegistry>(registry);
        services.AddSingleton(CreateDispatcherMock().Object);
        services.AddCrestCapabilityEndpoints();
        var endpoint = CreateEndpoint("owned.endpoint", expected.Id);
        services.AddCrestCapabilityEndpointContribution(new Review150FirstContribution
        {
            OwnerAssemblyName = "FeatureA", CapabilityDescriptors = new[] { expected },
            EndpointDescriptors = new[] { endpoint }, Bindings = new[] { CreateBinding(endpoint.Id) },
            Handlers = new[] { CreateHandler(expected.Id) },
            ResultMappers = new[] { CreateResultBinding(endpoint.Id, "a") },
        });
        using var sp = services.BuildServiceProvider();
        var endpoints = new DefaultEndpointRouteBuilder(sp);
        endpoints.MapCrestCapabilityEndpoints();
        GetEndpoints(endpoints).Should().HaveCount(1);
        registry.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public void Review150R2_different_risk_must_reject_the_frozen_authority()
    {
        var expected = CreateOwnedCapability();
        var actual = new CapabilityDescriptor {
            Id = expected.Id, Name = expected.Name, Version = expected.Version,
            Permissions = expected.Permissions, ProjectionKind = expected.ProjectionKind,
            RiskLevel = CapabilityRiskLevel.Low,
        };
        expected.RiskLevel.Should().Be(CapabilityRiskLevel.Medium);
        var (_, endpoints) = BuildPrebuiltRegistryHost(actual, expected);
        Action map = () => endpoints.MapCrestCapabilityEndpoints();
        map.Should().Throw<InvalidOperationException>("risk is an execution/governance contract field");
    }

    private static JsonSerializerOptions ResolveSerializerOptions(bool registerGeneratedContext)
    {
        var services = new ServiceCollection();
        services.Configure<JsonOptions>(options =>
            options.SerializerOptions.TypeInfoResolver = HostOnlyProbeJsonContext.Default);
        if (registerGeneratedContext)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<
                IConfigureOptions<JsonOptions>, WireProbeJsonOptionsConfigurator>());
        }

        return services.BuildServiceProvider()
            .GetRequiredService<IOptions<JsonOptions>>()
            .Value.SerializerOptions;
    }

    /// <summary>
    /// Reproduces the ordered resolution of the final options — the first resolver in the chain that
    /// answers wins — so the tests can attribute an answer to a specific resolver.
    /// </summary>
    private static IJsonTypeInfoResolver? FirstAnsweringResolver(JsonSerializerOptions options, Type type)
    {
        foreach (var resolver in options.TypeInfoResolverChain)
        {
            if (resolver.GetTypeInfo(type, options) is not null)
            {
                return resolver;
            }
        }

        return null;
    }

    private static (IServiceProvider ServiceProvider, IEndpointRouteBuilder Endpoints) BuildEndpointHost(
        CapabilityDescriptor capability,
        CapabilityEndpointContribution contribution,
        Action<JsonOptions>? configureJson = null,
        bool registerGeneratedContext = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(CreateCapabilityRegistryMock(capability).Object);
        services.AddSingleton(CreateDispatcherMock().Object);
        services.AddCrestCapabilityEndpoints();
        services.Configure<JsonOptions>(options =>
        {
            if (configureJson is not null)
            {
                configureJson(options);
            }
            else
            {
                options.SerializerOptions.TypeInfoResolver = HostOnlyProbeJsonContext.Default;
            }
        });
        if (registerGeneratedContext)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<
                IConfigureOptions<JsonOptions>, WireProbeJsonOptionsConfigurator>());
        }

        services.AddCrestCapabilityEndpointContribution(contribution);

        var sp = services.BuildServiceProvider();
        return (sp, new DefaultEndpointRouteBuilder(sp));
    }

    private static CapabilityDescriptor CreateCapability(string id = "test-cap")
        => new()
        {
            Id = id,
            Name = $"Capability {id}",
            Version = 1,
            State = DescriptorState.Active
        };

    private static CapabilityEndpointDescriptor CreateEndpoint(
        string id,
        string capabilityId = "test-cap",
        string route = "/api/v1/json-contract")
        => new()
        {
            Id = id,
            Name = id,
            Version = 1,
            State = DescriptorState.Active,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>(capabilityId, 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = route,
            OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 200 }
        };

    private static CapabilityEndpointBindingContract CreateBinding(string endpointId)
        => new(endpointId, 1, (_, _) => ValueTask.FromResult<object?>(new { payload = "json-contract" }));

    private static CapabilityEndpointResultBinding CreateResultBinding(string endpointId, string marker)
        => new(endpointId, 1, (ctx, _) => Results.Ok(new { code = 200, marker, data = ctx.Output }));

    private static CapabilityEndpointHandlerBinding CreateHandler(string capabilityId)
        => new(capabilityId, new TestHandlerInvoker());

    private static Mock<ICapabilityRegistry> CreateCapabilityRegistryMock(
        params CapabilityDescriptor[] capabilities)
    {
        var mock = new Mock<ICapabilityRegistry>();
        foreach (var capability in capabilities)
        {
            mock.Setup(r => r.GetByVersion(capability.Id, capability.Version)).Returns(capability);
            mock.Setup(r => r.GetById(capability.Id)).Returns(capability);
        }

        mock.Setup(r => r.GetAll()).Returns(capabilities);
        return mock;
    }

    private static Mock<ICapabilityDispatcher> CreateDispatcherMock()
    {
        var mock = new Mock<ICapabilityDispatcher>();
        mock.Setup(d => d.DispatchAsync(
                It.IsAny<CapabilityDescriptor>(),
                It.IsAny<InvocationSource>(),
                It.IsAny<object?>(),
                It.IsAny<Action<CapabilityExecutionContext>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CapabilityExecutionResult.Success(
                new { name = "dispatched" }, TimeSpan.FromMilliseconds(1)));
        return mock;
    }

    private static IReadOnlyList<RouteEndpoint> GetEndpoints(IEndpointRouteBuilder endpoints)
        => endpoints.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToList();

    private sealed class TestHandlerInvoker : ICapabilityHandlerInvoker
    {
        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
            => Task.FromResult<object?>(new { handled = true });
    }

    // Hand-built contributions simulating the generated per-assembly contribution classes.
    // TryAddEnumerable de-duplicates by implementation type, so each host-local contribution
    // needs its own concrete type.
    private sealed class MissingJsonRootContribution : CapabilityEndpointContribution;

    private sealed class NoJsonContextContribution : CapabilityEndpointContribution;

    private sealed class ReflectionShadowedContribution : CapabilityEndpointContribution;

    private sealed class WiredContractContribution : CapabilityEndpointContribution;

    /// <summary>
    /// Minimal IEndpointRouteBuilder implementation for testing.
    /// Does not provide real HTTP routing — only captures data sources.
    /// </summary>
    private sealed class DefaultEndpointRouteBuilder : IEndpointRouteBuilder
    {
        public DefaultEndpointRouteBuilder(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
            DataSources = new List<EndpointDataSource>();
        }

        public IServiceProvider ServiceProvider { get; }

        public ICollection<EndpointDataSource> DataSources { get; }

        public IApplicationBuilder CreateApplicationBuilder()
        {
            throw new NotSupportedException();
        }
    }
}

#pragma warning restore CC1001

[JsonSerializable(typeof(DynamicApiResponse))]
internal partial class Review150SecondContext : JsonSerializerContext { }
