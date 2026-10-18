using System.Linq;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using CrestCreates.Tooling.HttpProjection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.AppServiceCompatibilityGenerator;

/// <summary>
/// Issue #129 S2.2: the shared emitter turns the frozen model into ordinary Compile artifacts that
/// register through an explicit host call only. The compile probe uses the real runtime assemblies, so
/// the artifact shapes are verified by the compiler rather than by string matching alone.
/// </summary>
public sealed class AppServiceHttpProjectionEmitterTests
{
    private const string ContractSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using CrestCreates.Domain.Shared.Attributes;

        namespace Shop.Contracts
        {
            public sealed class WidgetDto { public Guid Id { get; set; } public string Name { get; set; } = ""; }
            public sealed class CreateWidgetDto { public string Name { get; set; } = ""; }

            public interface IWidgetAppService
            {
                [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "{id}", Kind = HttpProjectionOperationKind.Query, Permissions = new[] { "Widget.Get" })]
                Task<WidgetDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

                [AppServiceHttpAction(Verb = HttpProjectionVerb.Post, Route = "", Kind = HttpProjectionOperationKind.Command, Body = HttpProjectionBodyPolicy.Required, Permissions = new[] { "Widget.Create" })]
                Task<WidgetDto> CreateAsync(CreateWidgetDto input, CancellationToken cancellationToken = default);

                [AppServiceHttpAction(Verb = HttpProjectionVerb.Put, Route = "{id}", Kind = HttpProjectionOperationKind.Command, Body = HttpProjectionBodyPolicy.Required, Permissions = new[] { "Widget.Update" })]
                Task<WidgetDto> UpdateAsync(Guid id, CreateWidgetDto input, CancellationToken cancellationToken = default);

