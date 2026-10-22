using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using CrestCreates.Tooling.CrudGeneration;
using CrestCreates.Tooling.HttpProjection;
using CrestCreates.Capability.Abstractions;
using CrestCreates.DynamicApi;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.AppServiceCompatibilityGenerator;
public class ReviewS22AEmitterR2Probes
{
    private const string Preamble="using System; using System.Threading; using System.Threading.Tasks; using CrestCreates.Domain.Shared.Attributes;\n";
    private const string Stub="namespace Review { public static class JsonStub { public static System.Text.Json.Serialization.JsonSerializerContext Default = null!; } }";
    private static string Source(string members,string extra="") => Preamble+"namespace Review { public class Dto { public string Name {get;set;}=\"\"; } public class Body {public string Name {get;set;}=\"\";} public interface IService {"+members+"} [CapabilityCompatibilityProjection(ContractType=typeof(IService),RoutePrefix=\"api/items\")] public static class Anchor {} "+extra+" }";
    private sealed record Bundle(string Source,AppServiceHttpProjectionModel Model,AppServiceHttpEmissionOptions Options,IReadOnlyList<AppServiceHttpArtifact> Artifacts);
    private static Bundle BuildFull(string source,string anchor="Review.Anchor",string owner="Review.Http")
    {
        var c=SourceGeneratorTestHelper.CreateAnalysisCompilation(owner,new[]{source});
        Assert.Empty(c.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error));
        var r=AppServiceHttpProjectionModelBuilder.Build(c,c.GetTypeByMetadataName(anchor)!);
        Assert.False(r.HasErrors,string.Join(" | ",r.Diagnostics.Select(d=>d.ToDisplayString())));
        var options=AppServiceHttpEmissionOptions.ForContract(r.Model!.ContractFullName) with {JsonContextFullName="global::Review.JsonStub"};
        return new(source,r.Model,options,AppServiceHttpProjectionEmitter.Emit(r.Model,options));
    }
    private static Bundle Build(string members,string extra="")=>BuildFull(Source(members,extra));
    private static CSharpCompilation Compilation(string source,IEnumerable<AppServiceHttpArtifact> artifacts,string? extra=null)
        => SourceGeneratorTestHelper.CreateAnalysisCompilation("Review.Generated."+Guid.NewGuid().ToString("N"),
            new[]{source,Stub}.Concat(artifacts.Select(a=>a.Source)).Concat(extra is null?Array.Empty<string>():new[]{extra}).ToArray(),
            additionalReferences:new[]{"CrestCreates.Metadata","CrestCreates.Metadata.Abstractions","CrestCreates.Capability.Abstractions","CrestCreates.DynamicApi.Abstractions","CrestCreates.Schema.Abstractions","CrestCreates.Metadata.Abstractions.CanonicalHashing","CrestCreates.Accountability.Abstractions"},
            additionalMetadataReferences:new[]{
                MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Options.IConfigureOptions<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Http.Json.JsonOptions).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Text.Json.JsonSerializer).Assembly.Location)});
    private static void NoErrors(CSharpCompilation c)
    {
        var errors=c.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length==0,string.Join(" | ",errors.Select(d=>d.ToString())));
    }
    private static Assembly Load(Bundle b)
    {
        var c=Compilation(b.Source,b.Artifacts);NoErrors(c);
        using var image=new MemoryStream();var emit=c.Emit(image);
        Assert.True(emit.Success,string.Join(" | ",emit.Diagnostics));
        return Assembly.Load(image.ToArray());
    }
    private static Bundle? BuildSupportedOrReject(string members,string extra="")
    {
        var source=Source(members,extra);
        var c=SourceGeneratorTestHelper.CreateAnalysisCompilation("Review.Http",new[]{source});
        Assert.Empty(c.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error));
        var result=AppServiceHttpProjectionModelBuilder.Build(c,c.GetTypeByMetadataName("Review.Anchor")!);
        if(result.HasErrors)
        {
            Assert.Contains(result.Diagnostics,d=>d.Id==AppServiceHttpDiagnosticCodes.UnsupportedDeclaration && d.Severity==AppServiceHttpDiagnosticSeverity.Error && !string.IsNullOrWhiteSpace(d.Message));
            return null; // Unsupported shapes may fail closed at model time, before any artifact.
        }
        return BuildFull(source);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accepted_enum_query_must_bind_a_defined_name(bool nullable)
    {
        var type = nullable ? "State?" : "State";
        var declaration="[AppServiceHttpAction(Verb=HttpProjectionVerb.Get,Route=\"find\")] Task<Dto> FindAsync("+type+" value,CancellationToken ct=default);";
        var b=nullable ? BuildSupportedOrReject(declaration,"public enum State { Ready=1, Done=2 }") : Build(declaration,"public enum State { Ready=1, Done=2 }");
        if(b is null) return;
        var assembly=Load(b);
        var contribution=(CapabilityEndpointContribution)Activator.CreateInstance(assembly.GetType("CrestCreates.Generated.HttpProjection."+b.Options.ContributionTypeName)!,nonPublic:true)!;
        var context=new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.QueryString=new Microsoft.AspNetCore.Http.QueryString("?value=Ready");
        var input=await contribution.Bindings.Single().BindInputAsync(context,CancellationToken.None);
        Assert.NotNull(input);
        Assert.Equal("Ready",input.ToString());
    }
    [Theory]
    [InlineData("void Touch(ref Guid id,CancellationToken ct=default);")]
    [InlineData("Task<T> EchoAsync<T>(T input,CancellationToken ct=default);")]
    public void Accepted_method_shape_must_emit_compilable_invocation(string signature)
    {
        var b=BuildSupportedOrReject(signature);
        if(b is null) return;
        NoErrors(Compilation(b.Source,b.Artifacts));
    }
    [Fact]
    public void Two_owner_assemblies_can_be_activated_independently_from_one_host()
    {
        (MetadataReference Reference,string ActivationFullName) Owner(string owner, string ns)
        {
            var source=Source("Task<Dto> ReadAsync(CancellationToken ct=default);").Replace("namespace Review", "namespace "+ns);
            var b=BuildFull(source, ns+".Anchor", owner);
            var activation=AppServiceHttpProjectionEmitter.EmitAssemblyActivation(owner,new[]{(b.Model,b.Options)},AppServiceHttpAssemblyOptions.ForAssembly(owner) with {JsonContextFullName="global::Review.JsonStub"});
            var compilation=Compilation(source,b.Artifacts.Append(activation)).WithAssemblyName(owner);
            NoErrors(compilation);
            using var image=new MemoryStream();
            var emitted=compilation.Emit(image);
            Assert.True(emitted.Success,string.Join(" | ",emitted.Diagnostics));
            var activationType=compilation.GetSymbolsWithName(AppServiceHttpProjectionModel.ActivationMethodName,SymbolFilter.Member).OfType<IMethodSymbol>().Single(m=>m.IsExtensionMethod).ContainingType;
            return (MetadataReference.CreateFromImage(image.ToArray()),activationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }
        var first=Owner("First.Http", "First");
        var second=Owner("Second.Http", "Second");
        var host="using Microsoft.Extensions.DependencyInjection; public static class Host { public static void Boot(){var services=new ServiceCollection(); "+first.ActivationFullName+".AddGeneratedAppServiceHttp(services); "+second.ActivationFullName+".AddGeneratedAppServiceHttp(services); } }";
        NoErrors(Compilation("",Array.Empty<AppServiceHttpArtifact>(),host).AddReferences(first.Reference,second.Reference));
    }
}
