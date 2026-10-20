using System;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests;

/// <summary>
/// #129 S4 L01: the default analyzer must not carry the legacy generator; the opt-in Legacy analyzer
/// must contain only the still-supported legacy surface (the controller generators are archived).
/// </summary>
public class LegacyAnalyzerIsolationTests
{
    private const string LegacyNamespace = "CrestCreates.CodeGenerator.DynamicApiGenerator.";
    private const string CompatibilityNamespace = "CrestCreates.CodeGenerator.AppServiceCompatibilityGenerator.";
    private const string ControllerNamespace = "CrestCreates.CodeGenerator.ControllerGenerator.";

    private static readonly System.Reflection.Assembly DefaultAnalyzer =
        typeof(CrestCreates.CodeGenerator.ServiceGenerator.ServiceSourceGenerator).Assembly;

    private static readonly System.Reflection.Assembly LegacyAnalyzer =
        typeof(CrestCreates.CodeGenerator.DynamicApiGenerator.DynamicApiAotSourceGenerator).Assembly;

    [Fact]
    public void Default_analyzer_does_not_contain_legacy_generators()
    {
        DefaultAnalyzer.GetName().Name.Should().Be("CrestCreates.CodeGenerator");
        foreach (var generator in new[]
                 {
                     LegacyNamespace + "DynamicApiAotSourceGenerator",
                     CompatibilityNamespace + "AppServiceCompatibilityGenerator",
                     ControllerNamespace + "ControllerSourceGenerator",
                     ControllerNamespace + "CrudControllerSourceGenerator",
                 })
        {
            DefaultAnalyzer.GetType(generator, throwOnError: false).Should().BeNull(
                $"{generator} is legacy-only and must live in CrestCreates.CodeGenerator.Legacy");
        }

        // The convention analyzer/models are shared symbol utilities (the formal capability endpoint
        // generator uses them); they are not legacy generators and may stay in the default assembly.
        DefaultAnalyzer.GetType(LegacyNamespace + "DynamicApiConventionAnalyzer", throwOnError: false)
            .Should().NotBeNull();
    }

    [Fact]
    public void Legacy_analyzer_contains_only_the_explicit_legacy_generators()
    {
        LegacyAnalyzer.GetName().Name.Should().Be("CrestCreates.CodeGenerator.Legacy");
        LegacyAnalyzer.GetType(LegacyNamespace + "DynamicApiAotSourceGenerator", throwOnError: false)
            .Should().NotBeNull("the [CrestService] legacy Minimal API generator is still consumed by explicit hosts");
        LegacyAnalyzer.GetType(CompatibilityNamespace + "AppServiceCompatibilityGenerator", throwOnError: false)
            .Should().NotBeNull("the static compatibility generator is legacy-only and opt-in");
        LegacyAnalyzer.GetType(ControllerNamespace + "ControllerSourceGenerator", throwOnError: false)
            .Should().BeNull("the controller generator was archived in S4");
        LegacyAnalyzer.GetType(ControllerNamespace + "CrudControllerSourceGenerator", throwOnError: false)
            .Should().BeNull("the CRUD controller generator was archived in S4");
    }
}
