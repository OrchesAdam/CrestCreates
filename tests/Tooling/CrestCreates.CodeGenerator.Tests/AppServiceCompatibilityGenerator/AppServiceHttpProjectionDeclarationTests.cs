using System.Linq;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using FluentAssertions;
using Xunit;
using CompatibilityGen = CrestCreates.CodeGenerator.AppServiceCompatibilityGenerator.AppServiceCompatibilityGenerator;

namespace CrestCreates.CodeGenerator.Tests.AppServiceCompatibilityGenerator;

/// <summary>
/// Issue #129 S2.1: the static compatibility registrar must not double-publish an explicit-host
/// declaration, and a mode-less declaration gets an explicit migration signal instead of a silent
/// drift between the declaration and the registrar that owns it.
/// </summary>
public sealed class AppServiceHttpProjectionDeclarationTests
{
    [Fact]
    public void Explicit_host_contract_declaration_is_skipped_with_cchttp002()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>("""
            using System.Threading;
            using System.Threading.Tasks;
            using CrestCreates.Domain.Shared.Attributes;

            namespace Shop.Http
            {
                public interface IShippingService
                {
                    Task<string> PingAsync(CancellationToken cancellationToken = default);
                }

                [CapabilityCompatibilityProjection(
                    ContractType = typeof(IShippingService),
                    RoutePrefix = "api/shipping")]
                public static class ShippingHttpProjection { }
            }
            """);

        result.Diagnostics.Should().Contain(d => d.Id == "CCHTTP002" && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning,
            "an explicit-host declaration belongs to the precompile front-end; the static registrar must skip it and say so");
        result.GeneratedSources.Should().NotContain(source => source.SourceText.ToString().Contains("compat.appservice"),
            "skipping must not leave half an endpoint set behind");
    }

    [Fact]
    public void Mode_less_declaration_warns_cchttp005_and_keeps_static_emission()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input);
            }
            """,
            additionalSources: AppServiceCompatibilityGeneratorTests.BuildCompatibilityStubs());

        result.Diagnostics.Should().Contain(d => d.Id == "CCHTTP005" && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning,
            "a mode-less declaration must carry an explicit migration signal while the static registrar still owns it");
        result.GeneratedSources.Should().Contain(source =>
                source.SourceText.ToString().Contains("DescriptorProviderRegistry.Register"),
            "the warning must not change today's static emission");
    }

    [Fact]
    public void Legacy_static_mode_declaration_keeps_the_static_registrar_without_migration_warning()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(
            """
            using CrestCreates.Domain.Shared.Attributes;

            namespace MyApp;

            [CrestService]
            [CapabilityCompatibilityProjection(RegistrationMode = HttpProjectionRegistrationMode.LegacyStatic)]
            public class InventoryAppService
            {
                public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.Task.FromResult(input);
            }
            """,
            additionalSources: AppServiceCompatibilityGeneratorTests.BuildCompatibilityStubs());

        result.Diagnostics.Should().NotContain(d => d.Id == "CCHTTP005",
            "an explicit LegacyStatic declaration is a deliberate migration state, not a missing mode");
        result.GeneratedSources.Should().Contain(source =>
            source.SourceText.ToString().Contains("DescriptorProviderRegistry.Register"));
    }
}
