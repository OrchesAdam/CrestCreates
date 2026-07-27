using CrestCreates.JsonContracts.BuildTasks.Diagnostics;
using CrestCreates.JsonContracts.BuildTasks.Model;
using CrestCreates.JsonContracts.BuildTasks.Tests.Infrastructure;
using FluentAssertions;

namespace CrestCreates.JsonContracts.BuildTasks.Tests.Semantic;

/// <summary>Case IDs: F01-F06</summary>
public class SurfaceInferenceFailureTests : JsonContractCompilationTestBase
{
    [Fact]
    public void Fail_GenericMethodOnSurface()
    {
        var source = (Path: "GenericMethod.cs", Text: @"
using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;

public interface IGenericService
{
    Task<T> GetAsync<T>(System.Threading.CancellationToken ct);
}

[JsonContractSurface(typeof(IGenericService))]
public partial class GenericMethodContext : JsonSerializerContext { }");
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        JsonContractDiagnosticAssertions.ShouldHaveDiagnostic(
            result.Diagnostics,
            JsonContractDiagnosticIds.GenericMethod);
    }

    [Fact]
    public void Allow_ClosedGenericRootType()
    {
        var source = (Path: "OpenGenericRoot.cs", Text: @"
using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;
using System.Collections.Generic;
using System.Threading.Tasks;

public interface IOpenGenericService
{
    Task<List<object>> GetAsync(System.Threading.CancellationToken ct);
}

[JsonContractSurface(typeof(IOpenGenericService))]
public partial class OpenGenericRootContext : JsonSerializerContext { }");
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        result.Diagnostics.Should().BeEmpty();
        var rootNames = result.Model!.Contexts[0].SurfaceRoots.Select(r => r.FullMetadataName).ToList();
        rootNames.Should().Contain(n => n.Contains("List"));
    }

    [Fact]
    public void Fail_PointerRootType()
    {
        var source = (Path: "PointerRoot.cs", Text: """
            using System.Text.Json.Serialization;
            using CrestCreates.Core.Abstractions.Serialization;

            public unsafe interface IPointerService
            {
                int* GetPointer();
            }

            [JsonContractSurface(typeof(IPointerService))]
            public partial class PointerContext : JsonSerializerContext { }
            """);
        var result = JsonContractTestHelper.BuildModel(
            "TestAssembly", [source], GetDefaultReferences(), allowUnsafeBlocks: true);

        JsonContractDiagnosticAssertions.ShouldHaveDiagnostic(
            result.Diagnostics,
            JsonContractDiagnosticIds.ByRefPointerOrRefLikeParameter);
        result.Model!.Contexts.Single().SurfaceRoots.Should().BeEmpty();
    }

    [Fact]
    public void Fail_FunctionPointerRootType()
    {
        var source = (Path: "FunctionPointerRoot.cs", Text: """
            using System.Text.Json.Serialization;
            using CrestCreates.Core.Abstractions.Serialization;

            public unsafe interface IFunctionPointerService
            {
                delegate*<int, int> GetCallback();
            }

            [JsonContractSurface(typeof(IFunctionPointerService))]
            public partial class FunctionPointerContext : JsonSerializerContext { }
            """);
        var result = JsonContractTestHelper.BuildModel(
            "TestAssembly", [source], GetDefaultReferences(), allowUnsafeBlocks: true);

        JsonContractDiagnosticAssertions.ShouldHaveDiagnostic(
            result.Diagnostics,
            JsonContractDiagnosticIds.ByRefPointerOrRefLikeParameter);
        result.Model!.Contexts.Single().SurfaceRoots.Should().BeEmpty();
    }

    [Fact]
    public void Fail_RefLikeRootType()
    {
        var source = (Path: "RefLikeRoot.cs", Text: """
            using System;
            using System.Text.Json.Serialization;
            using CrestCreates.Core.Abstractions.Serialization;

            public interface IBufferService
            {
                Span<byte> GetBuffer();
            }

            [JsonContractSurface(typeof(IBufferService))]
            public partial class RefLikeContext : JsonSerializerContext { }
            """);
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], GetDefaultReferences());

        JsonContractDiagnosticAssertions.ShouldHaveDiagnostic(
            result.Diagnostics,
            JsonContractDiagnosticIds.ByRefPointerOrRefLikeParameter);
        result.Model!.Contexts.Single().SurfaceRoots.Should().BeEmpty();
    }

    [Fact]
    public void Fail_ByRefReturn()
    {
        var source = ByRefReturnSource("ref Dto Get();", "ByRefContext");
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], GetDefaultReferences());

        JsonContractDiagnosticAssertions.ShouldHaveDiagnostic(
            result.Diagnostics,
            JsonContractDiagnosticIds.InvalidRoot);
        result.Model!.Contexts.Single().SurfaceRoots.Should().BeEmpty();
    }

    [Fact]
    public void Fail_ByRefReadonlyReturn()
    {
        var source = ByRefReturnSource("ref readonly Dto Get();", "ByRefReadonlyContext");
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], GetDefaultReferences());

        JsonContractDiagnosticAssertions.ShouldHaveDiagnostic(
            result.Diagnostics,
            JsonContractDiagnosticIds.InvalidRoot);
        result.Model!.Contexts.Single().SurfaceRoots.Should().BeEmpty();
    }

    [Fact]
    public void Exclusion_ClosedGenericMatchesExactConstructedTypeOnly()
    {
        var source = (Path: "ExactExclusion.cs", Text: """
            using System.Text.Json.Serialization;
            using System.Threading.Tasks;
            using CrestCreates.Core.Abstractions.Serialization;

            public sealed class InvocationContext { }
            public sealed class BusinessRequest { }
            public sealed record Envelope<T>(T Value);

            public interface IService
            {
                Task ExecuteAsync(
                    Envelope<InvocationContext> invocation,
                    Envelope<BusinessRequest> request);
            }

            [JsonContractSurface(
                typeof(IService),
                ExcludedParameterTypes = new[] { typeof(Envelope<InvocationContext>) })]
            public partial class ExactExclusionContext : JsonSerializerContext { }
            """);
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], GetDefaultReferences());

        result.Diagnostics.Should().BeEmpty();
        var roots = result.Model!.Contexts.Single().SurfaceRoots;
        roots.Should().ContainSingle();
        roots[0].FullMetadataName.Should().Contain("Envelope<global::BusinessRequest>");
        roots[0].FullMetadataName.Should().NotContain("InvocationContext");
    }

    [Fact]
    public void Fail_UnresolvedPreCoreCompileRoot()
    {
        var source = JsonContractTestSources.SameProjectUnresolvedType();
        var refs = GetDefaultReferences();
        var result = JsonContractTestHelper.BuildModel("TestAssembly", [source], refs);

        JsonContractDiagnosticAssertions.ShouldHaveDiagnostic(
            result.Diagnostics,
            JsonContractDiagnosticIds.UnresolvedPreCoreCompileRoot);
    }

    private static (string Path, string Text) ByRefReturnSource(string signature, string contextName) =>
        ("ByRefReturn.cs", $$"""
            using System.Text.Json.Serialization;
            using CrestCreates.Core.Abstractions.Serialization;

            public sealed class Dto { }
            public interface IByRefService
            {
                {{signature}}
            }

            [JsonContractSurface(typeof(IByRefService))]
            public partial class {{contextName}} : JsonSerializerContext { }
            """);
}
