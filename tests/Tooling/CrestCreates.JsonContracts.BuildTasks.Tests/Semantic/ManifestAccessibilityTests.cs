using CrestCreates.JsonContracts.BuildTasks.Model;
using CrestCreates.JsonContracts.BuildTasks.Semantic;
using FluentAssertions;

namespace CrestCreates.JsonContracts.BuildTasks.Tests.Semantic;

/// <summary>Case IDs: B17, B18, F13</summary>
public class ManifestAccessibilityTests
{
    [Fact]
    public void Parse_InternalManifestAccessibility()
    {
        JsonContractSurfaceModelBuilder.ParseManifestAccessibility("Internal")
            .Should().Be(JsonContractManifestAccessibility.Internal);
    }

    [Fact]
    public void Parse_PublicManifestAccessibility()
    {
        JsonContractSurfaceModelBuilder.ParseManifestAccessibility("Public")
            .Should().Be(JsonContractManifestAccessibility.Public);
    }

    [Fact]
    public void Parse_NullOrDefaultReturnsInternal()
    {
        JsonContractSurfaceModelBuilder.ParseManifestAccessibility(null)
            .Should().Be(JsonContractManifestAccessibility.Internal);
        JsonContractSurfaceModelBuilder.ParseManifestAccessibility("")
            .Should().Be(JsonContractManifestAccessibility.Internal);
        JsonContractSurfaceModelBuilder.ParseManifestAccessibility("   ")
            .Should().Be(JsonContractManifestAccessibility.Internal);
    }

    [Fact]
    public void Fail_InvalidManifestAccessibility()
    {
        var act = () => JsonContractSurfaceModelBuilder.ParseManifestAccessibility("protected");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Invalid manifest accessibility*protected*");
    }

    [Fact]
    public void Fail_LowercaseVariantRejected()
    {
        var act = () => JsonContractSurfaceModelBuilder.ParseManifestAccessibility("public");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Parse_TrimsWhitespace()
    {
        JsonContractSurfaceModelBuilder.ParseManifestAccessibility("  Internal  ")
            .Should().Be(JsonContractManifestAccessibility.Internal);
    }
}
