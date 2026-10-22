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
public class ReviewS22AEmitterR4Probes
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
    [InlineData("Box")]
    [InlineData("Nullable")]
    public void Nullable_reference_body_type_is_not_confused_with_System_Nullable(string typeName)
    {
        var b=Build("[AppServiceHttpAction(Verb=HttpProjectionVerb.Post,Route=\"save\",Body=HttpProjectionBodyPolicy.Optional)] Task<Dto> SaveAsync("+typeName+"<Body>? input,CancellationToken ct=default);",
            "public sealed class "+typeName+"<T> {public T Value {get;set;}=default!;}");
        Assert.Contains("Review."+typeName+"<",b.Model.Actions.Single().BodyTypeFullName);
        Assert.EndsWith("?",b.Model.Actions.Single().BodyTypeFullName);
        NoErrors(Compilation(b.Source,b.Artifacts));
    }
    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(false,true)]
    public async Task Synthetic_optional_IfMatch_survives_official_metadata_nullability(bool restoreNullableAnnotation,bool provideHeader)
    {
        var b=Build("[AppServiceHttpAction(Verb=HttpProjectionVerb.Delete,Route=\"{id}\",RequiresConcurrencyToken=true)] Task DeleteAsync(Guid id,CancellationToken ct=default);");
        var header=Assert.Single(b.Model.Actions.Single().Parameters.Where(p=>p.Source=="Header"));
        Assert.False(header.Required); Assert.False(header.IsContractParameter);
        var envelopeType="global::"+b.Model.Actions.Single().InputEnvelopeFullName;
        var artifacts=b.Artifacts.Select(a=>restoreNullableAnnotation ? a with {Source=a.Source.Replace("global::System.String IfMatch", "global::System.String? IfMatch")} : a).ToArray();
        var contextSource="""
            #nullable enable
            using System.Text.Json; using System.Text.Json.Serialization;
            namespace ReviewMetadata {
            [JsonSerializable(typeof(ENVELOPE))] internal partial class Context:JsonSerializerContext {}
            public static class Probe {
            public static string Serialize(object input) {
              var options = new JsonSerializerOptions { TypeInfoResolver=Context.Default, RespectNullableAnnotations=true, DefaultIgnoreCondition=JsonIgnoreCondition.Never };
              return JsonSerializer.SerializeToElement(input, options.GetTypeInfo(input.GetType())).GetRawText();
            }
            }
            }
            """.Replace("ENVELOPE",envelopeType);
        var input=Compilation(b.Source,artifacts,contextSource).AddReferences(MetadataReference.CreateFromFile(typeof(System.Text.Encodings.Web.JavaScriptEncoder).Assembly.Location));
        var dotnetRoot=Directory.GetParent(typeof(object).Assembly.Location)!.Parent!.Parent!.Parent!.FullName;
        var generatorPath=Directory.EnumerateFiles(Path.Combine(dotnetRoot,"packs","Microsoft.NETCore.App.Ref"),"System.Text.Json.SourceGeneration.dll",SearchOption.AllDirectories).OrderByDescending(x=>x,StringComparer.Ordinal).First();
        var generators=new Microsoft.CodeAnalysis.Diagnostics.AnalyzerFileReference(generatorPath,new Loader()).GetGenerators(LanguageNames.CSharp); Assert.NotEmpty(generators);
        GeneratorDriver driver=CSharpGeneratorDriver.Create(generators,parseOptions:(CSharpParseOptions)input.SyntaxTrees.First().Options);
        driver=driver.RunGeneratorsAndUpdateCompilation(input,out var updated,out var generatorDiagnostics);
        Assert.Empty(generatorDiagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error));NoErrors((CSharpCompilation)updated);
        using var image=new MemoryStream();var emitted=updated.Emit(image);Assert.True(emitted.Success,string.Join(" | ",emitted.Diagnostics));
        var a=Assembly.Load(image.ToArray());
        var contribution=(CapabilityEndpointContribution)Activator.CreateInstance(a.GetType("CrestCreates.Generated.HttpProjection."+b.Options.ContributionTypeName)!,true)!;
        var http=new Microsoft.AspNetCore.Http.DefaultHttpContext();http.Request.RouteValues["id"]=Guid.NewGuid().ToString();
        if(provideHeader)http.Request.Headers["If-Match"]="stamp";
        var bound=await contribution.Bindings.Single().BindInputAsync(http,CancellationToken.None);Assert.NotNull(bound);
        Assert.Equal(provideHeader ? "stamp" : null,bound.GetType().GetProperty("IfMatch")!.GetValue(bound));
        var json=(string)a.GetType("ReviewMetadata.Probe")!.GetMethod("Serialize")!.Invoke(null,new[]{bound})!;
        Assert.Contains(provideHeader ? "\"IfMatch\":\"stamp\"" : "\"IfMatch\":null",json);
    }
    private sealed class Loader : IAnalyzerAssemblyLoader {
       public void AddDependencyLocation(string path){}
       public Assembly LoadFromPath(string path)=>Assembly.LoadFrom(path);
    }
}
