using System;
using System.Linq;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using CrestCreates.Tooling.HttpProjection;
using Microsoft.CodeAnalysis;
using Xunit;
using CompatibilityGen = CrestCreates.CodeGenerator.AppServiceCompatibilityGenerator.AppServiceCompatibilityGenerator;

namespace CrestCreates.CodeGenerator.Tests.AppServiceCompatibilityGenerator;

public class ReviewS21ContractProbes
{
    private const string Usings = "using System; using System.Threading; using System.Threading.Tasks; using CrestCreates.Domain.Shared.Attributes;\n";
    private static AppServiceHttpProjectionBuildResult Build(string source, string anchor)
    {
        var c = SourceGeneratorTestHelper.CreateAnalysisCompilation("Review.Http", new[]{Usings+source});
        Assert.Empty(c.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error));
        return AppServiceHttpProjectionModelBuilder.Build(c,c.GetTypeByMetadataName(anchor)!);
    }
    [Fact]
    public void Closed_generic_contract_is_a_supported_closed_interface()
    {
        var r=Build("""
            namespace Review {
              public class Dto { public string Name { get; set; } = ""; }
              public interface IService<T> { Task<T> GetByIdAsync(Guid id,CancellationToken ct=default); }
              [CapabilityCompatibilityProjection(ContractType=typeof(IService<Dto>))]
              public static class Anchor {}
            }
            """, "Review.Anchor");
        Assert.False(r.HasErrors,string.Join(" | ",r.Diagnostics.Select(d=>d.ToDisplayString())));
        Assert.Contains("Review.Dto",r.Model!.Actions.Single().ResultTypeFullName);
    }
    [Fact]
    public void Private_interface_helper_is_not_an_action()
    {
        var r=Build("""
            namespace Review {
              public interface IService {
                Task<string> PingAsync(CancellationToken ct=default);
                private string PrivateHelper() => "hidden";
              }
              [CapabilityCompatibilityProjection(ContractType=typeof(IService))]
              public static class Anchor {}
            }
            """, "Review.Anchor");
        Assert.False(r.HasErrors,string.Join(" | ",r.Diagnostics.Select(d=>d.ToDisplayString())));
        Assert.Equal(new[]{"PingAsync"},r.Model!.Actions.Select(a=>a.Name));
    }
    private static string TwinSource(bool explicitPrefix) => """
        namespace First {
          public class Dto { public string Name { get; set; } = ""; }
          public interface IOrderService { Task<Dto> UpdateAsync(Guid id,Dto input,CancellationToken ct=default); }
          [CapabilityCompatibilityProjection(ContractType=typeof(IOrderService),RoutePrefix="first"PLACEHOLDER_FIRST)]
          public static class Anchor {}
        }
        namespace Second {
          public class Dto { public string Name { get; set; } = ""; }
          public interface IOrderService { Task<Dto> UpdateAsync(Guid id,Dto input,CancellationToken ct=default); }
          [CapabilityCompatibilityProjection(ContractType=typeof(IOrderService),RoutePrefix="second"PLACEHOLDER_SECOND)]
          public static class Anchor {}
        }
        """.Replace("PLACEHOLDER_FIRST",explicitPrefix ? ",CapabilityIdPrefix=\"first\"":"")
            .Replace("PLACEHOLDER_SECOND",explicitPrefix ? ",CapabilityIdPrefix=\"second\"":"");
    [Fact]
    public void Envelopes_include_full_owner_identity_even_with_distinct_explicit_prefixes()
    {
        var source=TwinSource(true);
        var a=Build(source,"First.Anchor"); var b=Build(source,"Second.Anchor");
        Assert.False(a.HasErrors); Assert.False(b.HasErrors);
        Assert.NotEqual(a.Model!.Actions.Single().CapabilityId,b.Model!.Actions.Single().CapabilityId);
        Assert.NotEqual(a.Model.Actions.Single().InputEnvelopeFullName,b.Model.Actions.Single().InputEnvelopeFullName);
    }
    [Fact]
    public void Default_capability_ids_include_full_contract_identity()
    {
        var source=TwinSource(false);
        var a=Build(source,"First.Anchor"); var b=Build(source,"Second.Anchor");
        Assert.False(a.HasErrors); Assert.False(b.HasErrors);
        Assert.NotEqual(a.Model!.Actions.Single().CapabilityId,b.Model!.Actions.Single().CapabilityId);
    }
    [Fact]
    public void Explicit_host_without_contract_cannot_silently_publish_static()
    {
        var result=SourceGeneratorTestHelper.RunGenerator<CompatibilityGen>(Usings+"""
            namespace Review {
              [CrestService]
              [CapabilityCompatibilityProjection(RegistrationMode=HttpProjectionRegistrationMode.ExplicitHost)]
              public class BrokenAppService {
                public Task<string> PingAsync(CancellationToken ct=default)=>Task.FromResult("ok");
              }
            }
            """,additionalSources:AppServiceCompatibilityGeneratorTests.BuildCompatibilityStubs());
        Assert.True(result.CompilationSuccess,string.Join(" | ",result.Diagnostics));
        Assert.DoesNotContain(result.GeneratedSources,s=>s.SourceText.ToString().Contains("DescriptorProviderRegistry.Register"));
    }
}
