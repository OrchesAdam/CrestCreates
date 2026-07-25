using CrestCreates.JsonContracts.BuildTasks.Diagnostics;
using FluentAssertions;
using FluentAssertions.Execution;

namespace CrestCreates.JsonContracts.BuildTasks.Tests.Infrastructure;

public static class JsonContractDiagnosticAssertions
{
    public static void ShouldHaveDiagnostic(
        IEnumerable<JsonContractDiagnostic> diagnostics,
        string expectedId,
        string? contextMetadataName = null,
        string? surfaceMetadataName = null)
    {
        var matching = diagnostics.Where(d => d.Id == expectedId).ToList();
        using var scope = new AssertionScope();
        matching.Should().NotBeEmpty($"diagnostic '{expectedId}' should be present");

        if (contextMetadataName is not null)
        {
            matching.Should().Contain(d => d.ContextMetadataName == contextMetadataName,
                $"diagnostic '{expectedId}' should reference context '{contextMetadataName}'");
        }

        if (surfaceMetadataName is not null)
        {
            matching.Should().Contain(d => d.SurfaceMetadataName == surfaceMetadataName,
                $"diagnostic '{expectedId}' should reference surface '{surfaceMetadataName}'");
        }
    }

    public static void ShouldNotHaveDiagnostic(
        IEnumerable<JsonContractDiagnostic> diagnostics,
        string unexpectedId)
    {
        diagnostics.Should().NotContain(d => d.Id == unexpectedId,
            $"diagnostic '{unexpectedId}' should not be present");
    }
}
