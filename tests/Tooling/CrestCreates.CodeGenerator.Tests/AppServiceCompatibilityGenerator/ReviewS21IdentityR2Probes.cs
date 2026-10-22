using System;
using System.Linq;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using CrestCreates.Tooling.HttpProjection;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.AppServiceCompatibilityGenerator;
public class ReviewS21IdentityR2Probes
{
    private const string Source = """
        using System; using System.Threading; using System.Threading.Tasks;
        using CrestCreates.Domain.Shared.Attributes;
        namespace A.B {
          public class Dto { public string Name {get;set;}=""; }
          public interface IOrder { Task<Dto> UpdateAsync(Guid id, Dto input,CancellationToken ct=default); }
          [CapabilityCompatibilityProjection(ContractType=typeof(IOrder),RoutePrefix="first"FIRST)]
          public static class Anchor {}
        }
        namespace A_B {
          public class Dto { public string Name {get;set;}=""; }
          public interface IOrder { Task<Dto> UpdateAsync(Guid id, Dto input,CancellationToken ct=default); }
          [CapabilityCompatibilityProjection(ContractType=typeof(IOrder),RoutePrefix="second"SECOND)]
          public static class Anchor {}
        }
        """;
    private static AppServiceHttpProjectionModel Build(string anchor, bool explicitPrefix=false)
    {
        var source=Source.Replace("FIRST",explicitPrefix ? ", CapabilityIdPrefix=\"first\"":"")
                         .Replace("SECOND",explicitPrefix ? ", CapabilityIdPrefix=\"second\"":"");
        var c=SourceGeneratorTestHelper.CreateAnalysisCompilation("Review.Http",new[]{source});
        Assert.Empty(c.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error));
        var result=AppServiceHttpProjectionModelBuilder.Build(c,c.GetTypeByMetadataName(anchor)!);
        Assert.False(result.HasErrors,string.Join(" | ",result.Diagnostics));
        return result.Model!;
    }
    [Fact]
    public void Envelope_FQN_must_be_usable_in_an_ordinary_Compile_declaration()
    {
        var fqn=Build("A.B.Anchor").Actions.Single().InputEnvelopeFullName!;
        var split=fqn.LastIndexOf('.');
        var source="namespace "+fqn[..split]+" { internal sealed class "+fqn[(split+1)..]+" {} }";
        var c=SourceGeneratorTestHelper.CreateAnalysisCompilation("Review.Envelope",new[]{source});
        var errors=c.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length==0,fqn+" => "+string.Join(" | ",errors.Select(d=>d.ToString())));
    }
    [Fact]
    public void Namespace_separators_and_underscores_must_not_collapse_default_identity()
    {
        var a=Build("A.B.Anchor"); var b=Build("A_B.Anchor");
        Assert.NotEqual(a.Actions.Single().CapabilityId,b.Actions.Single().CapabilityId);
    }
    [Fact]
    public void Distinct_contracts_with_explicit_prefixes_must_not_share_an_envelope()
    {
        var a=Build("A.B.Anchor",true); var b=Build("A_B.Anchor",true);
        Assert.NotEqual(a.Actions.Single().CapabilityId,b.Actions.Single().CapabilityId);
        Assert.NotEqual(a.Actions.Single().InputEnvelopeFullName,b.Actions.Single().InputEnvelopeFullName);
    }
}
