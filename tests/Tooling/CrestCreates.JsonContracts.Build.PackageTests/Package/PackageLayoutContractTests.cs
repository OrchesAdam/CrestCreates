using System.IO.Compression;
using CrestCreates.JsonContracts.Build.PackageTests.Infrastructure;
using FluentAssertions;

namespace CrestCreates.JsonContracts.Build.PackageTests.Package;

public class PackageLayoutContractTests(JsonContractBuildFixture fixture) : JsonContractContractTestBase(fixture)
{
    [Fact]
    public async Task Pack_PackageLayoutContainsOnlyBuildAndToolsAndTasks()
    {
        await Fixture.PackAsync();

        var nupkg = Fixture.PackagePath;
        nupkg.Should().NotBeNullOrEmpty();

        var extractDir = Path.Combine(Path.GetTempPath(), $"nupkg_layout_{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractDir);

        try
        {
            ZipFile.ExtractToDirectory(nupkg, extractDir);

            var files = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(extractDir, f).Replace('\\', '/'))
                .Where(f => !f.StartsWith("_rels/") && !f.StartsWith("package/") && f != "[Content_Types].xml" && !f.EndsWith(".psmdcp") && !f.EndsWith(".nuspec"))
                .ToList();

            foreach (var f in files)
            {
                var allowed = f.StartsWith("build/") || f.StartsWith("tasks/") || f.StartsWith("tools/");
                allowed.Should().BeTrue($"file '{f}' should be under build/, tasks/, or tools/");
            }

            var buildFiles = files.Where(f => f.StartsWith("build/")).ToList();
            buildFiles.Should().Contain("build/CrestCreates.JsonContracts.Build.props");
            buildFiles.Should().Contain("build/CrestCreates.JsonContracts.Build.targets");
            buildFiles.Should().Contain("build/CrestCreates.JsonContracts.Build.Common.props");
            buildFiles.Should().Contain("build/CrestCreates.JsonContracts.Build.Common.targets");
            buildFiles.Should().Contain("build/CrestCreates.JsonContracts.Build.Repository.props");
            buildFiles.Should().Contain("build/CrestCreates.JsonContracts.Build.Repository.targets");

            var taskDlls = files.Where(f => f.StartsWith("tasks/") && f.EndsWith(".dll")).ToList();
            taskDlls.Should().ContainSingle("only one task DLL should be in tasks/");
            taskDlls[0].Should().Be("tasks/net10.0/CrestCreates.JsonContracts.BuildTasks.dll");

            var toolDlls = files.Where(f => f.StartsWith("tools/") && f.EndsWith(".dll")).ToList();
            toolDlls.Should().Contain("tools/net10.0/CrestCreates.JsonContracts.Tool.dll");

            var libDlls = files.Where(f => f.StartsWith("lib/")).ToList();
            libDlls.Should().BeEmpty("no lib/ content — this is a build-time-only package");
        }
        finally
        {
            try { Directory.Delete(extractDir, true); } catch { }
        }
    }

    [Fact]
    public async Task Pack_NoMicrosoftBuildDllsInPackage()
    {
        await Fixture.PackAsync();

        var extractDir = Path.Combine(Path.GetTempPath(), $"nupkg_nobuild_{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractDir);

        try
        {
            ZipFile.ExtractToDirectory(Fixture.PackagePath, extractDir);

            var msbuildDlls = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories)
                .Where(f => Path.GetFileName(f).StartsWith("Microsoft.Build."))
                .ToList();

            msbuildDlls.Should().BeEmpty("Microsoft.Build.* DLLs must not be in the package");
        }
        finally
        {
            try { Directory.Delete(extractDir, true); } catch { }
        }
    }
}

public class LocalFeedConsumerContractTests(JsonContractBuildFixture fixture) : JsonContractContractTestBase(fixture)
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
        using System.Threading;
        using System.Threading.Tasks;

        public interface ITestService
        {
            Task<string> GetAsync(string id, CancellationToken ct = default);
        }
        """;

    [Fact(Skip = "Requires CrestCreates.Core.Abstractions NuGet package in local feed")]
    public async Task Pack_LocalFeedConsumerGetsTaskAndTargetsOnly()
    {
        var spec = new ConsumerSpec(Transport: "Package", SourceFiles: [s_surfaceContext, s_serviceInterface]);
        var project = await CreatePackageConsumerAsync(spec);
        var result = await BuildAsync(project);

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);

        var generated = ReadGeneratedSource(project);
        generated.Should().Contain("JsonSerializable");

        AssertNoTaskAssemblies(project.ProjectDirectory);
    }
}

public class PackageLeakageContractTests(JsonContractBuildFixture fixture) : JsonContractContractTestBase(fixture)
{
    [Fact]
    public async Task Build_TaskDependenciesDoNotLeakToRuntimeOutput()
    {
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
            ]);

        var project = await CreateRepositoryConsumerAsync(spec);
        var result = await BuildAsync(project);
        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);

        var binDir = Path.Combine(project.ProjectDirectory, "bin", "Debug", "net10.0");
        if (Directory.Exists(binDir))
        {
            var leakedDlls = Directory.GetFiles(binDir, "*.dll")
                .Where(f => Path.GetFileName(f).Contains("CrestCreates.JsonContracts.BuildTasks")
                         || Path.GetFileName(f).Contains("CrestCreates.JsonContracts.Tool")
                         || Path.GetFileName(f).Contains("Microsoft.CodeAnalysis"))
                .ToList();

            leakedDlls.Should().BeEmpty("BuildTasks/Tool/Roslyn DLLs must not leak to runtime output");
        }
    }
}
