using CrestCreates.JsonContracts.Build.PackageTests.Infrastructure;
using FluentAssertions;

namespace CrestCreates.JsonContracts.Build.PackageTests.Boundary;

[Collection("JsonContractBuild")]
public class IncrementalContractTests : JsonContractContractTestBase
{
    private static readonly string s_surfaceContext =
        """
        using System.Text.Json;
        using System.Text.Json.Serialization;
        using CrestCreates.Core.Abstractions.Serialization;

        [JsonContractSurface(typeof(ITestService))]
        public partial class TestContext : JsonSerializerContext { }
        """;

    private static readonly string s_serviceInterface = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;

        public interface ITestService
        {
            Task<string> GetAsync(string id, CancellationToken ct = default);
        }
        """;

    private static readonly string s_serviceInterfaceWithExtra = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;

        public interface ITestService
        {
            Task<string> GetAsync(string id, CancellationToken ct = default);
            Task<int> CountAsync(CancellationToken ct = default);
        }
        """;

    public IncrementalContractTests(JsonContractBuildFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Build_AddMethodThenRebuildUpdatesManifest()
    {
        var spec = new ConsumerSpec("Repository", [s_surfaceContext, s_serviceInterface]);
        var project = await CreateRepositoryConsumerAsync(spec);

        var build1 = await BuildAsync(project);
        build1.ExitCode.Should().Be(0, build1.StandardError);

        var manifest1 = ReadInputManifest(project);
        manifest1.Should().Contain("Source0");
        manifest1.Should().Contain("\"implicitUsings\":\"enable\"");
        manifest1.Should().Contain("\"taskSemanticVersion\":\"1.0.0\"");
        manifest1.Should().Contain("\"taskAssemblyIdentity\":");

        var sourceFile = Path.Combine(project.ProjectDirectory, "Source1.cs");
        await File.WriteAllTextAsync(sourceFile, s_serviceInterfaceWithExtra);

        var build2 = await RebuildAsync(project);
        build2.ExitCode.Should().Be(0, build2.StandardError);

        var source2 = ReadGeneratedSource(project);
        source2.Should().Contain("Int32");
    }

    [Fact]
    public async Task Build_RemoveMethodThenRebuildRemovesRoot()
    {
        var spec = new ConsumerSpec("Repository", [s_surfaceContext, s_serviceInterfaceWithExtra]);
        var project = await CreateRepositoryConsumerAsync(spec);

        var build1 = await BuildAsync(project);
        build1.ExitCode.Should().Be(0, build1.StandardError);

        var source1 = ReadGeneratedSource(project);
        source1.Should().Contain("Int32");

        var sourceFile = Path.Combine(project.ProjectDirectory, "Source1.cs");
        await File.WriteAllTextAsync(sourceFile, s_serviceInterface);

        var build2 = await RebuildAsync(project);
        build2.ExitCode.Should().Be(0, build2.StandardError);

        var source2 = ReadGeneratedSource(project);
        source2.Should().NotContain("Int32");
    }

    [Fact]
    public async Task Build_SourceDeletionInvalidatesGeneration()
    {
        var spec = new ConsumerSpec(
            "Repository",
            [
                s_surfaceContext,
                """
                using System.Threading;
                using System.Threading.Tasks;
                public partial interface ITestService
                {
                    Task<string> GetAsync(string id, CancellationToken ct = default);
                }
                """,
                """
                using System.Threading;
                using System.Threading.Tasks;
                public partial interface ITestService
                {
                    Task<int> CountAsync(CancellationToken ct = default);
                }
                """
            ]);
        var project = await CreateRepositoryConsumerAsync(spec);

        var build1 = await BuildAsync(project);
        build1.ExitCode.Should().Be(0, build1.StandardError);

        var source1 = ReadGeneratedSource(project);
        source1.Should().Contain("Int32");

        var sourceFile = Path.Combine(project.ProjectDirectory, "Source2.cs");
        File.Delete(sourceFile);
        var projectXml = await File.ReadAllTextAsync(project.ProjectFile);
        projectXml = projectXml.Replace("    <Compile Include=\"Source2.cs\" />" + Environment.NewLine, string.Empty, StringComparison.Ordinal);
        await File.WriteAllTextAsync(project.ProjectFile, projectXml);

        var build2 = await BuildAsync(project);
        build2.ExitCode.Should().Be(0, build2.StandardError);

        var source2 = ReadGeneratedSource(project);
        source2.Should().NotContain("Int32");
    }

    [Fact]
    public async Task Build_UnchangedSemanticOutputDoesNotRewriteTimestamp()
    {
        var spec = new ConsumerSpec("Repository", [s_surfaceContext, s_serviceInterface]);
        var project = await CreateRepositoryConsumerAsync(spec);

        var build1 = await BuildAsync(project);
        build1.ExitCode.Should().Be(0, build1.StandardError);

        var snapshot1 = SnapshotGeneratedFile(project);

        var build2 = await BuildAsync(project);
        build2.ExitCode.Should().Be(0, build2.StandardError);

        var snapshot2 = SnapshotGeneratedFile(project);
        snapshot2.Content.Should().Equal(snapshot1.Content);
        snapshot2.LastWriteTimeUtc.Should().Be(snapshot1.LastWriteTimeUtc);
    }

    [Fact]
    public async Task Build_MissingGeneratedSourceInvalidatesStamp()
    {
        var spec = new ConsumerSpec("Repository", [s_surfaceContext, s_serviceInterface]);
        var project = await CreateRepositoryConsumerAsync(spec);

        var build1 = await BuildAsync(project);
        build1.ExitCode.Should().Be(0, build1.StandardError);

        var generatedPath = Path.Combine(project.ProjectDirectory, "obj", "Debug", "net10.0", "CrestCreates.JsonContracts.g.cs");
        File.Delete(generatedPath);

        var build2 = await BuildAsync(project);
        build2.ExitCode.Should().Be(0, build2.StandardError);

        File.Exists(generatedPath).Should().BeTrue();
    }

    [Fact]
    public async Task Build_DesignTimeReusesExistingGeneratedFile()
    {
        var spec = new ConsumerSpec("Repository", [s_surfaceContext, s_serviceInterface]);
        var project = await CreateRepositoryConsumerAsync(spec);

        var build1 = await BuildAsync(project);
        build1.ExitCode.Should().Be(0, build1.StandardError);

        var generatedContent = ReadGeneratedSource(project);

        var envVars = new Dictionary<string, string>
        {
            ["DesignTimeBuild"] = "true",
            ["BuildingInsideVisualStudio"] = "true",
            ["SkipCompilerExecution"] = "true",
        };

        var dtResult = await DotNetProcess.RunAsync(
            project.ProjectDirectory,
            $"msbuild \"{project.ProjectFile}\" /p:DesignTimeBuild=true /p:BuildingInsideVisualStudio=true /p:SkipCompilerExecution=true /t:CoreCompile",
            timeout: TimeSpan.FromMinutes(1));

        var generatedPath = Path.Combine(project.ProjectDirectory, "obj", "Debug", "net10.0", "CrestCreates.JsonContracts.g.cs");
        if (File.Exists(generatedPath))
        {
            var dtContent = File.ReadAllText(generatedPath);
            dtContent.Should().Be(generatedContent);
        }
    }
}
