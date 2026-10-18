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
public class ReviewS22AEmitterR1Probes
{
    private const string Preamble="using System; using System.Threading; using System.Threading.Tasks; using CrestCreates.Domain.Shared.Attributes;\n";
    private const string Stub="namespace Review { public static class JsonStub { public static System.Text.Json.Serialization.JsonSerializerContext Default = null!; } }";
    private static string Source(string members,string extra="") => Preamble+"namespace Review { public class Dto { public string Name {get;set;}=\"\"; } public class Body {public string Name {get;set;}=\"\";} public interface IService {"+members+"} [CapabilityCompatibilityProjection(ContractType=typeof(IService),RoutePrefix=\"api/items\")] public static class Anchor {} "+extra+" }";
    private sealed record Bundle(string Source,AppServiceHttpProjectionModel Model,AppServiceHttpEmissionOptions Options,IReadOnlyList<AppServiceHttpArtifact> Artifacts);
    private static Bundle BuildFull(string source,string anchor="Review.Anchor")
    {
        var c=SourceGeneratorTestHelper.CreateAnalysisCompilation("Review.Http",new[]{source});
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
    [Theory]
    [InlineData("Task<Dto> AssignAsync(Guid id, Guid agentId, CancellationToken ct=default);", "assign")]
    [InlineData("Task<Dto> ResolveAsync(Guid id, CancellationToken ct=default);", "resolve")]
    public void Frozen_query_only_commands_must_compile(string declaration,string route)
    {
        var b=Build("[AppServiceHttpAction(Verb=HttpProjectionVerb.Get,Route=\""+route+"\",Kind=HttpProjectionOperationKind.Command)] "+declaration);
        NoErrors(Compilation(b.Source,b.Artifacts));
    }
    [Theory]
    [InlineData("int")]
    [InlineData("string")]
    public void Accepted_scalar_query_model_must_materialize_without_throwing(string type)
    {
        var b=Build("[AppServiceHttpAction(Verb=HttpProjectionVerb.Get,Route=\"find\")] Task<Dto> FindAsync("+type+" value,CancellationToken ct=default);");
        Assert.NotEmpty(b.Artifacts);
    }
    [Theory]
    [InlineData("Task<int> CountAsync(CancellationToken ct=default);")]
    [InlineData("string Ping(CancellationToken ct=default);")]
    [InlineData("void Ping(CancellationToken ct=default);")]
    public void Accepted_result_and_sync_signatures_must_compile(string declaration)
    {
        var b=Build(declaration);
        NoErrors(Compilation(b.Source,b.Artifacts));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invoker_preserves_original_parameter_order(bool queryFirst)
    {
        var signature=queryFirst?"Guid queryId,Guid id,Body input,CancellationToken ct=default":"Guid id,Guid queryId,Body input,CancellationToken ct=default";
        var b=Build("[AppServiceHttpAction(Verb=HttpProjectionVerb.Post,Route=\"{id}\")] Task<string> SaveAsync("+signature+");",
            "public sealed class Service:IService {public Task<string> SaveAsync("+signature+") => Task.FromResult($\"Q:{queryId};R:{id}\");}");
        var assembly=Load(b);
        var envelope=Activator.CreateInstance(assembly.GetType(b.Model.Actions.Single().InputEnvelopeFullName!)!,nonPublic:true)!;
        var routeId=Guid.NewGuid();var queryId=Guid.NewGuid();
        envelope.GetType().GetProperty("Id")!.SetValue(envelope,routeId);
        envelope.GetType().GetProperty("QueryId")!.SetValue(envelope,queryId);
        envelope.GetType().GetProperty("Input")!.SetValue(envelope,Activator.CreateInstance(assembly.GetType("Review.Body")!));
        var services=new ServiceCollection();
        services.AddSingleton(assembly.GetType("Review.IService")!,Activator.CreateInstance(assembly.GetType("Review.Service")!)!);
        using var provider=services.BuildServiceProvider();
        var invokerType=assembly.GetTypes().Single(t=>typeof(ICapabilityContextAwareHandlerInvoker).IsAssignableFrom(t));
        var invoker=(ICapabilityContextAwareHandlerInvoker)Activator.CreateInstance(invokerType,nonPublic:true)!;
        var output=await invoker.InvokeAsync(new CapabilityExecutionContext{ServiceProvider=provider,Input=envelope},CancellationToken.None);
        Assert.Equal($"Q:{queryId};R:{routeId}",output);
    }
    [Fact]
    public void Manifest_covers_all_types_actually_declared_by_the_emitter()
    {
        var b=Build("Task<Dto> UpdateAsync(Guid id,Body input,CancellationToken ct=default);");
        var entry=AppServiceHttpProjectionEmitter.BuildManifestEntry(b.Model,b.Options);
        var actual=b.Artifacts.SelectMany(a=>CrudArtifactEmitter.DeclaredTypeFullNames(a.Source)).Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        Assert.Equal(actual,entry.DeclaredTypeFullNames.OrderBy(x=>x,StringComparer.Ordinal));
    }
    [Fact]
    public void Manifest_declared_set_matches_the_artifact_named_by_its_hint()
    {
        var b=Build("Task<Dto> UpdateAsync(Guid id,Body input,CancellationToken ct=default);");
        var entry=AppServiceHttpProjectionEmitter.BuildManifestEntry(b.Model,b.Options);
        var artifact=b.Artifacts.Single(a=>a.FileName==entry.ArtifactHintName);
        Assert.Equal(CrudArtifactEmitter.DeclaredTypeFullNames(artifact.Source).OrderBy(x=>x,StringComparer.Ordinal),entry.DeclaredTypeFullNames.OrderBy(x=>x,StringComparer.Ordinal));
    }
    [Fact]
    public void Two_contracts_in_one_owner_have_an_unambiguous_activation_call()
    {
        var source=Preamble+"namespace Review { public class Dto{} public interface IService {Task<Dto> GetByIdAsync(Guid id,CancellationToken ct=default);} public interface ISecond {Task<Dto> GetByIdAsync(Guid id,CancellationToken ct=default);} [CapabilityCompatibilityProjection(ContractType=typeof(IService),RoutePrefix=\"first\")] public static class Anchor{} [CapabilityCompatibilityProjection(ContractType=typeof(ISecond),RoutePrefix=\"second\")] public static class SecondAnchor{} }";
        var a=BuildFull(source);var b=BuildFull(source,"Review.SecondAnchor");
        var activation=AppServiceHttpProjectionEmitter.EmitAssemblyActivation(
            "Review.Http",
            new[]{(a.Model,a.Options),(b.Model,b.Options)},
            AppServiceHttpAssemblyOptions.ForAssembly("Review.Http") with {JsonContextFullName="global::Review.JsonStub"});
        var artifacts=a.Artifacts.Concat(b.Artifacts).Append(activation).ToArray();
        NoErrors(Compilation(source,artifacts));
        const string host="using Microsoft.Extensions.DependencyInjection; using CrestCreates.Generated.HttpProjection; public static class Host { public static void Boot(){new ServiceCollection().AddGeneratedAppServiceHttp();} }";
        NoErrors(Compilation(source,artifacts,host));
    }
    [Fact]
    public void Scalar_capability_input_is_a_required_JSON_root()
    {
        var b=Build("Task<Dto> GetByIdAsync(Guid id,CancellationToken ct=default);");
        var assembly=Load(b);
        var contribution=(CapabilityEndpointContribution)Activator.CreateInstance(assembly.GetType("CrestCreates.Generated.HttpProjection."+b.Options.ContributionTypeName)!,nonPublic:true)!;
        Assert.Contains(contribution.RequiredJsonRootTypes,r=>r.ClrType==typeof(Guid)&&r.Component=="CapabilityInput");
    }
    [Fact]
    public async Task Declared_IfMatch_header_survives_binding_as_typed_input()
    {
        var b=Build("[AppServiceHttpAction(Verb=HttpProjectionVerb.Delete,Route=\"{id}\",RequiresConcurrencyToken=true)] Task DeleteAsync(Guid id,CancellationToken ct=default);");
        var assembly=Load(b);
        var contribution=(CapabilityEndpointContribution)Activator.CreateInstance(assembly.GetType("CrestCreates.Generated.HttpProjection."+b.Options.ContributionTypeName)!,nonPublic:true)!;
        var context=new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.RouteValues["id"]=Guid.NewGuid().ToString();
        context.Request.Headers["If-Match"]="stamp-from-caller";
        var input=await contribution.Bindings.Single().BindInputAsync(context,CancellationToken.None);
        Assert.NotNull(input);
        Assert.Equal("stamp-from-caller",input.GetType().GetProperty("IfMatch")?.GetValue(input));
    }

}
