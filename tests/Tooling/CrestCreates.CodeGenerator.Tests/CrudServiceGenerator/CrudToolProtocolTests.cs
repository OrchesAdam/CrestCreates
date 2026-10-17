using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.CrudServiceGenerator;

public class CrudToolProtocolTests
{
    [Theory]
    [InlineData("missing", "CCCRUD008")]
    [InlineData("schema", "CCCRUD008")]
    [InlineData("owner", "CCCRUD008")]
    [InlineData("types", "CCCRUD008")]
    [InlineData("hash", "CCCRUD008")]
    [InlineData("shadow-framework", "CCCRUD003")]
    public void Json_stage_must_reject_untrusted_crud_provenance(string mutation, string code)
    {
        var directory = Path.Combine(Path.GetTempPath(), "crud-protocol-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var fixture = typeof(CrudServiceRealDeclarationBaselineTests);
            var source = (string)fixture.GetField("DeclarationSource", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
            var references = ((IReadOnlyList<MetadataReference>)fixture.GetMethod("ClosureReferences", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!)
                .Select(reference => reference.Display!).ToArray();
            var sources = new List<object> { new {Path = "Declaration.cs", Text = source} };
            var prepared = CrudPrecompileProducer.Invoke("prepare-crud", directory, new
            {
                AssemblyName="Shop.Application", Sources=sources, ReferencePaths=references,
                LangVersion="latest", Nullable="enable",
            });
            sources.Add(new {Path="CrestCreates.Crud.g.cs", Text=Encoding.UTF8.GetString(prepared.GetProperty("BundleBytes").GetBytesFromBase64())});
            var manifest = JsonNode.Parse(prepared.GetProperty("Outputs").GetRawText())!;
            var first = manifest["Entries"]![0]!;
            switch (mutation)
            {
                case "schema": manifest["SchemaVersion"]="unsupported"; break;
                case "owner": first["OwnerAssemblyName"]="Other.Assembly"; break;
                case "types": first["DeclaredTypeFullNames"]=new JsonArray("Other.Type"); break;
                case "hash": first["ContentHash"]="sha256:incorrect"; break;
                case "shadow-framework": sources.Add(new {Path="Shadow.cs", Text="namespace CrestCreates.DynamicApi { public class DynamicApiResponse {} public class DynamicApiResponse<T> {} }"}); break;
            }
            var error = Assert.Throws<InvalidOperationException>(() => CrudPrecompileProducer.Invoke("generate", directory, new
            {
                AssemblyName="Shop.Application", Sources=sources, ReferencePaths=references,
                LangVersion="latest", Nullable="enable", ManifestAccessibility="Internal",
                CrudOutputsManifestJson=mutation=="missing" ? null : manifest.ToJsonString(),
            }));
            Assert.Contains(code, error.Message);
        }
        finally { Directory.Delete(directory,true); }
    }
}
