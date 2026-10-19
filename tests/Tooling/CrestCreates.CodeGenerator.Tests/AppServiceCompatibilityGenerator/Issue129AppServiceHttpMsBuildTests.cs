using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.CodeGenerator.Tests.CrudServiceGenerator;
using CrestCreates.Tooling.HttpProjection;
using FluentAssertions;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.AppServiceCompatibilityGenerator;

/// <summary>
/// Issue #129 S2.2-B baselines at real-MSBuild level: the hand-written projection surface is
/// materialized as ordinary Compile input, JsonContracts closes its roots in the same CoreCompile, the
/// outputs manifest is reconciled, and lifecycle mutation (no-op, clean, rename, design-time, TFM/RID)
/// never reuses a stale success stamp.
/// </summary>
[Collection("CrudBuild")]
public sealed class Issue129AppServiceHttpMsBuildTests
{
    private const string ProjectTemplate = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            __TFMS__
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <OutputType>Library</OutputType>
            <JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>
            <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
            <PublishTrimmed>false</PublishTrimmed>
            <PublishAot>false</PublishAot>
            <EnableTrimAnalyzer>false</EnableTrimAnalyzer>
            <EnableWindowsTargeting Condition="'$(TargetFrameworks)' != ''">true</EnableWindowsTargeting>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="__ROOT__/src/Tooling/CrestCreates.CodeGenerator/CrestCreates.CodeGenerator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" SetTargetFramework="TargetFramework=netstandard2.0" />
            <ProjectReference Include="__ROOT__/src/Framework/Ddd/CrestCreates.Domain.Shared/CrestCreates.Domain.Shared.csproj" />
            <ProjectReference Include="__ROOT__/src/Framework/Ddd/CrestCreates.Domain/CrestCreates.Domain.csproj" />
            <ProjectReference Include="__ROOT__/src/Framework/Ddd/CrestCreates.Application.Contracts/CrestCreates.Application.Contracts.csproj" />
            <ProjectReference Include="__ROOT__/src/Framework/Api/CrestCreates.DynamicApi/CrestCreates.DynamicApi.csproj" />
            <FrameworkReference Include="Microsoft.AspNetCore.App" />
          </ItemGroup>
          __IMPORTS__
          __EXTRA__
        </Project>
        """;

    private const string DeclarationSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using CrestCreates.Domain.Shared.Attributes;

        namespace Shop.Contracts
        {
            public sealed class WidgetDto
            {
                public Guid Id { get; set; }
                public string Name { get; set; } = string.Empty;
            }

            public sealed class CreateWidgetDto
            {
                public string Name { get; set; } = string.Empty;
            }

            public interface IWidgetAppService
            {
                [AppServiceHttpAction(Verb = HttpProjectionVerb.Post, Route = "", Kind = HttpProjectionOperationKind.Command, Body = HttpProjectionBodyPolicy.Required, Permissions = new[] { "Widget.Create" })]
                Task<WidgetDto> CreateAsync(CreateWidgetDto input, CancellationToken cancellationToken = default);

                [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "{id}", Kind = HttpProjectionOperationKind.Query, Permissions = new[] { "Widget.Get" })]
                Task<WidgetDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

                [AppServiceHttpAction(Verb = HttpProjectionVerb.Put, Route = "{id}", Kind = HttpProjectionOperationKind.Command, Body = HttpProjectionBodyPolicy.Required, Permissions = new[] { "Widget.Update" }, RequiresConcurrencyToken = true)]
                Task<WidgetDto> UpdateAsync(Guid id, CreateWidgetDto input, string? expectedStamp, CancellationToken cancellationToken = default);

                [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "assign", Kind = HttpProjectionOperationKind.Command, Permissions = new[] { "Widget.Update" })]
                Task<WidgetDto> AssignAsync(Guid id, Guid agentId, CancellationToken cancellationToken = default);
            }

            public interface IWidgetQueryAppService
            {
                [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "all", Kind = HttpProjectionOperationKind.Query, Permissions = new[] { "Widget.Get" })]
                Task<List<WidgetDto>> GetAllAsync(CancellationToken cancellationToken = default);

                [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "overdue", Kind = HttpProjectionOperationKind.Query, Permissions = new[] { "Widget.Get" })]
                Task<IReadOnlyList<WidgetDto>> GetOverdueAsync(CancellationToken cancellationToken = default);
            }
        }

        namespace Shop.Http
        {
            [CapabilityCompatibilityProjection(ContractType = typeof(Shop.Contracts.IWidgetAppService), RoutePrefix = "api/widgets")]
            public static class WidgetHttpProjection
            {
            }

            [CapabilityCompatibilityProjection(ContractType = typeof(Shop.Contracts.IWidgetQueryAppService), RoutePrefix = "api/widget-query")]
            public static class WidgetQueryHttpProjection
            {
            }
        }
        """;

