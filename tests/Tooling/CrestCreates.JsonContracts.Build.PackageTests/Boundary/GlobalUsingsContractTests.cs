using CrestCreates.JsonContracts.Build.PackageTests.Infrastructure;
using FluentAssertions;

namespace CrestCreates.JsonContracts.Build.PackageTests.Boundary;

public sealed class GlobalUsingsContractTests(JsonContractBuildFixture fixture) : JsonContractContractTestBase(fixture)
{
    [Fact]
    public async Task Build_ImplicitUsingsOnlySurfaceBindsAfterGenerateGlobalUsings()
    {
        var project = await CreateRepositoryConsumerAsync(new ConsumerSpec(
            "Repository",
            [
                """
                using System.Text.Json.Serialization;
                using CrestCreates.Core.Abstractions.Serialization;

                [JsonContractSurface(typeof(ITestService))]
                public partial class TestContext : JsonSerializerContext { }
                """,
                """
                public interface ITestService
                {
                    Task<IReadOnlyList<string>> GetAsync(CancellationToken cancellationToken = default);
                }
                """
            ],
            ImplicitUsings: "enable"));

        var result = await BuildAsync(project);

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);
        var manifest = ReadInputManifest(project);
        manifest.Should().Contain("GlobalUsings.g.cs");
        ReadGeneratedSource(project).Should().Contain("IReadOnlyList");
    }
}

public sealed class ManifestAccessibilityContractTests(JsonContractBuildFixture fixture) : JsonContractContractTestBase(fixture)
{
    private static readonly string[] s_sources =
    [
        """
        using System.Text.Json.Serialization;
        using CrestCreates.Core.Abstractions.Serialization;

        [JsonContractSurface(typeof(ITestService))]
        public partial class TestContext : JsonSerializerContext { }
        """,
        """
        public interface ITestService
        {
            Task<string> GetAsync(CancellationToken cancellationToken = default);
        }
        """
    ];

    [Fact]
    public async Task Build_InternalManifestAccessibility()
    {
        var project = await CreateRepositoryConsumerAsync(new ConsumerSpec("Repository", s_sources));

        var result = await BuildAsync(project);

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);
        ReadGeneratedSource(project).Should().Contain("internal static class TestContextRootManifest");
    }

    [Fact]
    public async Task Build_InternalManifestRemainsAssemblyScoped()
    {
        var producer = await CreateRepositoryConsumerAsync(new ConsumerSpec("Repository", s_sources));
        (await BuildAsync(producer)).ExitCode.Should().Be(0);
        var consumer = await CreateManifestConsumerAsync(producer, expectPublic: false);

        var result = await DotNetProcess.RunAsync(
            consumer,
            "build --disable-build-servers",
            timeout: TimeSpan.FromMinutes(2));

        result.ExitCode.Should().NotBe(0);
        (result.StandardOutput + result.StandardError).Should().Contain("inaccessible");
    }

    [Fact]
    public async Task Build_PublicManifestAccessibility()
    {
        var project = await CreateRepositoryConsumerAsync(new ConsumerSpec(
            "Repository", s_sources, ManifestAccessibility: "Public"));

        var result = await BuildAsync(project);

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);
        ReadGeneratedSource(project).Should().Contain("public static class TestContextRootManifest");
    }

    [Fact]
    public async Task Build_PublicManifestSetsAreImmutable()
    {
        var producer = await CreateRepositoryConsumerAsync(new ConsumerSpec(
            "Repository", s_sources, ManifestAccessibility: "Public"));
        (await BuildAsync(producer)).ExitCode.Should().Be(0);
        var consumer = await CreateManifestConsumerAsync(producer, expectPublic: true);

        var result = await DotNetProcess.RunAsync(
            consumer,
            "run --disable-build-servers",
            timeout: TimeSpan.FromMinutes(2));

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);
        result.StandardOutput.Should().Contain("PUBLIC_MANIFEST_IMMUTABLE_OK");
    }

    [Fact]
    public async Task Build_PublicManifestIsConsumableFromSeparateAssembly()
    {
        var producer = await CreateRepositoryConsumerAsync(new ConsumerSpec(
            "Repository", s_sources, ManifestAccessibility: "Public"));
        (await BuildAsync(producer)).ExitCode.Should().Be(0);
        var consumer = await CreateManifestConsumerAsync(producer, expectPublic: true);

        var result = await DotNetProcess.RunAsync(
            consumer,
            "build --disable-build-servers",
            timeout: TimeSpan.FromMinutes(2));

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);
    }

    private async Task<string> CreateManifestConsumerAsync(ProjectLayout producer, bool expectPublic)
    {
        var directory = Path.Combine(Fixture.ProjectsDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var projectReference = producer.ProjectFile.Replace("&", "&amp;", StringComparison.Ordinal);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "Consumer.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup><ProjectReference Include="{projectReference}" /></ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "Program.cs"),
            expectPublic
                ? """
                  var roots = TestContext.TestContextRootManifest.SurfaceRootTypes;
                  if (roots.Count == 0) return 1;
                  if (roots is ISet<Type> mutable)
                  {
                      try
                      {
                          mutable.Add(typeof(ManifestMutationProbe));
                          return 2;
                      }
                      catch (NotSupportedException)
                      {
                      }
                  }
                  Console.WriteLine("PUBLIC_MANIFEST_IMMUTABLE_OK");
                  return 0;
                  internal sealed class ManifestMutationProbe { }
                  """
                : """
                  _ = TestContext.TestContextRootManifest.SurfaceRootTypes;
                  return 0;
                  """);
        return directory;
    }
}
