using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Capability;
using CrestCreates.DynamicApi;
using CrestCreates.Generated.CrestCreates_GeneratedCrud_AotFixture_Feature;
using CrestCreates.GeneratedCrud.AotFixture.Feature.Widgets.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.GeneratedCrud.AotFixture.Tests;

/// <summary>Host-only DTO served by the host's own resolver (kept out of the generated CRUD context).</summary>
internal sealed class CrudR1HostOnlyDto
{
    public string Name { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CrudR1HostOnlyDto))]
internal partial class CrudR1HostOnlyContext : JsonSerializerContext;

/// <summary>
/// A host-explicit resolver that is deliberately NOT a <see cref="DefaultJsonTypeInfoResolver"/>
/// instance (it only wraps one), so the generated ordering logic treats it as a host resolver: it
/// answers every type first, which is exactly the explicit conflict the map-time verification must
/// reject deterministically instead of silently overriding or accepting it.
/// </summary>
internal sealed class CrudR1ExplicitConflictResolver : IJsonTypeInfoResolver
{
    private readonly IJsonTypeInfoResolver _inner = new DefaultJsonTypeInfoResolver();

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        => _inner.GetTypeInfo(type, options);
}

/// <summary>
/// PR150-R1-CRUD128-02 acceptance: the default JIT host composition. The generated context must be
/// reachable without clearing the chain, the host's own resolvers keep their types and priority, and
/// an explicitly conflicting resolver fails the map deterministically with a named diagnostic.
/// </summary>
public class CrudR1HostCompositionTests
{
    [Fact]
    public async Task Host_own_resolver_keeps_its_types_and_the_generated_context_serves_crud_roots()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddCapabilityRuntime();
        builder.Services.AddCrestCompatibilityProjection();
        builder.Services.Configure<JsonOptions>(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, CrudR1HostOnlyContext.Default));
        builder.Services.AddGeneratedCrud();

        await using var app = builder.Build();
        app.MapCrestCapabilityEndpoints();

        var options = app.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        var chain = options.TypeInfoResolverChain;

        FirstAnsweringResolver(chain, options, typeof(CrudR1HostOnlyDto))
            .Should().BeSameAs(CrudR1HostOnlyContext.Default,
                "the host's own resolver keeps serving the host's own types");

        var crudAnswer = FirstAnsweringResolver(chain, options, typeof(CreateWidgetDto));
        crudAnswer.Should().NotBeNull("the generated CRUD root must be answerable on the final options");
        crudAnswer!.GetType().FullName.Should().Contain("CrestCreatesCrudJsonSerializerContext",
            "the CRUD metadata must come from the generated source-generated context");

        var defaultIndex = IndexOfDefaultJsonResolver(chain);
        defaultIndex.Should().BeGreaterThan(chain.IndexOf(crudAnswer!),
            "the built-in reflection resolver is kept but never shadows the generated CRUD metadata");
        chain.Should().Contain(CrudR1HostOnlyContext.Default);
    }

    [Fact]
    public async Task Explicit_conflicting_resolver_must_be_rejected_deterministically()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddCapabilityRuntime();
        builder.Services.AddCrestCompatibilityProjection();
        builder.Services.Configure<JsonOptions>(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, new CrudR1ExplicitConflictResolver()));
        builder.Services.AddGeneratedCrud();

        await using var app = builder.Build();
        Action map = () => app.MapCrestCapabilityEndpoints();

        map.Should().Throw<InvalidOperationException>()
            .WithMessage("*CrudR1ExplicitConflictResolver*")
            .WithMessage("*before the contribution's JsonContext*");
    }

    [Fact]
    public async Task Review150R2_explicit_default_resolver_with_modifiers_must_not_be_silently_bypassed()
    {
        var explicitlyConfigured = new DefaultJsonTypeInfoResolver();
        var invoked = 0;
        explicitlyConfigured.Modifiers.Add(metadata =>
        {
            if (metadata.Type == typeof(CreateWidgetDto))
            {
                invoked++;
                metadata.Properties.Single(property => property.Name == "name").Name = "hostName";
            }
        });
        var controlMetadata = explicitlyConfigured.GetTypeInfo(typeof(CreateWidgetDto), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        controlMetadata.Properties.Should().Contain(property => property.Name == "hostName",
            "the explicitly configured modifier really changes this CRUD type's contract");
        invoked = 0;
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddCapabilityRuntime();
        builder.Services.AddCrestCompatibilityProjection();
        builder.Services.Configure<JsonOptions>(options => options.SerializerOptions.TypeInfoResolverChain.Insert(0, explicitlyConfigured));
        builder.Services.AddGeneratedCrud();
        await using var app = builder.Build();
        Action map = () => app.MapCrestCapabilityEndpoints();
        var failure = Record.Exception(map);
        if (failure is null)
        {
            var options = app.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
            var finalMetadata = options.GetTypeInfo(typeof(CreateWidgetDto));
            throw new Xunit.Sdk.XunitException("Map accepted an explicit conflicting resolver; modifier calls=" + invoked
                + "; final property names=" + string.Join(",", finalMetadata.Properties.Select(property => property.Name)));
        }
        failure.Should().BeOfType<InvalidOperationException>().Which.Message
            .Should().Contain(nameof(DefaultJsonTypeInfoResolver));

    }

    [Fact]
    public async Task Derived_reflection_resolver_keeps_priority_and_fails_the_map()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddCapabilityRuntime();
        builder.Services.AddCrestCompatibilityProjection();
        builder.Services.Configure<JsonOptions>(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, new CrudR2DerivedReflectionResolver()));
        builder.Services.AddGeneratedCrud();

        await using var app = builder.Build();
        Action map = () => app.MapCrestCapabilityEndpoints();

        map.Should().Throw<InvalidOperationException>()
            .WithMessage("*CrudR2DerivedReflectionResolver*")
            .WithMessage("*before the contribution's JsonContext*");
    }

    private sealed class CrudR2DerivedReflectionResolver : DefaultJsonTypeInfoResolver;

    private static IJsonTypeInfoResolver? FirstAnsweringResolver(
        IList<IJsonTypeInfoResolver> chain,
        JsonSerializerOptions options,
        Type type)
    {
        foreach (var resolver in chain)
        {
            try
            {
                if (resolver.GetTypeInfo(type, options) is not null)
                {
                    return resolver;
                }
            }
            catch (NotSupportedException)
            {
            }
        }

        return null;
    }

    private static int IndexOfDefaultJsonResolver(IList<IJsonTypeInfoResolver> chain)
    {
        for (var index = 0; index < chain.Count; index++)
        {
            if (chain[index] is DefaultJsonTypeInfoResolver)
            {
                return index;
            }
        }

        return -1;
    }
}
