using CrestCreates.JsonContracts.BuildTasks.Diagnostics;
using CrestCreates.JsonContracts.BuildTasks.Model;
using CrestCreates.JsonContracts.BuildTasks.Tests.Infrastructure;
using FluentAssertions;

namespace CrestCreates.JsonContracts.BuildTasks.Tests.Semantic;

/// <summary>Case IDs: H01, H02, H03, H04, H05</summary>
public class SurfaceInferenceHappyTests : JsonContractCompilationTestBase
{
    [Fact]
    public void Build_InheritedInterfaceMethods()
    {
        var source = JsonContractTestSources.InheritedSurface();
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        result.Diagnostics.Should().BeEmpty();
        result.Model!.Contexts.Should().HaveCount(1);

        var ctx = result.Model.Contexts[0];
        ctx.SurfaceRoots.Should().NotBeEmpty();

        var rootNames = ctx.SurfaceRoots.Select(r => r.FullMetadataName).ToList();
        rootNames.Should().Contain("global::System.String");
        rootNames.Should().Contain("global::System.Int32");
    }

    [Fact]
    public void Build_UnwrapsTaskAndValueTask()
    {
        var source = (Path: "TaskValueTaskSurface.cs", Text: @"
using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;
using System.Threading.Tasks;

public interface ITaskService
{
    Task<string> GetAsync(System.Threading.CancellationToken ct);
    ValueTask<int> ComputeAsync(System.Threading.CancellationToken ct);
    Task NoResultAsync(System.Threading.CancellationToken ct);
    ValueTask NoValueAsync(System.Threading.CancellationToken ct);
    void VoidMethod(System.Threading.CancellationToken ct);
}

[JsonContractSurface(typeof(ITaskService))]
public partial class TaskContext : JsonSerializerContext { }");
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        result.Diagnostics.Should().BeEmpty();
        var rootNames = result.Model!.Contexts[0].SurfaceRoots.Select(r => r.FullMetadataName).ToList();
        rootNames.Should().Contain("global::System.String");
        rootNames.Should().Contain("global::System.Int32");
        rootNames.Should().NotContain(n => n.Contains("Task") || n.Contains("ValueTask"));
    }

    [Fact]
    public void Build_TaskValueTaskAndVoidProduceNoReturnRoot()
    {
        var source = (Path: "NoReturnRootSurface.cs", Text: @"
using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;
using System.Threading.Tasks;

public interface INoReturnService
{
    Task DoAsync(System.Threading.CancellationToken ct);
    ValueTask ComputeAsync(System.Threading.CancellationToken ct);
    void Execute(System.Threading.CancellationToken ct);
}

[JsonContractSurface(typeof(INoReturnService))]
public partial class NoReturnContext : JsonSerializerContext { }");
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        result.Diagnostics.Should().BeEmpty();
        result.Model!.Contexts[0].SurfaceRoots.Should().BeEmpty();
    }

    [Fact]
    public void Build_MultipleSurfacesMergeDeterministically()
    {
        var source = (Path: "MultiSurfaceMerge.cs", Text: @"
using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;

public record SharedDto(string Value);

public interface IFirstSurface
{
    System.Threading.Tasks.Task<SharedDto> FirstAsync(System.Threading.CancellationToken ct);
}

public interface ISecondSurface
{
    System.Threading.Tasks.Task<SharedDto> SecondAsync(SharedDto input, System.Threading.CancellationToken ct);
}

[JsonContractSurface(typeof(IFirstSurface))]
[JsonContractSurface(typeof(ISecondSurface))]
public partial class MergeContext : JsonSerializerContext { }");
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        result.Diagnostics.Should().BeEmpty();
        var rootNames = result.Model!.Contexts[0].SurfaceRoots.Select(r => r.FullMetadataName).ToList();
        rootNames.Should().Contain("global::SharedDto");
        rootNames.Count(n => n == "global::SharedDto").Should().Be(1);
    }
}
