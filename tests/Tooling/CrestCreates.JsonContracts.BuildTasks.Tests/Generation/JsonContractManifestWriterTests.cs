using CrestCreates.JsonContracts.BuildTasks.Generation;
using CrestCreates.JsonContracts.BuildTasks.Model;
using CrestCreates.JsonContracts.BuildTasks.Tests.Infrastructure;
using FluentAssertions;

namespace CrestCreates.JsonContracts.BuildTasks.Tests.Generation;

public sealed class JsonContractManifestWriterTests : JsonContractCompilationTestBase
{
    [Fact]
    public void WriteManifest_ContainsSurfaceRoots()
    {
        Write().Should().Contain("typeof(global::SurfaceDto)");
    }

    [Fact]
    public void WriteManifest_ContainsExplicitRoots()
    {
        Write().Should().Contain("typeof(global::ExplicitDto)");
    }

    [Fact]
    public void WriteManifest_AllDirectRootsIsUnion()
    {
        var output = Write();

        output.Should().Contain("SurfaceRootTypes");
        output.Should().Contain("ExplicitRootTypes");
        output.Should().Contain("AllDirectRootTypes");
        output.Should().Contain("BindingRootTypes");
        output.Split("typeof(global::SurfaceDto)").Length.Should().BeGreaterThan(2);
        output.Split("typeof(global::ExplicitDto)").Length.Should().BeGreaterThan(2);
    }

    [Fact]
    public void WriteManifest_InternalAccessibility()
    {
        Write().Should().Contain("internal static class TestContextRootManifest");
    }

    [Fact]
    public void WriteManifest_PublicAccessibility()
    {
        Write(JsonContractManifestAccessibility.Public)
            .Should().Contain("public static class TestContextRootManifest");
    }

    private string Write(JsonContractManifestAccessibility accessibility = JsonContractManifestAccessibility.Internal)
    {
        var source = (Path: "Manifest.cs", Text: """
            using System.Text.Json.Serialization;
            using CrestCreates.Core.Abstractions.Serialization;
            using System.Threading;
            using System.Threading.Tasks;

            public record SurfaceDto(string Value);
            public record ExplicitDto(int Value);
            public interface IService { Task<SurfaceDto> GetAsync(CancellationToken cancellationToken); }

            [JsonContractSurface(typeof(IService))]
            [JsonContractExplicitRoot(typeof(ExplicitDto))]
            public partial class TestContext : JsonSerializerContext { }
            """);
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], GetDefaultReferences());
        result.Model.Should().NotBeNull();
        result.Model!.Contexts.Single().ManifestAccessibility = accessibility;
        return JsonContractSourceWriter.WriteContextSource(result.Model);
    }
}
