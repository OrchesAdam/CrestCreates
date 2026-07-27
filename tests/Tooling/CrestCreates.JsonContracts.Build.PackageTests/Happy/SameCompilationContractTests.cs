using CrestCreates.JsonContracts.Build.PackageTests.Infrastructure;
using FluentAssertions;

namespace CrestCreates.JsonContracts.Build.PackageTests.Happy;

public sealed class SameCompilationContractTests(JsonContractBuildFixture fixture) : JsonContractContractTestBase(fixture)
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

        public static class GeneratedMetadataConsumer
        {
            public static System.Text.Json.Serialization.Metadata.JsonTypeInfo<string> StringInfo
                => TestContext.Default.String;
        }
        """
    ];

    [Fact]
    public async Task Build_GeneratedAttributesParticipateInSameCompilation()
    {
        var project = await CreateRepositoryConsumerAsync(new ConsumerSpec("Repository", s_sources));

        var result = await BuildAsync(project);

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);
        ReadGeneratedSource(project).Should().Contain("JsonSerializable(typeof(global::System.String))");
    }

    [Fact]
    public async Task Build_StjGeneratorProducesJsonTypeInfoForGeneratedRoots()
    {
        var project = await CreateRepositoryConsumerAsync(new ConsumerSpec("Repository", s_sources));

        var result = await BuildAsync(project);

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);
        var assembly = Path.Combine(project.ProjectDirectory, "bin", "Debug", "net10.0", Path.GetFileNameWithoutExtension(project.ProjectFile) + ".dll");
        File.Exists(assembly).Should().BeTrue();
    }

    [Fact]
    public async Task Build_CleanCheckoutSucceedsOnFirstInvocation()
    {
        var project = await CreatePackageConsumerAsync(new ConsumerSpec("Package", s_sources));

        Directory.Exists(Path.Combine(project.ProjectDirectory, "obj")).Should().BeFalse();
        var result = await BuildAsync(project, "--no-cache", "--force");

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);
        File.Exists(Path.Combine(project.ProjectDirectory, "obj", "Debug", "net10.0", "CrestCreates.JsonContracts.g.cs")).Should().BeTrue();
    }
}
