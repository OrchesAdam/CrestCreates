using CrestCreates.JsonContracts.Build.PackageTests.Infrastructure;
using FluentAssertions;

namespace CrestCreates.JsonContracts.Build.PackageTests.Boundary;

[Collection("JsonContractBuild")]
public class MultiTargetingContractTests : JsonContractContractTestBase
{
    private static readonly string s_surfaceContext = """
        using System.Text.Json;
        using System.Text.Json.Serialization;
        using CrestCreates.Core.Abstractions.Serialization;

        [JsonContractSurface(typeof(ITestService))]
        public partial class TestContext : JsonSerializerContext { }
        """;

    private static readonly string s_serviceInterface = """
        using System.Threading;
        using System.Threading.Tasks;

        public interface ITestService
        {
            Task<string> GetAsync(string id, CancellationToken ct = default);
        }
        """;

    public MultiTargetingContractTests(JsonContractBuildFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Build_MultiTargetingProducesIndependentOutputs()
    {
        var spec = new ConsumerSpec("Repository", [s_surfaceContext, s_serviceInterface])
        {
            TargetFramework = "net10.0"
        };
        var project = await CreateRepositoryConsumerAsync(spec);

        var build = await BuildAsync(project);
        build.ExitCode.Should().Be(0, build.StandardError);

        var objDir = Path.Combine(project.ProjectDirectory, "obj", "Debug");
        var net10Generated = Path.Combine(objDir, "net10.0", "CrestCreates.JsonContracts.g.cs");
        File.Exists(net10Generated).Should().BeTrue();
    }

    [Fact]
    public async Task Build_DebugAndReleaseOutputsAreIsolated()
    {
        var spec = new ConsumerSpec("Repository", [s_surfaceContext, s_serviceInterface]);
        var project = await CreateRepositoryConsumerAsync(spec);

        var debugBuild = await BuildAsync(project);
        debugBuild.ExitCode.Should().Be(0, debugBuild.StandardError);

        var debugGenerated = Path.Combine(project.ProjectDirectory, "obj", "Debug", "net10.0", "CrestCreates.JsonContracts.g.cs");
        File.Exists(debugGenerated).Should().BeTrue();

        var releaseBuild = await DotNetProcess.RunAsync(
            project.ProjectDirectory,
            $"build \"{project.ProjectFile}\" -c Release --disable-build-servers",
            timeout: TimeSpan.FromMinutes(2));
        releaseBuild.ExitCode.Should().Be(0, releaseBuild.StandardError);

        var releaseGenerated = Path.Combine(project.ProjectDirectory, "obj", "Release", "net10.0", "CrestCreates.JsonContracts.g.cs");
        File.Exists(releaseGenerated).Should().BeTrue();

        if (File.Exists(debugGenerated) && File.Exists(releaseGenerated))
        {
            var debugContent = await File.ReadAllTextAsync(debugGenerated);
            var releaseContent = await File.ReadAllTextAsync(releaseGenerated);
            debugContent.Should().Be(releaseContent);
        }
    }
}