    private static readonly string[] RequiredRoots =
    [
        "global::CrestCreates.DynamicApi.DynamicApiResponse",
        "global::CrestCreates.DynamicApi.DynamicApiResponse<global::Shop.Contracts.WidgetDto>",
        "global::CrestCreates.DynamicApi.DynamicApiResponse<global::System.Collections.Generic.List<global::Shop.Contracts.WidgetDto>>",
        "global::CrestCreates.DynamicApi.DynamicApiResponse<global::System.Collections.Generic.IReadOnlyList<global::Shop.Contracts.WidgetDto>>",
        "global::Shop.Contracts.CreateWidgetDto",
        "global::Shop.Contracts.WidgetDto",
        "global::System.Collections.Generic.List<global::Shop.Contracts.WidgetDto>",
        "global::System.Collections.Generic.IReadOnlyList<global::Shop.Contracts.WidgetDto>",
        "global::System.Guid",
        "global::System.String",
    ];

    [Fact]
    public async Task Projection_surface_roots_and_stj_metadata_must_close_in_one_build()
    {
        var probe = await Probe.RunAsync();

        probe.ExitCode.Should().Be(0, probe.Output);
        probe.Output.Should().NotContain("SYSLIB1030",
            "the official STJ generator must close every root the projection declares");
        probe.Output.Should().Contain("warning CCHTTP002",
            "the legacy static registrar declines ownership with a warning, not a silent second publication");

        probe.Bundle.Should().NotBeEmpty("the precompile stage must materialize ordinary Compile input");
        probe.Bundle.Should().Contain("GeneratedAppServiceHttpContribution_");
        probe.Bundle.Should().Contain("__Update_CompatibilityInput",
            "composed route/body/If-Match inputs must be materialized");
        probe.Bundle.Should().Contain("GeneratedAppServiceHttpActivation_Probe",
            "one ExplicitHost activation is emitted per owner assembly");
        probe.Bundle.Should().NotContain("HttpProjectionPrecompile",
            "wire identities must never leak the isolated producer's synthetic name");
        probe.Bundle.Should().Contain("compat.appservice.Probe.Shop_002EContracts_002EIWidgetAppService.create");

        foreach (var root in RequiredRoots)
        {
            probe.JsonContractsSource.Should().Contain(
                $"[global::System.Text.Json.Serialization.JsonSerializable(typeof({root}))]",
                $"the JSON context must register '{root}'");
        }

        probe.JsonContractsSource.Should().Contain("class HttpProjectionJsonContext_Probe");
        probe.JsonContractsSource.Should().Contain("JsonKnownNamingPolicy.CamelCase");
        probe.JsonContractsSource.Should().Contain("namespace CrestCreates.Generated.HttpProjection");
        probe.StjSources.Should().Contain(source =>
            source.Contains("JsonTypeInfo<global::Shop.Contracts.WidgetDto>", StringComparison.Ordinal));
        probe.StjSources.Should().Contain(source =>
            source.Contains("JsonTypeInfo<global::Shop.Contracts.CreateWidgetDto>", StringComparison.Ordinal));
        probe.StjSources.Should().Contain(source =>
            source.Contains("JsonTypeInfo<global::System.Collections.Generic.List<global::Shop.Contracts.WidgetDto>>", StringComparison.Ordinal));
        probe.StjSources.Should().Contain(source =>
            source.Contains("JsonTypeInfo<global::CrestCreates.DynamicApi.DynamicApiResponse<global::Shop.Contracts.WidgetDto>>", StringComparison.Ordinal));
        probe.StjSources.Should().Contain(source =>
            source.Contains("JsonTypeInfo<global::CrestCreates.DynamicApi.DynamicApiResponse>", StringComparison.Ordinal));

        using var manifest = JsonDocument.Parse(probe.OutputsManifest);
        var entries = manifest.RootElement.GetProperty("Entries").EnumerateArray().ToList();
        entries.Should().HaveCount(3, "two contract bundles plus one assembly activation");
        entries.Select(entry => entry.GetProperty("ArtifactHintName").GetString())
            .Should().Contain("GeneratedAppServiceHttpBundle_Shop_002EContracts_002EIWidgetAppService.g.cs");
        entries.Should().OnlyContain(entry =>
            entry.GetProperty("OwnerAssemblyName").GetString() == "Probe"
            && entry.GetProperty("ModelSchemaVersion").GetString() == AppServiceHttpProjectionModel.ModelSchemaVersion
            && entry.GetProperty("ContentHash").GetString()!.StartsWith("sha256:", StringComparison.Ordinal));
        var bundleEntry = entries.Single(entry =>
            entry.GetProperty("ArtifactHintName").GetString()!.Contains("IWidgetAppService"));
        bundleEntry.GetProperty("RequiredJsonRootTypeNames").EnumerateArray()
            .Select(value => value.GetString())
            .Should().Contain("global::System.Guid")
            .And.Contain("string",
                "the If-Match stamp is a real input root, not an assumed framework default");

        probe.InputManifest.Should().Contain("\"schema\":\"appservice-http-inputs-v1\"");
        probe.InputManifest.Should().Contain("Declaration.cs");
    }

