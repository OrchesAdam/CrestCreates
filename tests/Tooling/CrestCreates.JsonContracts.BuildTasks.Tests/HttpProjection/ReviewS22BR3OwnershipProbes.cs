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
public sealed class ReviewS22BR3OwnershipProbes : JsonContractCompilationTestBase
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
    [InlineData("original")]
    [InlineData("forged")]
    [InlineData("wrong-namespace-copy")]
    public void Validation_must_bind_the_expected_FQN_instead_of_any_matching_active_tokens(string placement) {
        var authored=CreateCompilation("Probe.Http",new[] { ("Declaration.cs",Source) });
        authored.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error).Should().BeEmpty();
        var result=new AppServiceHttpPrecompileEngine().Run("Probe.Http",new[] { ("Declaration.cs",Source) },GetDefaultReferences(),"latest",Array.Empty<string>(),"enable",false);
        result.Succeeded.Should().BeTrue();
        var original=string.Join("\n",result.Artifacts.Select(a=>a.Text));
        var forged=original.Replace("api/probe","api/forged");
        forged.Should().NotBe(original);
        var actual=placement=="original"?original:placement=="forged"?forged:"namespace Decoy {\n"+original+"\n}\n"+forged;
        var compilation=CreateCompilation("Probe.Http",new[] { ("Declaration.cs",Source),("Bundle.g.cs",actual) });
        var name=result.Manifest!.Entries!.SelectMany(e=>e.DeclaredTypeFullNames!).First(n=>n.Contains("Contribution_"));
        var real=compilation.GetTypeByMetadataName(name);
        real.Should().NotBeNull();
        var literals=real!.DeclaringSyntaxReferences.Single().GetSyntax().DescendantNodes().OfType<LiteralExpressionSyntax>().Where(n=>n.IsKind(SyntaxKind.StringLiteralExpression)).Select(n=>n.Token.ValueText).ToArray();
        literals.Should().Contain(s=>s.Contains(placement=="original"?"api/probe":"api/forged"));
        if(placement=="wrong-namespace-copy") {
            literals.Should().NotContain(s=>s.Contains("api/probe"));
            var decoy=compilation.GetTypeByMetadataName("Decoy."+name);
            decoy.Should().NotBeNull();
            SymbolEqualityComparer.Default.Equals(real,decoy).Should().BeFalse();
            decoy!.DeclaringSyntaxReferences.Single().GetSyntax().ToFullString().Should().Contain("api/probe");
        }
        var discovery=new AppServiceHttpJsonSurfaceDiscovery();
        discovery.Discover(compilation,"Probe.Http",JsonSerializer.Serialize(result.Manifest));
        if(placement=="original") discovery.Diagnostics.Should().BeEmpty();
        else discovery.Diagnostics.Should().Contain(d=>d.Id==AppServiceHttpDiagnosticCodes.TransportManifestState,"the expected FQN's active declaration was modified even if an unrelated namespace contains original tokens");
    }
}
