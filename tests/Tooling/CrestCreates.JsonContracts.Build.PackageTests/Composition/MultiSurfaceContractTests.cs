using CrestCreates.JsonContracts.Build.PackageTests.Infrastructure;
using FluentAssertions;

namespace CrestCreates.JsonContracts.Build.PackageTests.Composition;

public class MultiSurfaceContractTests(JsonContractBuildFixture fixture) : JsonContractContractTestBase(fixture)
{
    private static readonly string s_context1 =
        """
        using System.Text.Json;
        using System.Text.Json.Serialization;
        using CrestCreates.Core.Abstractions.Serialization;

        [JsonContractSurface(typeof(IServiceA))]
        public partial class ContextA : JsonSerializerContext { }
        """;

    private static readonly string s_context2 =
        """
        using System.Text.Json;
        using System.Text.Json.Serialization;
        using CrestCreates.Core.Abstractions.Serialization;

        [JsonContractSurface(typeof(IServiceB))]
        public partial class ContextB : JsonSerializerContext { }
        """;

    private static readonly string s_interfaces = """
        using System.Threading;
        using System.Threading.Tasks;

        public interface IServiceA
        {
            Task<string> GetAAsync(string id, CancellationToken ct = default);
        }

        public interface IServiceB
        {
            Task<int> CountBAsync(CancellationToken ct = default);
        }
        """;

    [Fact]
    public async Task Build_MultipleContextsAreIsolatedAndSorted()
    {
        var spec = new ConsumerSpec(Transport: "Repository", SourceFiles: [s_context1, s_context2, s_interfaces]);
        var project = await CreateRepositoryConsumerAsync(spec);
        var result = await BuildAsync(project);

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);

        var generated = ReadGeneratedSource(project);
        generated.Should().Contain("ContextA");
        generated.Should().Contain("ContextB");
        var indexA = generated.IndexOf("ContextA", StringComparison.Ordinal);
        var indexB = generated.IndexOf("ContextB", StringComparison.Ordinal);
        indexA.Should().BeLessThan(indexB, "contexts should be sorted by name");
    }
}

public class ImportCompositionContractTests(JsonContractBuildFixture fixture) : JsonContractContractTestBase(fixture)
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

    [Fact]
    public async Task Build_DuplicateImportCannotRunGenerationOrAddCompileTwice()
    {
        var spec = new ConsumerSpec(
            Transport: "Repository",
            SourceFiles: [s_surfaceContext, s_serviceInterface],
            DuplicateImport: true);

        var project = await CreateRepositoryConsumerAsync(spec);
        var result = await BuildAsync(project);

        if (result.ExitCode != 0)
            Assert.Fail($"Build failed:\nSTDOUT: {result.StandardOutput}\nSTDERR: {result.StandardError}");

        var generatedPath = Path.Combine(project.ProjectDirectory, "obj", "Debug", "net10.0", "CrestCreates.JsonContracts.g.cs");
        if (!File.Exists(generatedPath))
            return;

        var generated = File.ReadAllText(generatedPath);
        var count = generated.Split("JsonSerializable").Length - 1;
        count.Should().Be(1, "duplicate import should not cause double generation");
    }
}
