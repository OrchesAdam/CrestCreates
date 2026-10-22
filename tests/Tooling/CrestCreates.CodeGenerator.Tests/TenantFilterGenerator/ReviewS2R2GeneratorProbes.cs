using System;
using System.Linq;
using System.Reflection;
using CrestCreates.CodeGenerator.TenantFilterGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.TenantFilterGenerator;

public sealed class ReviewS2R2GeneratorProbes
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Full_type_identity_encoding_must_distinguish_underscore_from_namespace_separator(bool collision)
    {
        var secondNamespace = collision ? "A_B" : "A_C";
        var source = $$"""
            namespace A.B {
                [CrestCreates.Domain.Shared.Attributes.Entity]
                public class Row : CrestCreates.Domain.Shared.Entities.Auditing.ISoftDelete { public bool IsDeleted { get; set; } }
            }
            namespace {{secondNamespace}} {
                [CrestCreates.Domain.Shared.Attributes.Entity]
                public class Row : CrestCreates.Domain.Shared.Entities.Auditing.ISoftDelete { public bool IsDeleted { get; set; } }
            }
            """;
        const string soft = "namespace CrestCreates.Domain.Shared.Entities.Auditing { public interface ISoftDelete { bool IsDeleted { get; set; } } }";
        var stubs = (string[])typeof(TenantFilterSourceGeneratorTests).GetProperty("AllStubs", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var result = SourceGeneratorTestHelper.RunGenerator<TenantFilterSourceGenerator>(source, additionalSources: stubs.Append(soft).ToArray());
        Assert.True(result.CompilationSuccess, string.Join(Environment.NewLine, result.GetErrors()));
    }
}
