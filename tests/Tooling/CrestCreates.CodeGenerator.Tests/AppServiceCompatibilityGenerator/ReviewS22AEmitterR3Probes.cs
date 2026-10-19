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
public class ReviewS22AEmitterR3Probes
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
    [InlineData("int?", null, "<null>")]
    [InlineData("int?", "42", "42")]
    [InlineData("Guid?", null, "<null>")]
    [InlineData("Guid?", "01b9d199-dbf5-4b77-9c6f-f706ef9cfcd1", "01b9d199-dbf5-4b77-9c6f-f706ef9cfcd1")]
    [InlineData("State?", null, "<null>")]
    [InlineData("State?", "Ready", "Ready")]
    [InlineData("string?", null, "<null>")]
    [InlineData("string?", "hello", "hello")]
    public async Task Optional_query_preserves_absence_and_value_through_real_invoker(string type,string? text,string expected)
    {
        var b=Build("[AppServiceHttpAction(Verb=HttpProjectionVerb.Get,Route=\"find\")] Task<string> FindAsync("+type+" value,CancellationToken ct=default);",
            "public enum State { Ready=1, Done=2 } public sealed class Service:IService { public Task<string> FindAsync("+type+" value,CancellationToken ct=default) => Task.FromResult(value?.ToString() ?? \"<null>\"); }");
        Assert.False(b.Model.Actions.Single().Parameters.Single(p=>p.Source=="Query").Required);
        var a=Load(b); var contribution=(CapabilityEndpointContribution)Activator.CreateInstance(a.GetType("CrestCreates.Generated.HttpProjection."+b.Options.ContributionTypeName)!,true)!;
        var http=new Microsoft.AspNetCore.Http.DefaultHttpContext();
        if(text is not null) http.Request.QueryString=new Microsoft.AspNetCore.Http.QueryString("?value="+Uri.EscapeDataString(text));
        var input=await contribution.Bindings.Single().BindInputAsync(http,CancellationToken.None);
        var services=new ServiceCollection(); services.AddSingleton(a.GetType("Review.IService")!,Activator.CreateInstance(a.GetType("Review.Service")!)!);
        using var provider=services.BuildServiceProvider();
        var output=await ((ICapabilityContextAwareHandlerInvoker)contribution.Handlers.Single().Invoker).InvokeAsync(new CapabilityExecutionContext{ServiceProvider=provider,Input=input},CancellationToken.None);
        Assert.Equal(expected,output);
    }
    [Theory]
    [InlineData("int?", "bad")]
    [InlineData("Guid?", "bad")]
    [InlineData("State?", "bad")]
    [InlineData("int", null)]
    [InlineData("Guid", null)]
    [InlineData("State", null)]
    public async Task Optional_invalid_and_required_missing_queries_are_rejected(string type,string? text)
    {
        var b=Build("[AppServiceHttpAction(Verb=HttpProjectionVerb.Get,Route=\"find\")] Task<string> FindAsync("+type+" value,CancellationToken ct=default);", "public enum State {Ready=1,Done=2}");
        var a=Load(b); var contribution=(CapabilityEndpointContribution)Activator.CreateInstance(a.GetType("CrestCreates.Generated.HttpProjection."+b.Options.ContributionTypeName)!,true)!;
        var http=new Microsoft.AspNetCore.Http.DefaultHttpContext(); if(text is not null)http.Request.QueryString=new Microsoft.AspNetCore.Http.QueryString("?value="+text);
        var error=await Assert.ThrowsAsync<Microsoft.AspNetCore.Http.BadHttpRequestException>(async()=>await contribution.Bindings.Single().BindInputAsync(http,CancellationToken.None));
        Assert.Equal(400,error.StatusCode);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Crud_optional_header_can_be_serialized_with_nullable_annotations_respected(bool restoreNullableAnnotation)
    {
        const string source="""
            using System;
            using CrestCreates.Domain.Shared.Attributes;
            using CrestCreates.Domain.Shared.Entities.Auditing;
            namespace ReviewCrud {
            public class Product : IHasConcurrencyStamp {public Guid Id {get;set;} public string Name {get;set;}=""; public string ConcurrencyStamp {get;set;}="";}
            [GenerateCrudService(typeof(Product),RoutePrefix="/api/products")] public partial class ProductAppService {}
            }
            """;
        var c=SourceGeneratorTestHelper.CreateAnalysisCompilation("Review.Crud",new[]{source},additionalReferences:new[]{"CrestCreates.Domain","CrestCreates.Application.Contracts","CrestCreates.Authorization.Abstractions","CrestCreates.Capability.Abstractions"}).RemoveAllSyntaxTrees().AddSyntaxTrees(CSharpSyntaxTree.ParseText(source));NoErrors(c);
        Assert.NotEqual(c.Assembly,c.GetTypeByMetadataName("CrestCreates.Domain.Shared.Attributes.GenerateCrudServiceAttribute")!.ContainingAssembly);
        var models=CrudGenerationModelBuilder.Build(c,"Review.Crud");
        Assert.Empty(models.Diagnostics);
        var model=Assert.Single(models.Models);
        var artifact=CrudArtifactEmitter.Emit(model).Single(a=>a.FileName==model.EnvelopeName("Delete")+".g.cs");
        var envelopeSource=artifact.Source;
        if(restoreNullableAnnotation)envelopeSource=envelopeSource.Replace("global::System.String IfMatch", "global::System.String? IfMatch");
        var envelopeType="global::"+model.EnvelopeFullName("Delete");
        var contextSource="""
            #nullable enable
            using System.Text.Json; using System.Text.Json.Serialization;
            namespace ReviewMetadata {
            [JsonSerializable(typeof(ENVELOPE))] internal partial class Context:JsonSerializerContext {}
            public static class Probe {
            public static string Serialize() {
              var options = new JsonSerializerOptions { TypeInfoResolver=Context.Default, RespectNullableAnnotations=true };
              return JsonSerializer.Serialize(new ENVELOPE(), options.GetTypeInfo(typeof(ENVELOPE)));
            }
            }
            }
            """.Replace("ENVELOPE",envelopeType);
        var input=Compilation("",new[]{new AppServiceHttpArtifact(artifact.FileName,envelopeSource)},contextSource).AddReferences(MetadataReference.CreateFromFile(typeof(System.Text.Encodings.Web.JavaScriptEncoder).Assembly.Location));
        var dotnetRoot=Directory.GetParent(typeof(object).Assembly.Location)!.Parent!.Parent!.Parent!.FullName;
        var generatorPath=Directory.EnumerateFiles(Path.Combine(dotnetRoot,"packs","Microsoft.NETCore.App.Ref"),"System.Text.Json.SourceGeneration.dll",SearchOption.AllDirectories).OrderByDescending(x=>x,StringComparer.Ordinal).First();
        var generators=new Microsoft.CodeAnalysis.Diagnostics.AnalyzerFileReference(generatorPath,new Loader()).GetGenerators(LanguageNames.CSharp); Assert.NotEmpty(generators);
        GeneratorDriver driver=CSharpGeneratorDriver.Create(generators,parseOptions:(CSharpParseOptions)input.SyntaxTrees.First().Options);
        driver=driver.RunGeneratorsAndUpdateCompilation(input,out var updated,out var generatorDiagnostics);
        Assert.Empty(generatorDiagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error));NoErrors((CSharpCompilation)updated);
        using var image=new MemoryStream();var emitted=updated.Emit(image);Assert.True(emitted.Success,string.Join(" | ",emitted.Diagnostics));
        var a=Assembly.Load(image.ToArray());var json=(string)a.GetType("ReviewMetadata.Probe")!.GetMethod("Serialize")!.Invoke(null,null)!;
        Assert.Contains("\"IfMatch\":null",json);
    }
    private sealed class Loader : IAnalyzerAssemblyLoader {
       public void AddDependencyLocation(string path){}
       public Assembly LoadFromPath(string path)=>Assembly.LoadFrom(path);
    }
}
