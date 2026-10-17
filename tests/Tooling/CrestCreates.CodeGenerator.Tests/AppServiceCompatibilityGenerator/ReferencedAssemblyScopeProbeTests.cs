using System;
using System.Linq;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using CompatibilityGen = CrestCreates.CodeGenerator.AppServiceCompatibilityGenerator.AppServiceCompatibilityGenerator;

namespace CrestCreates.CodeGenerator.Tests.AppServiceCompatibilityGenerator;

/// <summary>
/// Issue #129 design addendum (referenced declaration scope) probe R-01. It records what the current
/// declaration surface can express, so the addendum is decided from behaviour instead of wording:
/// a projection declared inside a referenced assembly is invisible to the consuming compilation, while a
/// local anchor over a referenced contract is discovered.
/// </summary>
public sealed class ReferencedAssemblyScopeProbeTests
{
    private const string ReferencedContract = """
        namespace Referenced.Billing
        {
            public interface IQuoteService
            {
                System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken cancellationToken = default);
            }
        }
        """;

    [Fact]
    public void R01a_projection_declared_in_a_referenced_assembly_is_not_discovered()
    {
        // The referenced assembly really carries the declaration (attributes + [CrestService] + projection).
        var referencedImage = SourceGeneratorTestHelper.EmitAssemblyImage(
            "Referenced.Declarations",
            new[]
            {
                """
                namespace CrestCreates.Domain.Shared.Attributes
                {
                    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false)]
                    public class CrestServiceAttribute : System.Attribute { }

                    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method, AllowMultiple = false)]
                    public sealed class CapabilityCompatibilityProjectionAttribute : System.Attribute
                    {
                        public string? CapabilityIdPrefix { get; init; }
                        public string? RoutePrefix { get; init; }
                    }
                }

                namespace Referenced.Billing
                {
                    [CrestCreates.Domain.Shared.Attributes.CrestService]
                    [CrestCreates.Domain.Shared.Attributes.CapabilityCompatibilityProjection(RoutePrefix = "api/quotes")]
                    public class ReferencedQuoteService
                    {
                        public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken cancellationToken = default)
                            => System.Threading.Tasks.Task.FromResult(input);
                    }
                }
                """,
            });

        var result = SourceGeneratorTestHelper.RunGenerators(
            "Consumer.Compilation",
            new[] { "namespace Consumer { public static class Marker { } }" },
            new[] { new CompatibilityGen() },
            additionalMetadataReferences: new[]
            {
                MetadataReference.CreateFromImage(referencedImage),
            });

        result.GeneratedSources.Should().BeEmpty(
            "the generator only ever sees declarations in the compiling assembly; referencing an assembly "
            + "that carries [CrestService] + projection must not project it. This is the fact the addendum "
            + "builds on: referenced services need a local anchor.");
        result.GetErrors().Should().BeEmpty();
    }

    [Fact]
    public void R01b_local_anchor_over_a_referenced_contract_is_discovered()
    {
        var referencedImage = SourceGeneratorTestHelper.EmitAssemblyImage(
            "Referenced.Contracts",
            new[] { ReferencedContract });

        var localFacade = """
            using CrestCreates.Domain.Shared.Attributes;
            using Referenced.Billing;

            namespace Consumer.Host
            {
                [CrestService]
                [CapabilityCompatibilityProjection(RoutePrefix = "api/quotes")]
                public class QuoteFacade : IQuoteService
                {
                    public System.Threading.Tasks.Task<string> CreateAsync(string input, System.Threading.CancellationToken cancellationToken = default)
                        => System.Threading.Tasks.Task.FromResult(input);
                }
            }
            """;

        var result = SourceGeneratorTestHelpers.RunWithStubs(localFacade, referencedImage);

        result.GeneratedSources.Should().NotBeEmpty(
            "a local anchor implementing a contract from a referenced assembly is expressible today; the "
            + "addendum keeps this as the single declaration chain");
        result.GeneratedSources.Select(source => source.FileName).Should().Contain(
            name => name.Contains("GeneratedAppServiceCompatibilityCapabilities", StringComparison.Ordinal));
        result.GetErrors().Should().BeEmpty("the probe must compile against the real reference image, not stubs");
    }
}

file static class SourceGeneratorTestHelpers
{
    internal static SourceGeneratorResult RunWithStubs(string source, byte[] referencedImage)
        => SourceGeneratorTestHelper.RunGenerators(
            "Consumer.Compilation",
            new[] { source },
            new[] { new CompatibilityGen() },
            additionalSources: AppServiceCompatibilityGeneratorTests.BuildCompatibilityStubs(),
            additionalMetadataReferences: new[]
            {
                MetadataReference.CreateFromImage(referencedImage),
            });
}
