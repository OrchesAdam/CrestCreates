using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Xunit;
using Xunit.Abstractions;
namespace CrestCreates.CodeGenerator.Tests.CrudServiceGenerator;
public class ReviewR5ResponseRootProbes
{
 readonly ITestOutputHelper output;
 public ReviewR5ResponseRootProbes(ITestOutputHelper output) {this.output=output;}
 static readonly CrudFormalContractProbe.Model Model = new("ProductAppService","Product","Shop.Application.Products");
 const string Ns="Shop.Application.Products.Contracts.";
 const string Page="global::CrestCreates.Application.Contracts.DTOs.Common.PagedResultDto<global::Shop.Application.Products.Contracts.ProductDto>";
 const string PagedEnvelope="global::CrestCreates.DynamicApi.DynamicApiResponse<"+Page+">";
 static string Attr(string t)=>"[global::System.Text.Json.Serialization.JsonSerializable(typeof("+t+"))]\n";
 static string Source(string? missing=null)
 {
  var dtos=new[]{"ProductDto","CreateProductDto","UpdateProductDto","ProductListRequestDto"};
  var declarations=string.Join("\n",dtos.Where(d=>d!=missing).Select(d=>"public class "+d+" {}"));
  var roots=string.Join("",dtos.Where(d=>d!=missing).Select(d=>Attr("global::"+Ns+d)))
   +Attr(Page)+Attr("global::CrestCreates.DynamicApi.DynamicApiResponse<global::"+Ns+"ProductDto>")
   +Attr("global::CrestCreates.DynamicApi.DynamicApiResponse")
   +(missing=="paged-envelope"?"":Attr(PagedEnvelope));
  return "namespace Shop.Application.Products.Contracts {"+declarations+"}\nnamespace CrestCreates.Generated {\n"+roots
   +"internal abstract partial class FullJsonContext : global::System.Text.Json.Serialization.JsonSerializerContext { protected FullJsonContext(global::System.Text.Json.JsonSerializerOptions options):base(options) {} } }";
 }
 IReadOnlyList<string> Violations(string? missing=null)
 {
  var refs=(IReadOnlyList<MetadataReference>)typeof(CrudServiceRealDeclarationBaselineTests).GetMethod("ClosureReferences",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,null)!;
  var c=CrudFormalContractProbe.AuthoredCompilation(Source(missing),refs);
  Assert.Empty(CrudFormalContractProbe.ClosureViolations(c));
  Assert.Empty(c.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error));
  var result=CrudFormalContractProbe.ResponseRootViolations(c,c.SyntaxTrees.ToArray(),Model);
  output.WriteLine(string.Join("\n",result));
  return result;
 }
 [Fact] public void Complete_response_root_model_must_pass() => Assert.Empty(Violations());
 [Theory]
 [InlineData("paged-envelope")]
 public void Missing_required_contract_must_be_rejected(string missing) => Assert.NotEmpty(Violations(missing));

 [Fact] public void Complete_input_envelope_model_must_pass()
 {
  const string update="global::CrestCreates.Generated.ProductAppService_Update_CompatibilityInput";
  const string delete="global::CrestCreates.Generated.ProductAppService_Delete_CompatibilityInput";
  var source=Source().Replace("internal abstract partial class FullJsonContext",Attr(update)+Attr(delete)+"internal abstract partial class FullJsonContext")
   +"namespace CrestCreates.Generated { internal sealed class ProductAppService_Update_CompatibilityInput { public global::System.Guid Id {get;init;} public global::Shop.Application.Products.Contracts.UpdateProductDto Input {get;init;} = default!; } internal sealed class ProductAppService_Delete_CompatibilityInput { public global::System.Guid Id {get;init;} public string? IfMatch {get;init;} } }";
  var refs=(IReadOnlyList<MetadataReference>)typeof(CrudServiceRealDeclarationBaselineTests).GetMethod("ClosureReferences",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,null)!;
  var c=CrudFormalContractProbe.AuthoredCompilation(source,refs);
  Assert.Empty(CrudFormalContractProbe.ClosureViolations(c));
  Assert.Empty(c.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error));
  Assert.Empty(CrudFormalContractProbe.InputEnvelopeViolations(c,c.SyntaxTrees.ToArray(),Model));
 }
}
