using System.Text.Json;
using CrestCreates.JsonContracts.BuildTasks.HttpProjection;
using CrestCreates.JsonContracts.BuildTasks.Semantic;
using CrestCreates.JsonContracts.BuildTasks.Tests.Infrastructure;
using CrestCreates.Tooling.HttpProjection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
namespace CrestCreates.JsonContracts.BuildTasks.Tests.HttpProjection;
public sealed class ReviewS22BProbes : JsonContractCompilationTestBase
{
    private const string Source = """
        using System.Threading.Tasks;
        using CrestCreates.Domain.Shared.Attributes;
        namespace Probe {
          public struct Box<T> { public T Value { get; set; } }
          public class Outer<T> { public class Inner<U> { public U Value { get; set; } = default!; } }
          public interface IService {
            [AppServiceHttpAction(Verb = HttpProjectionVerb.Post, Route = "echo", Body = HttpProjectionBodyPolicy.Required)]
            Task<string> EchoAsync(BODY input, System.Threading.CancellationToken cancellationToken = default);
          }
          [CapabilityCompatibilityProjection(ContractType = typeof(IService), RoutePrefix = "api/probe")]
          public static class Anchor { }
        }
        """;
    private AppServiceHttpPrecompileResult Produce(string source) {
        var authored = CreateCompilation("Probe.Http", new[] { ("Declaration.cs", source) });
        authored.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        var result = new AppServiceHttpPrecompileEngine().Run("Probe.Http", new[] { ("Declaration.cs", source) }, GetDefaultReferences(), "latest", Array.Empty<string>(), "enable", false);
        result.Succeeded.Should().BeTrue(string.Join(" | ", result.Diagnostics.Select(d => d.Message)));
        return result;
    }
    private static string Bundle(AppServiceHttpPrecompileResult result) => string.Join("\n", result.Artifacts.Select(a => a.Text));
    [Theory]
    [InlineData("string")]
    [InlineData("Box<int>")]
    [InlineData("Box<int>?")]
    [InlineData("Outer<int>.Inner<string>")]
    public void Json_roots_must_preserve_the_actual_closed_parameter_symbol(string body) {
        var source = Source.Replace("BODY", body);
        var result = Produce(source);
        var compilation = CreateCompilation("Probe.Http", new[] { ("Declaration.cs", source), ("Bundle.g.cs", Bundle(result)) });
        var expected = ((IMethodSymbol)compilation.GetTypeByMetadataName("Probe.IService")!.GetMembers("EchoAsync").Single()).Parameters[0].Type;
        var discovery = new AppServiceHttpJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation, "Probe.Http", JsonSerializer.Serialize(result.Manifest));
        discovery.Diagnostics.Should().BeEmpty();
        roots.Should().Contain(r => SymbolEqualityComparer.Default.Equals(r.RootType, expected), "the runtime BodyTypes root must be exactly " + expected.ToDisplayString());
    }
    [Fact]
    public void Compiled_bundle_route_mutation_must_be_rejected_against_manifest() {
        var source = Source.Replace("BODY", "string");
        var result = Produce(source);
        var original = Bundle(result);
        var baseline = new AppServiceHttpJsonSurfaceDiscovery();
        baseline.Discover(CreateCompilation("Probe.Http", new[] { ("Declaration.cs", source), ("Bundle.g.cs", original) }), "Probe.Http", JsonSerializer.Serialize(result.Manifest));
        baseline.Diagnostics.Should().BeEmpty();
        original.Should().Contain("api/probe");
        var mutated = original.Replace("api/probe", "api/forged");
        mutated.Should().NotBe(original);
        var discovery = new AppServiceHttpJsonSurfaceDiscovery();
        discovery.Discover(CreateCompilation("Probe.Http", new[] { ("Declaration.cs", source), ("Bundle.g.cs", mutated) }), "Probe.Http", JsonSerializer.Serialize(result.Manifest));
        discovery.Diagnostics.Should().Contain(d => d.Id == AppServiceHttpDiagnosticCodes.TransportManifestState, "the compiled artifact no longer has the manifest content hash");
    }
    [Fact]
    public void Duplicate_anchor_must_return_CCHTTP003_in_the_actual_producer() {
        var source = Source.Replace("BODY", "string").Replace("public static class Anchor { }", "public static class Anchor { }\n [CapabilityCompatibilityProjection(ContractType = typeof(IService), RoutePrefix = \"api/other\")] public static class SecondAnchor { }");
        var authored = CreateCompilation("Probe.Http", new[] { ("Declaration.cs", source) });
        authored.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        var result = new AppServiceHttpPrecompileEngine().Run("Probe.Http", new[] { ("Declaration.cs", source) }, GetDefaultReferences(), "latest", Array.Empty<string>(), "enable", false);
        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Id == AppServiceHttpDiagnosticCodes.OwnerRouteActionConflict && d.IsError);
        result.Artifacts.Should().BeEmpty();
    }
    [Fact]
    public void Explicit_host_without_ContractType_must_not_silently_produce_no_endpoints() {
        const string source = """
            using CrestCreates.Domain.Shared.Attributes;
            [CapabilityCompatibilityProjection(RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost)]
            public static class MissingContractAnchor { }
            """;
        var authored = CreateCompilation("Probe.Http", new[] { ("Declaration.cs", source) });
        authored.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        var result = new AppServiceHttpPrecompileEngine().Run("Probe.Http", new[] { ("Declaration.cs", source) }, GetDefaultReferences(), "latest", Array.Empty<string>(), "enable", false);
        result.Succeeded.Should().BeFalse("an explicitly formal declaration is invalid and must not be mistaken for a legacy declaration");
        result.Diagnostics.Should().Contain(d => d.Id == AppServiceHttpDiagnosticCodes.UnsupportedDeclaration && d.IsError);
    }

}
