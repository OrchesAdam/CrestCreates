using CrestCreates.JsonContracts.BuildTasks.HttpProjection;
using CrestCreates.JsonContracts.BuildTasks.Tests.Infrastructure;
using CrestCreates.Tooling.HttpProjection;
using FluentAssertions;
using Xunit;

namespace CrestCreates.JsonContracts.BuildTasks.Tests.HttpProjection;

/// <summary>
/// Issue #129 S2.2-B: the isolated precompile producer returns ordinary Compile artifacts plus a
/// per-artifact manifest, and declines declarations it does not own.
/// </summary>
public sealed class AppServiceHttpPrecompileEngineTests : JsonContractCompilationTestBase
{
    private const string TwoContracts = """
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
                [AppServiceHttpAction(Verb = HttpProjectionVerb.Post, Route = "", Kind = HttpProjectionOperationKind.Command, Body = HttpProjectionBodyPolicy.Required, Permissions = new[] { "Widget.Create" })]
                Task<WidgetDto> CreateAsync(CreateWidgetDto input, CancellationToken cancellationToken = default);

                [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "{id}", Kind = HttpProjectionOperationKind.Query, Permissions = new[] { "Widget.Get" })]
                Task<WidgetDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
            }

            public interface IOrderAppService
            {
                [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "all", Kind = HttpProjectionOperationKind.Query)]
                Task<System.Collections.Generic.List<WidgetDto>> GetAllAsync(CancellationToken cancellationToken = default);
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
    public void Two_contracts_emit_two_bundles_one_activation_and_a_per_artifact_manifest()
    {
        var result = Run(TwoContracts, "Shop.Http");

        result.Succeeded.Should().BeTrue(string.Join(" | ", result.Diagnostics.Select(d => d.Id + ": " + d.Message)));
        result.DeclarationCount.Should().Be(2);
        result.Artifacts.Should().HaveCount(3, "one bundle per contract plus one assembly activation");
        result.Artifacts.Count(a => a.FileName.Contains("Bundle_")).Should().Be(2);
        result.Artifacts.Single(a => a.FileName.Contains("Activation_")).FileName.Should()
            .Contain(AppServiceHttpWireConvention.CanonicalToken("Shop.Http"),
                "the public activation identity is per owner assembly");

        var manifest = result.Manifest!;
        manifest.SchemaVersion.Should().Be(AppServiceHttpPrecompileEngine.ArtifactSchemaVersion);
        manifest.ModelSchemaVersion.Should().Be(AppServiceHttpProjectionModel.ModelSchemaVersion);
        manifest.Entries.Should().HaveCount(3);
        manifest.Entries!.Should().OnlyContain(entry =>
            entry.OwnerAssemblyName == "Shop.Http"
            && entry.ContractAssemblyName == "Shop.Http"
            && entry.ContentHash!.StartsWith("sha256:", StringComparison.Ordinal)
            && entry.DeclaredTypeFullNames!.Length > 0,
            string.Join(" | ", manifest.Entries!.Select(entry =>
                entry.ArtifactHintName + " owner=" + entry.OwnerAssemblyName + " contract=" + entry.ContractAssemblyName
                + " hash=" + entry.ContentHash + " declared=" + string.Join(",", entry.DeclaredTypeFullNames!))));
        manifest.Entries!.Select(entry => entry.ArtifactHintName).Should().BeEquivalentTo(
            result.Artifacts.Select(artifact => artifact.FileName));
        manifest.Entries!.Where(entry => entry.ArtifactHintName!.Contains("Bundle_")).Should().OnlyContain(entry =>
            entry.ReferencedTypeFullNames!.Length == 0,
            "contract and DTOs live in the compiling assembly and are declared, not referenced");
    }

    [Fact]
    public void Legacy_static_declarations_are_not_owned_by_the_formal_stage()
    {
        var result = Run("""
            using System.Threading;
            using System.Threading.Tasks;
            using CrestCreates.Domain.Shared.Attributes;

            namespace Legacy
            {
                [CrestService]
                [CapabilityCompatibilityProjection(RoutePrefix = "api/legacy")]
                public class LegacyAppService
                {
                    public Task<string> PingAsync(CancellationToken cancellationToken = default) => Task.FromResult("ok");
                }
            }
            """, "Shop.Legacy");

        result.Succeeded.Should().BeTrue();
        result.DeclarationCount.Should().Be(0);
        result.Artifacts.Should().BeEmpty();
        result.Manifest.Should().BeNull();
        result.Diagnostics.Should().BeEmpty("declining ownership is not a diagnostic");
    }

    [Fact]
    public void Unsupported_shapes_fail_closed_before_any_artifact()
    {
        var result = Run("""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using CrestCreates.Domain.Shared.Attributes;

            namespace Bad
            {
                public interface IService
                {
                    [AppServiceHttpAction(Verb = HttpProjectionVerb.Post)]
                    Task TouchAsync(ref Guid id, CancellationToken cancellationToken = default);
                }

                [CapabilityCompatibilityProjection(ContractType = typeof(IService), RoutePrefix = "api/bad")]
                public static class BadHttpProjection { }
            }
            """, "Shop.Bad");

        result.Succeeded.Should().BeFalse();
        result.Artifacts.Should().BeEmpty("an unsupported declaration must not leave a partial surface behind");
        result.Manifest.Should().BeNull();
        result.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == AppServiceHttpDiagnosticCodes.UnsupportedDeclaration && diagnostic.IsError);
    }

    [Fact]
    public void Artifacts_are_deterministic_for_the_same_inputs()
    {
        var first = Run(TwoContracts, "Shop.Http");
        var second = Run(TwoContracts, "Shop.Http");

        first.Artifacts.Select(a => a.FileName + "\u001f" + a.Text)
            .Should().Equal(second.Artifacts.Select(a => a.FileName + "\u001f" + a.Text));
        first.Manifest!.Entries!.Select(entry => entry.ContentHash)
            .Should().Equal(second.Manifest!.Entries!.Select(entry => entry.ContentHash));
    }

    private AppServiceHttpPrecompileResult Run(string source, string assemblyName)
        => new AppServiceHttpPrecompileEngine().Run(
            assemblyName,
            new[] { ("Generated.cs", source) },
            GetDefaultReferences(),
            "latest",
            Array.Empty<string>(),
            "enable",
            allowUnsafeBlocks: false);
}
