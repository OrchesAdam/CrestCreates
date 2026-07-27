using CrestCreates.JsonContracts.Build.PackageTests.Infrastructure;
using FluentAssertions;

namespace CrestCreates.JsonContracts.Build.PackageTests.Failure;

public class TransportConflictContractTests(JsonContractBuildFixture fixture) : JsonContractContractTestBase(fixture)
{
    [Fact]
    public async Task Build_RepositoryAndPackageTransportConflictFailsBeforeGeneration()
    {
        var taskProjectDir = Path.Combine(Fixture.RepositoryRoot, "src", "Tooling", "CrestCreates.JsonContracts.BuildTasks");
        var buildDir = Path.Combine(taskProjectDir, "build");

        var spec = new ConsumerSpec(
            Transport: "Repository",
            SourceFiles:
            [
                """
                using System.Text.Json;
                using System.Text.Json.Serialization;
                using CrestCreates.Core.Abstractions.Serialization;

                [JsonContractSurface(typeof(ITestService))]
                public partial class TestContext : JsonSerializerContext { }
                """,
                """
                using System.Threading;
                using System.Threading.Tasks;

                public interface ITestService
                {
                    Task<string> GetAsync(string id, CancellationToken ct = default);
                }
                """
            ],
            EarlierTarget: Path.Combine(buildDir, "CrestCreates.JsonContracts.Build.props"));

        var project = await CreateRepositoryConsumerAsync(spec);
        var result = await BuildAsync(project);

        result.ExitCode.Should().NotBe(0, "conflicting transports should fail the build");
        (result.StandardOutput + result.StandardError).Should().Contain("CJC014");
    }
}

public sealed class OutputPathBoundaryContractTests(JsonContractBuildFixture fixture) : JsonContractContractTestBase(fixture)
{
    private static readonly string[] s_sources =
    [
        """
        using System.Text.Json.Serialization;
        using CrestCreates.Core.Abstractions.Serialization;
        [JsonContractSurface(typeof(IService))]
        public partial class TestContext : JsonSerializerContext { }
        """,
        """
        public interface IService { Task<string> GetAsync(CancellationToken cancellationToken); }
        """
    ];

    [Fact]
    public Task Fail_GeneratedPathOutsideIntermediateOutputPath() =>
        AssertRejectedAsync(new ConsumerSpec("Repository", s_sources, GeneratedFile: "../escaped.g.cs"), "escaped.g.cs");

    [Fact]
    public Task Fail_InputManifestOutsideIntermediateOutputPath() =>
        AssertRejectedAsync(new ConsumerSpec("Repository", s_sources, InputManifest: "../escaped.inputs.json"), "escaped.inputs.json");

    [Fact]
    public Task Fail_GenerationStampOutsideIntermediateOutputPath() =>
        AssertRejectedAsync(new ConsumerSpec("Repository", s_sources, GenerationStamp: "../escaped.stamp"), "escaped.stamp");

    [Fact]
    public Task Fail_TemporaryDirectoryOutsideIntermediateOutputPath() =>
        AssertRejectedAsync(new ConsumerSpec("Repository", s_sources, TemporaryDirectory: "../escaped.tmp"), "escaped.tmp");

    [Fact]
    public void Target_PathValidationRejectsRootedRelativeForAllOwnedPaths()
    {
        var targets = File.ReadAllText(Fixture.CommonTargetsPath);

        foreach (var relativeProperty in new[]
        {
            "_CrestCreatesJsonContractGeneratedRelative",
            "_CrestCreatesJsonContractManifestRelative",
            "_CrestCreatesJsonContractStampRelative",
            "_CrestCreatesJsonContractTemporaryRelative",
        })
        {
            targets.Should().Contain(
                $"$([System.IO.Path]::IsPathRooted('$({relativeProperty})'))",
                $"{relativeProperty} must reject Windows cross-volume rooted relative results before side effects");
        }
    }

    private async Task AssertRejectedAsync(ConsumerSpec spec, string escapedName)
    {
        var project = await CreateRepositoryConsumerAsync(spec);

        var result = await BuildAsync(project);

        result.ExitCode.Should().NotBe(0);
        (result.StandardOutput + result.StandardError).Should().Contain("CJC012");
        File.Exists(Path.Combine(Path.GetDirectoryName(project.ProjectDirectory)!, escapedName)).Should().BeFalse();
    }
}