    [Fact]
    public async Task Tampered_outputs_manifest_must_fail_closed_and_recover()
    {
        const string tamper = """
            <Target Name="TamperAppServiceHttpManifest"
                    AfterTargets="IncludeCrestAppServiceHttpArtifacts"
                    BeforeTargets="PrepareCrestCreatesJsonContractInputs">
              <WriteLinesToFile File="$(_CrestAppServiceHttpOutputsManifestFullPath)"
                                Lines="{&quot;SchemaVersion&quot;:&quot;appservice-http-artifacts-v1&quot;,&quot;ModelSchemaVersion&quot;:&quot;appservice-http-model-v1&quot;,&quot;Entries&quot;:[]}"
                                Overwrite="true" />
            </Target>
            """;
        var probe = await Probe.RunAsync(extraProjectXml: tamper);

        probe.ExitCode.Should().NotBe(0,
            "a manifest that no longer covers the compiled surface must fail the JSON stage");
        probe.Output.Should().Contain("CCHTTP006");
        probe.Output.Should().NotContain("CJC013",
            "a response document with diagnostics is the authoritative failure, not a transport error");

        // Recovery: with the tamper target gone, the next build regenerates the manifest and succeeds.
        await Probe.ResetProjectAsync(probe.Directory);
        var recovered = await Probe.BuildAsync(probe.Directory);
        recovered.ExitCode.Should().Be(0, recovered.Output);
        File.ReadAllText(probe.OutputsManifestPath).Should().Contain("GeneratedAppServiceHttpBundle_");
    }

    [Fact]
    public async Task Unknown_schema_must_fail_without_recording_a_new_success_stamp()
    {
        var probe = await Probe.RunAsync();
        var previousStamp = File.GetLastWriteTimeUtc(probe.StampPath);

        var failed = await Probe.BuildAsync(
            probe.Directory,
            "-p:CrestCreatesAppServiceHttpModelSchemaVersion=unknown-schema");
        failed.ExitCode.Should().NotBe(0);
        failed.Output.Should().Contain("CCHTTP006");
        if (File.Exists(probe.StampPath))
        {
            File.GetLastWriteTimeUtc(probe.StampPath).Should().Be(previousStamp,
                "a failed build must not record a new success");
        }

        var recovered = await Probe.BuildAsync(probe.Directory);
        recovered.ExitCode.Should().Be(0, recovered.Output);
    }

    [Fact]
    public async Task Noop_clean_and_rename_must_preserve_or_rebuild_deterministically()
    {
        var probe = await Probe.RunAsync();
        var bundle = probe.Bundle;
        var stamp = File.GetLastWriteTimeUtc(probe.StampPath);

        (await Probe.BuildAsync(probe.Directory)).ExitCode.Should().Be(0);
        File.GetLastWriteTimeUtc(probe.StampPath).Should().Be(stamp,
            "an unchanged input set must not re-run the producer");
        File.ReadAllText(probe.BundlePath).Should().Be(bundle);

        // A missing output with a surviving stamp must be repaired, never treated as complete.
        File.Move(probe.OutputsManifestPath, probe.OutputsManifestPath + ".review-backup");
        (await Probe.BuildAsync(probe.Directory)).ExitCode.Should().Be(0);
        File.Exists(probe.OutputsManifestPath).Should().BeTrue("a missing owned output invalidates the stamp");
        File.ReadAllText(probe.OutputsManifestPath).Should().Contain("GeneratedAppServiceHttpBundle_");

        var cleaned = await Probe.CleanAsync(probe.Directory);
        cleaned.ExitCode.Should().Be(0, cleaned.Output);
        File.Exists(probe.BundlePath).Should().BeFalse("clean removes the materialized surface");
        File.Exists(probe.StampPath).Should().BeFalse();

        var rebuilt = await Probe.BuildAsync(probe.Directory);
        rebuilt.ExitCode.Should().Be(0, rebuilt.Output);
        File.ReadAllText(probe.BundlePath).Should().Be(bundle, "the producer is deterministic");

        var declaration = Path.Combine(probe.Directory, "Declaration.cs");
        var renamed = Path.Combine(probe.Directory, "Renamed.cs");
        File.Move(declaration, renamed);
        (await Probe.BuildAsync(probe.Directory)).ExitCode.Should().Be(0);
        File.ReadAllText(probe.InputManifestPath).Should().Contain("Renamed.cs").And.NotContain("Declaration.cs");
    }

