using System;
using System.IO;
using System.Linq;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using CrestCreates.Tooling.HttpProjection;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.AppServiceCompatibilityGenerator;
public class ReviewS21CanonicalR3Probes
{
    [Fact]
    public void Leading_digit_prefix_must_not_alias_an_encoded_literal_underscore()
    {
        Assert.NotEqual(AppServiceHttpWireConvention.CanonicalToken("005FHost"),
                        AppServiceHttpWireConvention.CanonicalToken("_Host"));
    }
    private static AppServiceHttpProjectionModel Build(string assemblyName)
    {
        const string source = """
            using System; using System.Threading; using System.Threading.Tasks;
            using CrestCreates.Domain.Shared.Attributes;
            namespace Review {
                public class Dto { public string Name {get;set;}=""; }
                public interface IService {
                    Task<Dto> UpdateAsync(Guid id, Dto input, CancellationToken ct=default);
                }
                [CapabilityCompatibilityProjection(ContractType=typeof(IService),RoutePrefix="items")]
                public static class Anchor {}
            }
            """;
        var c=SourceGeneratorTestHelper.CreateAnalysisCompilation(assemblyName,new[]{source});
        Assert.Empty(c.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error));
        using var output=new MemoryStream();
        var emit=c.Emit(output);
        Assert.True(emit.Success,string.Join(" | ",emit.Diagnostics));
        var result=AppServiceHttpProjectionModelBuilder.Build(c,c.GetTypeByMetadataName("Review.Anchor")!);
        Assert.False(result.HasErrors,string.Join(" | ",result.Diagnostics));
        Assert.Equal(assemblyName,result.Model!.HttpOwnerAssemblyName);
        return result.Model;
    }
    [Fact]
    public void Distinct_legal_owner_assemblies_must_have_distinct_action_identities()
    {
        var a=Build("005FHost"); var b=Build("_Host");
        Assert.NotEqual(a.HttpOwnerAssemblyName,b.HttpOwnerAssemblyName);
        var x=a.Actions.Single(); var y=b.Actions.Single();
        Assert.True(x.CapabilityId!=y.CapabilityId && x.InputEnvelopeFullName!=y.InputEnvelopeFullName,
            $"owners={a.HttpOwnerAssemblyName}/{b.HttpOwnerAssemblyName}; ids={x.CapabilityId}/{y.CapabilityId}; envelopes={x.InputEnvelopeFullName}/{y.InputEnvelopeFullName}");
    }
}
