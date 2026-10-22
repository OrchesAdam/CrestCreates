using System;
using System.Linq;
using CrestCreates.DynamicApi;
using CrestCreates.Web;
using CrestCreates.Web.Legacy;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

/// <summary>
/// #129 S4 L01: the formal runtime and Web assemblies must not carry or reference the legacy surface;
/// only the explicit Legacy packages may.
/// </summary>
public class LegacyIsolationBoundaryTests
{
    [Fact]
    public void Formal_runtime_does_not_carry_legacy_types()
    {
        var formal = typeof(DynamicApiResponse).Assembly;
        formal.GetName().Name.Should().Be("CrestCreates.DynamicApi");

        foreach (var legacyType in new[]
                 {
                     "CrestCreates.DynamicApi.DynamicApiGeneratedRuntime",
                     "CrestCreates.DynamicApi.DynamicApiGeneratedRegistryStore",
                     "CrestCreates.DynamicApi.DynamicApiRegistry",
                     "CrestCreates.DynamicApi.IDynamicApiGeneratedProvider",
                     "CrestCreates.DynamicApi.DynamicApiOptions",
                     "CrestCreates.DynamicApi.DynamicApiRouteConvention",
                     "CrestCreates.DynamicApi.CompatibilityBodyReader",
                 })
        {
            formal.GetType(legacyType, throwOnError: false).Should().BeNull(
                $"{legacyType} belongs to the explicit Legacy runtime; the formal package must not ship it");
        }
    }

    [Fact]
    public void Legacy_runtime_carries_the_moved_types()
    {
        var legacy = typeof(DynamicApiGeneratedRuntime).Assembly;
        legacy.GetName().Name.Should().Be("CrestCreates.DynamicApi.Legacy");
        legacy.GetType("CrestCreates.DynamicApi.DynamicApiGeneratedRegistryStore", throwOnError: false)
            .Should().NotBeNull();
    }

    [Fact]
    public void Formal_web_does_not_reference_legacy_packages()
    {
        var references = typeof(CrestWebOptions).Assembly.GetReferencedAssemblies()
            .Select(name => name.Name)
            .ToArray();

        references.Should().NotContain("CrestCreates.DynamicApi.Legacy");
        references.Should().NotContain("CrestCreates.Web.Legacy");
    }

    [Fact]
    public void Legacy_activation_is_explicit_and_allowlisted()
    {
        // The formal Web options no longer expose a generated-API preset; legacy activation is a
        // separate package whose Map validates the exact per-service/action allowlist.
        typeof(CrestWebOptions).GetProperty("GeneratedApi").Should().BeNull(
            "the default preset must not activate the legacy chain");

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Action addWithoutAllowlist = () => services.AddLegacyGeneratedApi(_ => { });
        addWithoutAllowlist.Should().Throw<InvalidOperationException>().WithMessage("*CCHTTP007*");
    }
}