    [Fact]
    public async Task Design_time_must_not_reuse_or_write_the_runtime_stamp()
    {
        var probe = await Probe.RunAsync();
        var runtimeBundle = probe.Bundle;
        var runtimeStamp = File.GetLastWriteTimeUtc(probe.StampPath);

        (await Probe.DesignTimeAsync(probe.Directory)).ExitCode.Should().Be(0);
        var designBundle = Path.Combine(probe.Intermediate, "design-time", "CrestCreates.AppServiceHttp.g.cs");
        File.ReadAllText(designBundle).Should().Contain("GeneratedAppServiceHttpContribution_");
        File.ReadAllText(Path.Combine(probe.Intermediate, "design-time", "CrestCreates.JsonContracts.g.cs"))
            .Should().Contain("HttpProjectionJsonContext_Probe");

        var declaration = Path.Combine(probe.Directory, "Declaration.cs");
        File.WriteAllText(declaration, File.ReadAllText(declaration)
            .Replace("Route = \"assign\"", "Route = \"assign-design\""));
        (await Probe.DesignTimeAsync(probe.Directory)).ExitCode.Should().Be(0);
        File.ReadAllText(designBundle).Should().Contain("assign-design");
        File.ReadAllText(probe.BundlePath).Should().Be(runtimeBundle,
            "the runtime surface must not be rewritten by a design-time build");
        File.GetLastWriteTimeUtc(probe.StampPath).Should().Be(runtimeStamp);

        (await Probe.BuildAsync(probe.Directory)).ExitCode.Should().Be(0);
        File.ReadAllText(probe.BundlePath).Should().Contain("assign-design");
    }

    [Fact]
    public async Task Target_framework_and_runtime_identifier_outputs_stay_isolated()
    {
        var probe = await Probe.RunAsync(tfms: "net10.0;net10.0-windows", external: true);
        probe.ExitCode.Should().Be(0, probe.Output);
        File.ReadAllText(Path.Combine(probe.Directory, "obj", "Debug", "net10.0",
                "CrestCreates.AppServiceHttp.inputs.json"))
            .Should().Contain("\"targetFramework\":\"net10.0\"");
        File.ReadAllText(Path.Combine(probe.Directory, "obj", "Debug", "net10.0-windows",
                "CrestCreates.AppServiceHttp.inputs.json"))
            .Should().Contain("\"targetFramework\":\"net10.0-windows\"");

        var baseManifest = File.ReadAllText(Path.Combine(probe.Directory, "obj", "Debug", "net10.0",
            "CrestCreates.AppServiceHttp.inputs.json"));

        foreach (var rid in new[] { "linux-x64", "linux-arm64" })
        {
            var result = await Probe.BuildAsync(
                probe.Directory, "-f", "net10.0", "-r", rid, "--self-contained", "false");
            result.ExitCode.Should().Be(0, result.Output);
            File.ReadAllText(Path.Combine(probe.Directory, "obj", "Debug", "net10.0", rid,
                    "CrestCreates.AppServiceHttp.inputs.json"))
                .Should().Contain("\"runtimeIdentifier\":\"" + rid + "\"");
        }

        File.ReadAllText(Path.Combine(probe.Directory, "obj", "Debug", "net10.0",
            "CrestCreates.AppServiceHttp.inputs.json")).Should().Be(baseManifest,
            "RID-specific builds must not overwrite the RID-less partition");
    }

    [Fact]
    public async Task Packaged_toolchain_must_build_a_fresh_external_consumer()
    {
        var probe = await Probe.RunPackagedAsync();

        probe.ExitCode.Should().Be(0, probe.Output);
        probe.Bundle.Should().Contain("GeneratedAppServiceHttpContribution_",
            "the packaged precompile stage must materialize the projection");
        probe.JsonContractsSource.Should().Contain("HttpProjectionJsonContext_Probe",
            "the packaged JSON stage must close the projection roots");
        probe.StjSources.Should().Contain(source =>
            source.Contains("JsonTypeInfo<global::Shop.Contracts.WidgetDto>", StringComparison.Ordinal));
        File.ReadAllText(Path.Combine(probe.Directory, "Probe.csproj")).Should()
            .NotContain(Probe.RepoRoot, "the external consumer must not depend on repository imports");
    }

    [Fact]
    public async Task Forged_bundle_after_include_must_fail_closed_and_recover()
    {
        const string tamper = """
            <Target Name="ForgeAppServiceBundle"
                    AfterTargets="IncludeCrestAppServiceHttpArtifacts"
                    BeforeTargets="PrepareCrestCreatesJsonContractInputs">
              <Exec Command="sed -i 's#api/widgets#api/forged#g' &quot;$(_CrestAppServiceHttpBundleFullPath)&quot;" />
            </Target>
            """;
        var probe = await Probe.RunAsync(extraProjectXml: tamper);

        probe.ExitCode.Should().NotBe(0,
            "the JSON stage must reconcile the actual compiled bundle, not only the producer's expectation");
        probe.Output.Should().Contain("CCHTTP006");
        probe.Bundle.Should().Contain("api/forged", "the probe really replaced the compiled bundle route");
        probe.Output.Should().NotContain("ArgumentException");

        await Probe.ResetProjectAsync(probe.Directory);
        var recovered = await Probe.BuildAsync(probe.Directory);
        recovered.ExitCode.Should().Be(0, recovered.Output);
        File.ReadAllText(probe.BundlePath).Should().Contain("api/widgets");
    }

