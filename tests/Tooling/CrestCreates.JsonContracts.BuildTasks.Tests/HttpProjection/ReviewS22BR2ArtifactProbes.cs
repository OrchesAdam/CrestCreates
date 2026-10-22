using System.Text.Json;
using CrestCreates.JsonContracts.BuildTasks.HttpProjection;
using CrestCreates.JsonContracts.BuildTasks.Semantic;
using CrestCreates.JsonContracts.BuildTasks.Tests.Infrastructure;
using CrestCreates.Tooling.HttpProjection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
namespace CrestCreates.JsonContracts.BuildTasks.Tests.HttpProjection;
public sealed class ReviewS22BR2ArtifactProbes : JsonContractCompilationTestBase
{
    private const string Source = """
        using System.Threading;
        using System.Threading.Tasks;
        using CrestCreates.Domain.Shared.Attributes;
        namespace Probe {
          public interface IService {
            [AppServiceHttpAction(Verb = HttpProjectionVerb.Post, Route = "echo", Body = HttpProjectionBodyPolicy.Required)]
            Task<string> EchoAsync(string input, CancellationToken cancellationToken = default);
          }
          [CapabilityCompatibilityProjection(ContractType = typeof(IService), RoutePrefix = "api/probe")]
          public static class Anchor { }
        }
        """;
    [Theory]
    [InlineData("active")]
    [InlineData("comment")]
    [InlineData("disabled")]
    [InlineData("literal")]
    public void Only_active_compiled_artifacts_may_satisfy_content_validation(string placement) {
        var authored = CreateCompilation("Probe.Http", new[] { ("Declaration.cs", Source) });
        authored.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        var result = new AppServiceHttpPrecompileEngine().Run("Probe.Http", new[] { ("Declaration.cs", Source) }, GetDefaultReferences(), "latest", Array.Empty<string>(), "enable", false);
        result.Succeeded.Should().BeTrue();
        result.Artifacts.Should().HaveCount(2);
        var original = string.Join("\n", result.Artifacts.Select(a => a.Text));
        var baseline = new AppServiceHttpJsonSurfaceDiscovery();
        baseline.Discover(CreateCompilation("Probe.Http", new[] { ("Declaration.cs", Source), ("Bundle.g.cs", original) }), "Probe.Http", JsonSerializer.Serialize(result.Manifest));
        baseline.Diagnostics.Should().BeEmpty();
        var forged = original.Replace("api/probe", "api/forged");
        forged.Should().NotBe(original);
        string compiled;
        if (placement == "active") compiled = original;
        else if (placement == "comment") {
            original.Should().NotContain("*/");
            compiled = "/*\n" + original + "\n*/\n" + forged;
        } else if (placement == "disabled") compiled = "#if false\n" + original + "\n#endif\n" + forged;
        else {
            var delimiter = new string('"', 6);
            original.Should().NotContain(delimiter);
            compiled = "internal static class RetainedArtifactText { internal const string Value =\n" + delimiter + "\n" + original + "\n" + delimiter + "; }\n" + forged;
        }
        var compilation = CreateCompilation("Probe.Http", new[] { ("Declaration.cs", Source), ("Bundle.g.cs", compiled) });
        var tree = compilation.SyntaxTrees.Single(t => t.FilePath == "Bundle.g.cs");
        var routes = tree.GetRoot().DescendantNodes().OfType<LiteralExpressionSyntax>().Where(n => n.IsKind(SyntaxKind.StringLiteralExpression) && n.Token.IsKind(SyntaxKind.StringLiteralToken)).Select(n => n.Token.ValueText).Where(s => s.Contains("api/"));
        routes.Should().Contain(s => s.Contains(placement == "active" ? "api/probe" : "api/forged"));
        if (placement != "active") routes.Should().NotContain(s => s.Contains("api/probe"), "the expected route exists only in inactive text");
        var discovery = new AppServiceHttpJsonSurfaceDiscovery();
        discovery.Discover(compilation, "Probe.Http", JsonSerializer.Serialize(result.Manifest));
        if (placement == "active") discovery.Diagnostics.Should().BeEmpty();
        else discovery.Diagnostics.Should().Contain(d => d.Id == AppServiceHttpDiagnosticCodes.TransportManifestState, "comment/disabled text cannot vouch for the active altered route");
    }
}
