using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.CodeGenerator.AppServiceHttpProjection;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using CrestCreates.Tooling.HttpProjection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.AppServiceCompatibilityGenerator;

/// <summary>
/// Default-toolchain diagnostics restored after the legacy analyzer exit: a formal projection without
/// the AppService HTTP/JSON toolchain must surface CCHTTP002, and the retired LegacyStatic mode must
/// fail deterministically (CCHTTP001) instead of being silently skipped.
/// </summary>
public sealed class AppServiceHttpToolchainDiagnosticsTests
{
    private const string Stubs = """
        namespace CrestCreates.Domain.Shared.Attributes
        {
            public enum HttpProjectionRegistrationMode { ExplicitHost = 0, LegacyStatic = 1 }

            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method, AllowMultiple = false)]
            public sealed class CapabilityCompatibilityProjectionAttribute : System.Attribute
            {
                public System.Type? ContractType { get; init; }
                public HttpProjectionRegistrationMode RegistrationMode { get; init; }
            }
        }
        """;

    private const string FormalDeclaration = """
        namespace Probe
        {
            public interface IService
            {
                System.Threading.Tasks.Task<string> PingAsync(System.Threading.CancellationToken cancellationToken = default);
            }

            [CrestCreates.Domain.Shared.Attributes.CapabilityCompatibilityProjection(
                ContractType = typeof(IService))]
            public static class Anchor { }
        }
        """;

    private const string LegacyStaticDeclaration = """
        namespace Probe
        {
            public interface IService
            {
                System.Threading.Tasks.Task<string> PingAsync(System.Threading.CancellationToken cancellationToken = default);
            }

            [CrestCreates.Domain.Shared.Attributes.CapabilityCompatibilityProjection(
                ContractType = typeof(IService),
                RegistrationMode = CrestCreates.Domain.Shared.Attributes.HttpProjectionRegistrationMode.LegacyStatic)]
            public static class LegacyAnchor { }
        }
        """;

    [Fact]
    public async Task Missing_toolchain_reports_cchttp002()
    {
        var diagnostics = await AnalyzeAsync(FormalDeclaration, toolchainEnabled: false);

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CCHTTP002"
            && diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.GetMessage().Contains("toolchain", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Method_level_projection_is_rejected()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Probe
            {
                public interface IService
                {
                    System.Threading.Tasks.Task<string> PingAsync(System.Threading.CancellationToken cancellationToken = default);
                }

                public static class Anchor
                {
                    [CrestCreates.Domain.Shared.Attributes.CapabilityCompatibilityProjection(
                        ContractType = typeof(IService))]
                    public static System.Threading.Tasks.Task<string> PingAsync(
                        System.Threading.CancellationToken cancellationToken = default)
                        => System.Threading.Tasks.Task.FromResult("ok");
                }
            }
            """, toolchainEnabled: true);

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CCHTTP001"
            && diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.GetMessage().Contains("method-level", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Enabled_toolchain_does_not_report_cchttp002()
    {
        var diagnostics = await AnalyzeAsync(FormalDeclaration, toolchainEnabled: true);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "CCHTTP002");
    }

    [Fact]
    public async Task Implicit_legacy_static_declaration_is_rejected()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Probe
            {
                [CrestCreates.Domain.Shared.Attributes.CapabilityCompatibilityProjection]
                public static class ImplicitAnchor { }
            }
            """, toolchainEnabled: true);

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CCHTTP001"
            && diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.GetMessage().Contains("implicit legacy-static", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Legacy_static_mode_is_rejected_even_with_the_toolchain()
    {
        var diagnostics = await AnalyzeAsync(LegacyStaticDeclaration, toolchainEnabled: true);

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CCHTTP001"
            && diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.GetMessage().Contains("LegacyStatic", StringComparison.Ordinal));
    }

    [Fact]
    public void Legacy_static_mode_fails_the_formal_model_builder()
    {
        var compilation = SourceGeneratorTestHelper.CreateAnalysisCompilation(
            "Probe",
            new[] { Stubs, LegacyStaticDeclaration });
        var anchor = compilation.GetTypeByMetadataName("Probe.LegacyAnchor")!;

        var build = AppServiceHttpProjectionModelBuilder.Build(compilation, anchor);

        build.HasErrors.Should().BeTrue();
        build.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CCHTTP001"
            && diagnostic.Severity == AppServiceHttpDiagnosticSeverity.Error
            && diagnostic.Message.Contains("LegacyStatic", StringComparison.Ordinal));
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source, bool toolchainEnabled)
    {
        var compilation = SourceGeneratorTestHelper.CreateAnalysisCompilation(
            "Probe",
            new[] { Stubs, source });
        var options = new AnalyzerOptions(
            ImmutableArray<AdditionalText>.Empty,
            new TestAnalyzerConfigOptionsProvider(
                toolchainEnabled
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["build_property.CrestCreatesAppServiceHttpJsonToolchain"] = "true",
                    }
                    : new Dictionary<string, string>(StringComparer.Ordinal)));
        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new AppServiceHttpProjectionDiagnosticAnalyzer()),
            options);

        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private sealed class TestAnalyzerConfigOptionsProvider(Dictionary<string, string> globalOptions)
        : AnalyzerConfigOptionsProvider
    {
        private readonly TestAnalyzerConfigOptions _empty = new(new Dictionary<string, string>());

        public override AnalyzerConfigOptions GlobalOptions { get; } = new TestAnalyzerConfigOptions(globalOptions);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _empty;
    }

    private sealed class TestAnalyzerConfigOptions(Dictionary<string, string> options) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
            => options.TryGetValue(key, out value!);
    }
}