    [Fact]
    public async Task Explicit_host_without_contract_type_must_fail_with_cchttp001()
    {
        const string source = """
            using CrestCreates.Domain.Shared.Attributes;

            namespace Shop.Http
            {
                [CapabilityCompatibilityProjection(
                    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost)]
                public static class MissingContractAnchor
                {
                }
            }
            """;
        var probe = await Probe.RunAsync(source: source);

        probe.ExitCode.Should().NotBe(0,
            "an explicitly formal declaration must not be mistaken for a legacy declaration");
        probe.Output.Should().Contain("CCHTTP001");
        probe.Bundle.Should().BeEmpty("no partial surface may be materialized");
        File.Exists(probe.StampPath).Should().BeFalse("a failed producer records no success stamp");
    }

    [Fact]
    public async Task Duplicate_contract_anchors_must_report_cchttp003_in_the_producer()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using CrestCreates.Domain.Shared.Attributes;

            namespace Shop.Http
            {
                public interface IService
                {
                    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get)]
                    Task<string> GetAsync(CancellationToken cancellationToken = default);
                }

                [CapabilityCompatibilityProjection(ContractType = typeof(IService), RoutePrefix = "api/one")]
                public static class AnchorOne
                {
                }

                [CapabilityCompatibilityProjection(ContractType = typeof(IService), RoutePrefix = "api/two")]
                public static class AnchorTwo
                {
                }
            }
            """;
        var probe = await Probe.RunAsync(source: source);

        probe.ExitCode.Should().NotBe(0);
        probe.Output.Should().Contain("CCHTTP003");
        probe.Output.Should().Contain("AnchorOne");
        probe.Output.Should().Contain("AnchorTwo");
        probe.Output.Should().NotContain("same key",
            "the producer must diagnose the duplicate anchors before building its dictionary");
        probe.Bundle.Should().BeEmpty("a conflicted producer emits no partial surface");
        File.Exists(probe.StampPath).Should().BeFalse("a conflicted producer records no success stamp");
    }

    [Fact]
    public async Task Referenced_contract_and_dtos_must_close_in_the_consumer_build()
    {
        var probe = await Probe.RunReferencedAsync();

        probe.ExitCode.Should().Be(0, probe.Output);
        probe.Output.Should().NotContain("SYSLIB1030");
        probe.Bundle.Should().Contain("GeneratedAppServiceHttpContribution_");
        probe.OutputsManifest.Should().Contain("\"ContractAssemblyName\":\"Shop.Contracts\"");
        probe.JsonContractsSource.Should().Contain(
            "typeof(global::Shop.Contracts.WidgetDto)");
        probe.JsonContractsSource.Should().Contain(
            "typeof(global::CrestCreates.DynamicApi.DynamicApiResponse<global::Shop.Contracts.WidgetDto>)");
        probe.StjSources.Should().Contain(stj =>
            stj.Contains("JsonTypeInfo<global::Shop.Contracts.WidgetDto>", StringComparison.Ordinal),
            "the official STJ generator must close referenced DTO roots in the consumer build");
        probe.StjSources.Should().Contain(stj =>
            stj.Contains("JsonTypeInfo<global::Shop.Contracts.CreateWidgetDto>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Removing_the_last_projection_must_clear_bundle_manifest_and_json_roots()
    {
        var probe = await Probe.RunAsync();
        probe.ExitCode.Should().Be(0, probe.Output);

        await File.WriteAllTextAsync(
            Path.Combine(probe.Directory, "Declaration.cs"),
            "namespace Shop.Empty { public sealed class Placeholder { } }");
        var removed = await Probe.BuildAsync(probe.Directory);

        removed.ExitCode.Should().Be(0, removed.Output);
        File.ReadAllText(probe.BundlePath).Should().NotContain("GeneratedAppServiceHttpContribution_");
        using var manifest = JsonDocument.Parse(File.ReadAllText(probe.OutputsManifestPath));
        manifest.RootElement.GetProperty("Entries").GetArrayLength().Should().Be(0);
        File.ReadAllText(Path.Combine(probe.Intermediate, "CrestCreates.JsonContracts.g.cs"))
            .Should().NotContain("HttpProjectionJsonContext_");
        removed.Output.Should().NotContain("CCHTTP002",
            "no projection declaration remains to be skipped");
    }

    [Fact]
    public async Task Nullable_and_nested_generic_body_roots_must_close_with_official_stj()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using CrestCreates.Domain.Shared.Attributes;

            namespace Probe
            {
                public struct Box<T> { public T Value { get; set; } }
                public class Outer<T> { public class Inner<U> { public U Value { get; set; } = default!; } }

                public interface IService
                {
                    [AppServiceHttpAction(Verb = HttpProjectionVerb.Post, Route = "nullable",
                        Body = HttpProjectionBodyPolicy.Required)]
                    Task<string> EchoNullableAsync(Box<int>? input, CancellationToken cancellationToken = default);

                    [AppServiceHttpAction(Verb = HttpProjectionVerb.Post, Route = "nested",
                        Body = HttpProjectionBodyPolicy.Required)]
                    Task<string> EchoNestedAsync(Outer<int>.Inner<string> input, CancellationToken cancellationToken = default);
                }

                [CapabilityCompatibilityProjection(ContractType = typeof(IService), RoutePrefix = "api/probe")]
                public static class Anchor
                {
                }
            }
            """;
        var probe = await Probe.RunAsync(source: source);

        probe.ExitCode.Should().Be(0, probe.Output);
        probe.Output.Should().NotContain("SYSLIB1030",
            "the official STJ generator must close the exact nullable/nested root symbols");
        probe.JsonContractsSource.Should().Contain(
            "typeof(global::System.Nullable<global::Probe.Box<global::System.Int32>>)");
        probe.JsonContractsSource.Should().Contain(
            "typeof(global::Probe.Outer<global::System.Int32>.Inner<global::System.String>)");
        probe.StjSources.Should().Contain(stj =>
            stj.Contains("JsonTypeInfo<global::Probe.Box<int>>", StringComparison.Ordinal),
            "the official STJ generator must close the nullable generic root");
        probe.StjSources.Should().Contain(stj =>
            stj.Contains("JsonTypeInfo<global::Probe.Outer<int>.Inner<string>>", StringComparison.Ordinal),
            "the official STJ generator must close the nested generic root");
    }

    private sealed record ProbeResult(
        string Directory,
        int ExitCode,
        string Output,
        string Intermediate)
    {
        public string BundlePath => Path.Combine(Intermediate, "CrestCreates.AppServiceHttp.g.cs");
        public string StampPath => Path.Combine(Intermediate, "CrestCreates.AppServiceHttp.stamp");
        public string InputManifestPath => Path.Combine(Intermediate, "CrestCreates.AppServiceHttp.inputs.json");
        public string OutputsManifestPath => Path.Combine(Intermediate, "CrestCreates.AppServiceHttp.outputs.json");
        public string Bundle => File.Exists(BundlePath) ? File.ReadAllText(BundlePath) : string.Empty;
        public string InputManifest => File.Exists(InputManifestPath) ? File.ReadAllText(InputManifestPath) : string.Empty;
        public string OutputsManifest => File.Exists(OutputsManifestPath) ? File.ReadAllText(OutputsManifestPath) : string.Empty;
        public string JsonContractsSource => Read(Path.Combine(Intermediate, "CrestCreates.JsonContracts.g.cs"));

        public IReadOnlyList<string> StjSources
        {
            get
            {
                var root = Path.Combine(Directory, "obj");
                return System.IO.Directory.Exists(root)
                    ? System.IO.Directory.EnumerateFiles(root, "*.g.cs", SearchOption.AllDirectories)
                        .Where(path => path.Contains("System.Text.Json.SourceGeneration", StringComparison.Ordinal))
                        .Select(File.ReadAllText)
                        .ToArray()
                    : Array.Empty<string>();
            }
        }

        private static string Read(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    private sealed class Probe
    {
        public static string RepoRoot => FindRepoRoot();

        public static async Task<ProbeResult> RunPackagedAsync()
        {
            var repoRoot = FindRepoRoot();
            await EnsureToolchainAsync(repoRoot);
            var directory = Path.Combine(Path.GetTempPath(), "issue129-pkg-" + Guid.NewGuid().ToString("N")[..8]);
            var feed = Path.Combine(directory, "feed");
            var lib = Path.Combine(directory, "lib");
            System.IO.Directory.CreateDirectory(feed);
            System.IO.Directory.CreateDirectory(lib);

            var pack = await RunDotnetAsync(directory,
            [
                "pack",
                Path.Combine(repoRoot, "src/Tooling/CrestCreates.JsonContracts.BuildTasks/CrestCreates.JsonContracts.BuildTasks.csproj"),
                "-o", feed, "-p:PackageVersion=1.0.0", "-m:1",
            ], TimeSpan.FromMinutes(12));
            pack.ExitCode.Should().Be(0, pack.Output);

            var assemblies = System.IO.Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll")
                .Where(path => Path.GetFileName(path).StartsWith("CrestCreates.", StringComparison.Ordinal)
                    || Path.GetFileName(path) is "FluentValidation.dll" or "Rougamo.dll")
                .ToArray();
            foreach (var assembly in assemblies)
            {
                File.Copy(assembly, Path.Combine(lib, Path.GetFileName(assembly)), overwrite: true);
            }

            File.Copy(typeof(CodeGenerator.AppServiceCompatibilityGenerator.AppServiceCompatibilityGenerator).Assembly.Location,
                Path.Combine(lib, "CrestCreates.CodeGenerator.dll"), overwrite: true);

            var project = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
                    <CompilerGeneratedFilesOutputPath>obj/$(Configuration)/$(TargetFramework)/source-generators</CompilerGeneratedFilesOutputPath>
                    <JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>
                    <RestorePackagesPath>packages</RestorePackagesPath>
                    <RestoreSources>feed</RestoreSources>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="CrestCreates.JsonContracts.Build" Version="1.0.0" />
                    <FrameworkReference Include="Microsoft.AspNetCore.App" />
                    <Analyzer Include="lib/CrestCreates.CodeGenerator.dll" />
                    <Reference Include="lib/*.dll" Exclude="lib/CrestCreates.CodeGenerator.dll" />
                  </ItemGroup>
                </Project>
                """;
            await File.WriteAllTextAsync(Path.Combine(directory, "Probe.csproj"), project);
            await File.WriteAllTextAsync(Path.Combine(directory, "Declaration.cs"), DeclarationSource);

            var result = await BuildAsync(directory);
            return new ProbeResult(
                directory,
                result.ExitCode,
                result.Output,
                Path.Combine(directory, "obj", "Debug", "net10.0"));
        }

        public static async Task<ProbeResult> RunAsync(
            string? directory = null,
            string? extraProjectXml = null,
            string tfms = "net10.0",
            bool external = false,
            string? source = null)
        {
            var repoRoot = FindRepoRoot();
            await EnsureToolchainAsync(repoRoot);

            directory ??= external
                ? Path.Combine(Path.GetTempPath(), "issue129-b-" + Guid.NewGuid().ToString("N")[..8])
                : Path.Combine(repoRoot, "tests", "artifacts", "issue129-b-" + Guid.NewGuid().ToString("N")[..8]);
            System.IO.Directory.CreateDirectory(directory);

            var projectPath = Path.Combine(directory, "Probe.csproj");
            if (!File.Exists(projectPath))
            {
                var project = ProjectTemplate
                    .Replace("__ROOT__", repoRoot.Replace('\\', '/'))
                    .Replace("__TFMS__", tfms.Contains(';')
                        ? "<TargetFrameworks>" + tfms + "</TargetFrameworks>"
                        : "<TargetFramework>" + tfms + "</TargetFramework>")
                    .Replace("__IMPORTS__", CrudServiceGenerator.CrudJsonContractMsBuildBaselineTests.ToolchainImports
                        .Replace("__ROOT__", repoRoot.Replace('\\', '/')))
                    .Replace("__EXTRA__", extraProjectXml ?? string.Empty);
                await File.WriteAllTextAsync(projectPath, project);
                await File.WriteAllTextAsync(Path.Combine(directory, "Declaration.cs"), source ?? DeclarationSource);
            }

            var result = await BuildAsync(directory);
            return new ProbeResult(
                directory,
                result.ExitCode,
                result.Output,
                Path.Combine(directory, "obj", "Debug", tfms.Split(';')[0]));
        }

        public static async Task<ProbeResult> RunReferencedAsync()
        {
            var repoRoot = FindRepoRoot();
            await EnsureToolchainAsync(repoRoot);
            var root = Path.Combine(
                repoRoot, "tests", "artifacts", "issue129-ref-" + Guid.NewGuid().ToString("N")[..8]);
            var contractsDirectory = Path.Combine(root, "Contracts");
            var consumerDirectory = Path.Combine(root, "Consumer");
            System.IO.Directory.CreateDirectory(contractsDirectory);
            System.IO.Directory.CreateDirectory(consumerDirectory);

            var contractsProject = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <AssemblyName>Shop.Contracts</AssemblyName>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="__ROOT__/src/Framework/Ddd/CrestCreates.Domain.Shared/CrestCreates.Domain.Shared.csproj" />
                  </ItemGroup>
                </Project>
                """.Replace("__ROOT__", repoRoot.Replace('\\', '/'));
            await File.WriteAllTextAsync(Path.Combine(contractsDirectory, "Contracts.csproj"), contractsProject);
            await File.WriteAllTextAsync(Path.Combine(contractsDirectory, "Contracts.cs"), """
                using System;
                using System.Threading;
                using System.Threading.Tasks;
                using CrestCreates.Domain.Shared.Attributes;

                namespace Shop.Contracts
                {
                    public sealed class WidgetDto
                    {
                        public Guid Id { get; set; }
                        public string Name { get; set; } = string.Empty;
                    }

                    public sealed class CreateWidgetDto
                    {
                        public string Name { get; set; } = string.Empty;
                    }

                    public interface IWidgetAppService
                    {
                        [AppServiceHttpAction(Verb = HttpProjectionVerb.Post, Route = "",
                            Kind = HttpProjectionOperationKind.Command,
                            Body = HttpProjectionBodyPolicy.Required)]
                        Task<WidgetDto> CreateAsync(CreateWidgetDto input, CancellationToken cancellationToken = default);

                        [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "{id}",
                            Kind = HttpProjectionOperationKind.Query)]
                        Task<WidgetDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
                    }
                }
                """);

            var probeReference = """
                <ItemGroup>
                  <ProjectReference Include="../Contracts/Contracts.csproj" />
                </ItemGroup>
                """;
            var project = ProjectTemplate
                .Replace("__ROOT__", repoRoot.Replace('\\', '/'))
                .Replace("__TFMS__", "<TargetFramework>net10.0</TargetFramework>")
                .Replace("__IMPORTS__", CrudServiceGenerator.CrudJsonContractMsBuildBaselineTests.ToolchainImports
                    .Replace("__ROOT__", repoRoot.Replace('\\', '/')))
                .Replace("__EXTRA__", probeReference);
            await File.WriteAllTextAsync(Path.Combine(consumerDirectory, "Probe.csproj"), project);
            await File.WriteAllTextAsync(Path.Combine(consumerDirectory, "Declaration.cs"), """
                using CrestCreates.Domain.Shared.Attributes;

                namespace Shop.Http
                {
                    [CapabilityCompatibilityProjection(
                        ContractType = typeof(Shop.Contracts.IWidgetAppService),
                        RoutePrefix = "api/widgets")]
                    public static class WidgetHttpProjection
                    {
                    }
                }
                """);

            var result = await BuildAsync(consumerDirectory);
            return new ProbeResult(
                consumerDirectory,
                result.ExitCode,
                result.Output,
                Path.Combine(consumerDirectory, "obj", "Debug", "net10.0"));
        }

        public static async Task<(int ExitCode, string Output)> CleanAsync(string directory)
            => await RunDotnetAsync(directory, ["clean", Path.Combine(directory, "Probe.csproj")], TimeSpan.FromMinutes(10));

        public static async Task ResetProjectAsync(string directory, string tfms = "net10.0")
        {
            var repoRoot = FindRepoRoot();
            var project = ProjectTemplate
                .Replace("__ROOT__", repoRoot.Replace('\\', '/'))
                .Replace("__TFMS__", tfms.Contains(';')
                    ? "<TargetFrameworks>" + tfms + "</TargetFrameworks>"
                    : "<TargetFramework>" + tfms + "</TargetFramework>")
                .Replace("__IMPORTS__", CrudJsonContractMsBuildBaselineTests.ToolchainImports
                    .Replace("__ROOT__", repoRoot.Replace('\\', '/')))
                .Replace("__EXTRA__", string.Empty);
            await File.WriteAllTextAsync(Path.Combine(directory, "Probe.csproj"), project);
            await Task.CompletedTask;
        }

        private static string FindRepoRoot()
        {
            for (var current = new System.IO.DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            {
                if (File.Exists(Path.Combine(current.FullName, "CrestCreates.slnx")))
                {
                    return current.FullName;
                }
            }

            throw new InvalidOperationException("Repository root not found.");
        }

        public static async Task<(int ExitCode, string Output)> BuildAsync(
            string directory,
            params string[] options)
        {
            var args = new List<string> { "build", Path.Combine(directory, "Probe.csproj") };
            args.AddRange(options);
            return await RunDotnetAsync(directory, args, TimeSpan.FromMinutes(12));
        }

        public static async Task<(int ExitCode, string Output)> DesignTimeAsync(string directory)
        {
            (await RunDotnetAsync(directory, ["restore", Path.Combine(directory, "Probe.csproj")], TimeSpan.FromMinutes(5)))
                .ExitCode.Should().Be(0);
            return await RunDotnetAsync(directory,
            [
                "msbuild", Path.Combine(directory, "Probe.csproj"), "-t:Compile",
                "-p:DesignTimeBuild=true", "-p:BuildingInsideVisualStudio=true", "-p:SkipCompilerExecution=true",
            ], TimeSpan.FromMinutes(12));
        }

        private static async Task EnsureToolchainAsync(string repoRoot)
        {
            var taskAssembly = Path.Combine(
                repoRoot, "src", "Tooling", "CrestCreates.JsonContracts.BuildTasks", "bin", "Debug", "net10.0",
                "CrestCreates.JsonContracts.BuildTasks.dll");
            if (File.Exists(taskAssembly))
            {
                return;
            }

            await RunDotnetAsync(repoRoot,
                ["build", "src/Tooling/CrestCreates.JsonContracts.BuildTasks/CrestCreates.JsonContracts.BuildTasks.csproj",
                    "-p:NuGetAudit=false", "--nologo", "-v", "q"], TimeSpan.FromMinutes(12));
        }

        private static async Task<(int ExitCode, string Output)> RunDotnetAsync(
            string workingDirectory,
            IEnumerable<string> arguments,
            TimeSpan timeout)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet") { WorkingDirectory = workingDirectory },
            };
            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            process.StartInfo.ArgumentList.Add("-p:NuGetAudit=false");
            process.StartInfo.ArgumentList.Add("--nologo");
            process.StartInfo.ArgumentList.Add("-v:q");
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
            process.StartInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
            process.Start();

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeoutSource = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw new TimeoutException("The AppService HTTP probe build exceeded its bounded timeout.");
            }

            return (process.ExitCode, await stdout + await stderr);
        }
    }
}