                [AppServiceHttpAction(Verb = HttpProjectionVerb.Delete, Route = "{id}", Kind = HttpProjectionOperationKind.Command, Permissions = new[] { "Widget.Delete" }, RequiresConcurrencyToken = true)]
                Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
            }
        }

        namespace Shop.Http
        {
            [CapabilityCompatibilityProjection(
                ContractType = typeof(Shop.Contracts.IWidgetAppService),
                RoutePrefix = "api/widgets")]
            public static class WidgetHttpProjection { }
        }
        """;

    private const string JsonContextStub = """
        namespace Generated.Tests
        {
            public static class HttpJsonContextStub
            {
                public static System.Text.Json.Serialization.JsonSerializerContext Default = null!;
            }
        }
        """;

    [Fact]
    public void Emitted_artifacts_are_ordinary_compile_and_register_only_through_the_host_call()
    {
        var (model, options, artifacts) = Emit();

        artifacts.Select(a => a.Source).Should().NotContain(source =>
            source.Contains("ModuleInitializer") || source.Contains("DescriptorProviderRegistry")
            || source.Contains("CompatibilityBodyReader") || source.Contains("DynamicApiGeneratedRuntime"),
            "the hand-written projection must not fall back to the legacy static chain");

        var activation = artifacts.Single(a => a.FileName.Contains("Activation")).Source;
        activation.Should().Contain(AppServiceHttpProjectionModel.ActivationMethodName);
        activation.Should().Contain("AddCrestCapabilityEndpointContribution");
        activation.Should().Contain("ICapabilityHandlerModule");
        activation.Should().Contain("IConfigureOptions<global::Microsoft.AspNetCore.Http.Json.JsonOptions>");

        var contribution = artifacts.Single(a => a.FileName.Contains("Contribution")).Source;
        contribution.Should().Contain("CapabilityEndpointContribution");
        contribution.Should().Contain("\"Widget.Get\"");
        contribution.Should().Contain("\"Widget.Update\"");
        contribution.Should().Contain("CapabilityEndpointJsonTypeInfoResolver.Require");
        contribution.Should().Contain("CapabilityEndpointBodyReader.ReadNativeBodyAsync");
        contribution.Should().Contain("CompatibilityHttpResultMapper.WrapGetResult");
        contribution.Should().Contain("\"/api/widgets/{id}\"");
        contribution.Should().Contain("RequiredIdentityIds");
        contribution.Should().Contain(options.JsonContextFullName + ".Default");

        var envelopes = artifacts.Single(a => a.FileName.Contains("Envelopes")).Source;
        envelopes.Should().Contain("CrestCreates.Generated");
        envelopes.Should().Contain("public global::System.Guid Id { get; init; }");
        envelopes.Should().Contain("public global::Shop.Contracts.CreateWidgetDto Input { get; init; }");
    }

    [Fact]
    public void Manifest_separates_declared_and_referenced_types()
    {
        var (model, options, _) = Emit();
        var entry = AppServiceHttpProjectionEmitter.BuildManifestEntry(model, options);

        entry.OwnerAssemblyName.Should().Be(model.HttpOwnerAssemblyName);
        entry.ContractAssemblyName.Should().Be(model.ContractAssemblyName);
        entry.ArtifactHintName.Should().Contain("GeneratedAppServiceHttpContribution_");
        entry.DeclaredTypeFullNames.Should().Contain(name => name.Contains("GeneratedAppServiceHttpContribution_"));
        entry.DeclaredTypeFullNames.Should().Contain(name => name.EndsWith("_CompatibilityInput"));
        entry.DeclaredTypeFullNames.Should().NotContain(model.ContractFullName,
            "the contract itself is referenced, not declared by this artifact");
        entry.DeclaredTypeFullNames.Should().NotContain(name => name.StartsWith(model.ContractNamespace + ".", System.StringComparison.Ordinal),
            "contract DTOs are referenced, not declared by the HTTP artifacts");
        entry.ReferencedTypeFullNames.Should().BeEmpty(
            "the contract and DTOs live in the same compilation here, so they are declared, not referenced");
    }

    [Fact]
    public void Emitted_artifacts_compile_against_the_real_runtime_assemblies()
    {
        var (model, options, artifacts) = Emit();
        var compilation = SourceGeneratorTestHelper.CreateAnalysisCompilation(
            "Shop.Http",
            new[] { ContractSource, JsonContextStub }
                .Concat(artifacts.Select(a => a.Source))
                .ToArray(),
            additionalReferences: new[]
            {
                "CrestCreates.Metadata",
                "CrestCreates.Metadata.Abstractions",
                "CrestCreates.Capability.Abstractions",
                "CrestCreates.DynamicApi.Abstractions",
                "CrestCreates.Schema.Abstractions",
                "CrestCreates.Metadata.Abstractions.CanonicalHashing",
                "CrestCreates.Accountability.Abstractions",
            },
            additionalMetadataReferences: new[]
            {
                MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Options.IConfigureOptions<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Http.Json.JsonOptions).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Text.Json.JsonSerializer).Assembly.Location),
            });

        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();
        errors.Should().BeEmpty("the emitted artifacts must compile as ordinary source");
    }

    private static (AppServiceHttpProjectionModel Model, AppServiceHttpEmissionOptions Options, System.Collections.Generic.IReadOnlyList<AppServiceHttpArtifact> Artifacts) Emit()
    {
        var compilation = SourceGeneratorTestHelper.CreateAnalysisCompilation("Shop.Http", new[] { ContractSource });
        var anchor = compilation.GetTypeByMetadataName("Shop.Http.WidgetHttpProjection")!;
        var result = AppServiceHttpProjectionModelBuilder.Build(compilation, anchor);
        result.HasErrors.Should().BeFalse(string.Join(" | ", result.Diagnostics.Select(d => d.ToDisplayString())));
        var options = AppServiceHttpEmissionOptions.ForContract(result.Model!.ContractFullName) with
        {
            JsonContextFullName = "global::Generated.Tests.HttpJsonContextStub",
        };
        var artifacts = AppServiceHttpProjectionEmitter.Emit(result.Model, options);
        artifacts.Should().HaveCount(3);
        return (result.Model, options, artifacts);
    }
}
