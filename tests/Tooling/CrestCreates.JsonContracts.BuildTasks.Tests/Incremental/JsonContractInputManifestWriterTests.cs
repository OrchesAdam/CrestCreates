using CrestCreates.JsonContracts.BuildTasks.Incremental;
using FluentAssertions;
using System.Text.Json;

namespace CrestCreates.JsonContracts.BuildTasks.Tests.Incremental;

/// <summary>Case IDs: B13, B14, B15</summary>
public class JsonContractInputManifestWriterTests
{
    [Fact]
    public void WriteManifest_ProducesDeterministicJson()
    {
        var manifest = CreateTestManifest();
        var bytes1 = JsonContractInputManifestWriter.WriteManifest(manifest);
        var bytes2 = JsonContractInputManifestWriter.WriteManifest(manifest);
        bytes1.Should().Equal(bytes2);
    }

    [Fact]
    public void WriteManifest_RoundTripsAllProperties()
    {
        var manifest = CreateTestManifest();
        var bytes = JsonContractInputManifestWriter.WriteManifest(manifest);
        var json = JsonDocument.Parse(bytes);

        var root = json.RootElement;

        root.GetProperty("sourcePaths").GetArrayLength().Should().Be(2);
        root.GetProperty("referencePaths").GetArrayLength().Should().Be(2);
        root.GetProperty("langVersion").GetString().Should().Be("latest");
        root.GetProperty("defineConstants").GetString().Should().Be("DEBUG;TRACE");
        root.GetProperty("nullable").GetString().Should().Be("enable");
        root.GetProperty("allowUnsafeBlocks").GetBoolean().Should().BeFalse();
        root.GetProperty("implicitUsings").GetString().Should().Be("enable");
        root.GetProperty("allowedOutputRoot").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("temporaryDirectory").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("manifestAccessibility").GetString().Should().Be("Internal");
        root.GetProperty("targetFramework").GetString().Should().Be("net10.0");
        root.GetProperty("taskSemanticVersion").GetString().Should().Be("1.0.0");
        root.GetProperty("taskAssemblyIdentity").GetString().Should().Be("TestAssembly");
    }

    [Fact]
    public void WriteManifest_IsByteStable()
    {
        var manifest = CreateTestManifest();
        var bytes1 = JsonContractInputManifestWriter.WriteManifest(manifest);
        var bytes2 = JsonContractInputManifestWriter.WriteManifest(manifest);
        bytes1.Should().Equal(bytes2);
    }

    [Fact]
    public void WriteManifest_SortsSourcePathsOrdinal()
    {
        var manifest = CreateTestManifest();
        manifest.SourcePaths = ["z.cs", "a.cs", "m.cs"];
        var bytes = JsonContractInputManifestWriter.WriteManifest(manifest);
        var json = JsonDocument.Parse(bytes);

        var paths = json.RootElement.GetProperty("sourcePaths")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .ToList();

        paths.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void WriteManifest_SortsReferencePathsOrdinal()
    {
        var manifest = CreateTestManifest();
        manifest.ReferencePaths = ["z.dll", "a.dll"];
        var bytes = JsonContractInputManifestWriter.WriteManifest(manifest);
        var json = JsonDocument.Parse(bytes);

        var paths = json.RootElement.GetProperty("referencePaths")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .ToList();

        paths.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void WriteManifest_NormalizesPathSeparators()
    {
        var manifest = CreateTestManifest();
        manifest.SourcePaths = [@"path\to\source.cs"];
        var bytes = JsonContractInputManifestWriter.WriteManifest(manifest);
        var json = JsonDocument.Parse(bytes);

        var path = json.RootElement.GetProperty("sourcePaths")
            .EnumerateArray()
            .First()
            .GetString()!;

        path.Should().NotContain("\\");
    }

    [Fact]
    public void WriteManifest_SourceAdditionChangesBytes()
    {
        var manifest1 = CreateTestManifest();
        var manifest2 = CreateTestManifest();
        manifest2.SourcePaths = manifest2.SourcePaths.Concat(["extra.cs"]).ToList();

        var bytes1 = JsonContractInputManifestWriter.WriteManifest(manifest1);
        var bytes2 = JsonContractInputManifestWriter.WriteManifest(manifest2);

        bytes1.Should().NotEqual(bytes2);
    }

    [Fact]
    public void WriteManifest_SourceDeletionChangesBytes()
    {
        var manifest1 = CreateTestManifest();
        var manifest2 = CreateTestManifest();
        manifest2.SourcePaths = manifest2.SourcePaths.Skip(1).ToList();

        var bytes1 = JsonContractInputManifestWriter.WriteManifest(manifest1);
        var bytes2 = JsonContractInputManifestWriter.WriteManifest(manifest2);

        bytes1.Should().NotEqual(bytes2);
    }

    [Fact]
    public void WriteManifest_UnchangedInputIsByteStable()
    {
        var manifest = CreateTestManifest();
        var bytes1 = JsonContractInputManifestWriter.WriteManifest(manifest);
        var bytes2 = JsonContractInputManifestWriter.WriteManifest(manifest);
        bytes1.Should().Equal(bytes2);
    }

    private static JsonContractInputManifest CreateTestManifest() => new()
    {
        SourcePaths = ["src/A.cs", "src/B.cs"],
        ReferencePaths = ["ref/X.dll", "ref/Y.dll"],
        LangVersion = "latest",
        DefineConstants = "DEBUG;TRACE",
        Nullable = "enable",
        AllowUnsafeBlocks = false,
        ImplicitUsings = "enable",
        AllowedOutputRoot = "/tmp/allowed",
        TemporaryDirectory = "/tmp/allowed/tmp",
        ManifestAccessibility = "Internal",
        TargetFramework = "net10.0",
        TaskSemanticVersion = "1.0.0",
        TaskAssemblyIdentity = "TestAssembly",
    };
}
