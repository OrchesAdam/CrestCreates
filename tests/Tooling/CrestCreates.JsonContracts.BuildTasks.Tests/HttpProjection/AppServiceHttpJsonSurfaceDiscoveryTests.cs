using System.Text.Json;
using System.Text.Json.Nodes;
using CrestCreates.JsonContracts.BuildTasks.HttpProjection;
using CrestCreates.JsonContracts.BuildTasks.Model;
using CrestCreates.JsonContracts.BuildTasks.Semantic;
using CrestCreates.JsonContracts.BuildTasks.Tests.Infrastructure;
using CrestCreates.Tooling.HttpProjection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CrestCreates.JsonContracts.BuildTasks.Tests.HttpProjection;

/// <summary>
/// Issue #129 S2.2-B: the JSON stage rebuilds the projection roots from symbols, reconciles them with
/// the precompile outputs manifest, and fails closed on a missing root or a manifest that no longer
/// describes the compiled surface.
/// </summary>
public sealed class AppServiceHttpJsonSurfaceDiscoveryTests : JsonContractCompilationTestBase
{
    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using CrestCreates.Domain.Shared.Attributes;

        namespace Shop.Contracts
        {
            public sealed class WidgetDto { public Guid Id { get; set; } }
            public sealed class CreateWidgetDto { public string Name { get; set; } = ""; }

            public interface IWidgetAppService
            {
                [AppServiceHttpAction(Verb = HttpProjectionVerb.Post, Route = "", Kind = HttpProjectionOperationKind.Command, Body = HttpProjectionBodyPolicy.Required)]
                Task<WidgetDto> CreateAsync(CreateWidgetDto input, CancellationToken cancellationToken = default);

                [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "{id}", Kind = HttpProjectionOperationKind.Query)]
                Task<WidgetDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

                [AppServiceHttpAction(Verb = HttpProjectionVerb.Put, Route = "{id}", Kind = HttpProjectionOperationKind.Command, Body = HttpProjectionBodyPolicy.Required, RequiresConcurrencyToken = true)]
                Task<WidgetDto> UpdateAsync(Guid id, CreateWidgetDto input, string? expectedStamp, CancellationToken cancellationToken = default);

                [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "all", Kind = HttpProjectionOperationKind.Query)]
                Task<System.Collections.Generic.List<WidgetDto>> GetAllAsync(CancellationToken cancellationToken = default);
            }

            public interface IOrderAppService
            {
                [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "overdue", Kind = HttpProjectionOperationKind.Query)]
                Task<System.Collections.Generic.IReadOnlyList<WidgetDto>> GetOverdueAsync(CancellationToken cancellationToken = default);
            }
        }

        namespace Shop.Http
        {
            [CapabilityCompatibilityProjection(ContractType = typeof(Shop.Contracts.IWidgetAppService), RoutePrefix = "api/widgets")]
            public static class WidgetHttpProjection { }

            [CapabilityCompatibilityProjection(ContractType = typeof(Shop.Contracts.IOrderAppService), RoutePrefix = "api/orders")]
            public static class OrderHttpProjection { }
        }
        """;

    [Fact]
    public void Roots_are_rebuilt_from_symbols_and_match_the_precompile_manifest()
    {
        var (manifestJson, bundle) = Precompile();
        var compilation = CreateCompilation("Shop.Http", new[]
        {
            ("Declaration.cs", Source),
            ("CrestCreates.AppServiceHttp.g.cs", bundle),
        });

        var discovery = new AppServiceHttpJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation, "Shop.Http", manifestJson);

        discovery.Diagnostics.Should().BeEmpty();
        var names = roots.Select(root => root.FullMetadataName).ToList();
        names.Should().Contain("global::Shop.Contracts.CreateWidgetDto");
        names.Should().Contain("global::Shop.Contracts.WidgetDto");
        names.Should().Contain("global::System.Collections.Generic.List<global::Shop.Contracts.WidgetDto>");
        names.Should().Contain("global::System.Collections.Generic.IReadOnlyList<global::Shop.Contracts.WidgetDto>");
        names.Should().Contain("global::CrestCreates.DynamicApi.DynamicApiResponse");
        names.Should().Contain("global::CrestCreates.DynamicApi.DynamicApiResponse<global::Shop.Contracts.WidgetDto>");
        names.Should().Contain("global::CrestCreates.DynamicApi.DynamicApiResponse<global::System.Collections.Generic.List<global::Shop.Contracts.WidgetDto>>");
        names.Should().Contain(name => name.Contains("__Update_CompatibilityInput"));
        roots.Should().OnlyContain(root =>
            root.Provenance.Origins.Single().SourceKind == JsonContractRootSourceKind.AppServiceHttpSurface);
    }

    [Fact]
    public void Generated_context_carries_the_camelCase_projection_context_and_every_root()
    {
        var (manifestJson, bundle) = Precompile();
        var engine = new JsonContractGenerationEngine();
        var result = engine.Generate(
            "Shop.Http",
            new[] { ("Declaration.cs", Source), ("CrestCreates.AppServiceHttp.g.cs", bundle) },
            GetDefaultReferences(),
            "latest",
            Array.Empty<string>(),
            "enable",
            false,
            "Internal",
            crudOutputsManifestJson: null,
            appServiceHttpOutputsManifestJson: manifestJson);

        result.HasErrors.Should().BeFalse(string.Join(" | ", result.Diagnostics.Select(d => d.Id + ": " + d.Message)));
        result.GeneratedSource.Should().Contain("class HttpProjectionJsonContext_Shop_002EHttp");
        result.GeneratedSource.Should().Contain("JsonKnownNamingPolicy.CamelCase");
        result.GeneratedSource.Should().Contain("typeof(global::CrestCreates.DynamicApi.DynamicApiResponse<global::Shop.Contracts.WidgetDto>)");
        result.GeneratedSource.Should().Contain("typeof(global::System.Collections.Generic.List<global::Shop.Contracts.WidgetDto>)");
        result.GeneratedSource.Should().Contain("typeof(global::Shop.Contracts.CreateWidgetDto)");
    }

    [Fact]
    public void Missing_result_envelope_reference_is_a_closure_error()
    {
        var references = GetDefaultReferences()
            .Where(path => !Path.GetFileName(path).Equals("CrestCreates.DynamicApi.dll", StringComparison.Ordinal))
            .ToList();
        var precompile = new AppServiceHttpPrecompileEngine().Run(
            "Shop.Http",
            new[] { ("Declaration.cs", Source) },
            references,
            "latest",
            Array.Empty<string>(),
            "enable",
            allowUnsafeBlocks: false);
        precompile.Succeeded.Should().BeTrue();

        var bundle = string.Join("\n", precompile.Artifacts.Select(artifact => artifact.Text));
        var compilation = CreateCompilation(
            "Shop.Http",
            new[] { ("Declaration.cs", Source), ("CrestCreates.AppServiceHttp.g.cs", bundle) },
            references);

        var discovery = new AppServiceHttpJsonSurfaceDiscovery();
        discovery.Discover(compilation, "Shop.Http", JsonSerializer.Serialize(precompile.Manifest));

        discovery.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == AppServiceHttpDiagnosticCodes.JsonClosureIncomplete
            && diagnostic.Message.Contains("DynamicApiResponse", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("removed-entry")]
    [InlineData("fake-root")]
    [InlineData("missing-root")]
    public void Manifest_state_that_does_not_describe_the_compiled_surface_fails_closed(string mutation)
    {
        var (manifestJson, bundle) = Precompile();
        var document = JsonNode.Parse(manifestJson)!.AsObject();
        var entries = document["Entries"]!.AsArray();
        switch (mutation)
        {
            case "hash":
                entries[0]!["ContentHash"] = "sha256:" + new string('0', 64);
                break;
            case "removed-entry":
                entries.RemoveAt(entries.Count - 1);
                break;
            case "fake-root":
                entries[0]!["RequiredJsonRootTypeNames"]!.AsArray().Add("global::Shop.Contracts.FakeDto");
                break;
            case "missing-root":
                entries[0]!["RequiredJsonRootTypeNames"]!.AsArray().RemoveAt(0);
                break;
        }

        var compilation = CreateCompilation("Shop.Http", new[]
        {
            ("Declaration.cs", Source),
            ("CrestCreates.AppServiceHttp.g.cs", bundle),
        });
        var discovery = new AppServiceHttpJsonSurfaceDiscovery();
        discovery.Discover(compilation, "Shop.Http", document.ToJsonString());

        discovery.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == AppServiceHttpDiagnosticCodes.TransportManifestState
            && diagnostic.Severity == JsonContractDiagnosticSeverity.Error,
            "a manifest that no longer matches the producer must not close a half-invented surface");
    }

    [Fact]
    public void Projection_without_a_precompile_manifest_is_not_a_completed_surface()
    {
        var compilation = CreateCompilation("Shop.Http", new[] { ("Declaration.cs", Source) });
        var discovery = new AppServiceHttpJsonSurfaceDiscovery();
        discovery.Discover(compilation, "Shop.Http", outputsManifestJson: null);

        discovery.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == AppServiceHttpDiagnosticCodes.TransportManifestState);
    }

    [Fact]
    public void Two_anchors_for_the_same_contract_are_an_owner_conflict()
    {
        var source = Source.Replace(
            "public static class OrderHttpProjection { }",
            "public static class OrderHttpProjection { }\n\n"
            + "    [CapabilityCompatibilityProjection(ContractType = typeof(Shop.Contracts.IWidgetAppService), RoutePrefix = \"api/duplicate\")]\n"
            + "    public static class DuplicateWidgetHttpProjection { }");
        var compilation = CreateCompilation("Shop.Http", new[] { ("Declaration.cs", source) });
        var discovery = new AppServiceHttpJsonSurfaceDiscovery();
        discovery.Discover(compilation, "Shop.Http", outputsManifestJson: "{}");

        discovery.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == AppServiceHttpDiagnosticCodes.OwnerRouteActionConflict);
    }

    private (string ManifestJson, string Bundle) Precompile()
    {
        var result = new AppServiceHttpPrecompileEngine().Run(
            "Shop.Http",
            new[] { ("Declaration.cs", Source) },
            GetDefaultReferences(),
            "latest",
            Array.Empty<string>(),
            "enable",
            allowUnsafeBlocks: false);
        result.Succeeded.Should().BeTrue(string.Join(" | ", result.Diagnostics.Select(d => d.Id + ": " + d.Message)));
        var bundle = string.Join("\n", result.Artifacts.Select(artifact =>
            "// ---- artifact: " + artifact.FileName + " ----\n" + artifact.Text));
        return (JsonSerializer.Serialize(result.Manifest), bundle);
    }
}
