using System;
using System.Linq;
using System.Reflection;
using CrestCreates.CodeGenerator.TenantFilterGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.TenantFilterGenerator;

public class ReviewS2GeneratorProbes
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Soft_entities_in_different_namespaces_must_compile(bool sameSimpleName)
    {
        var secondName = sameSimpleName ? "Row" : "OtherRow";
        var source = $$"""
            namespace Alpha {
                [CrestCreates.Domain.Shared.Attributes.Entity]
                public class Row : CrestCreates.Domain.Shared.Entities.Auditing.ISoftDelete { public bool IsDeleted { get; set; } }
            }
            namespace Beta {
                [CrestCreates.Domain.Shared.Attributes.Entity]
                public class {{secondName}} : CrestCreates.Domain.Shared.Entities.Auditing.ISoftDelete { public bool IsDeleted { get; set; } }
            }
            """;
        const string soft = "namespace CrestCreates.Domain.Shared.Entities.Auditing { public interface ISoftDelete { bool IsDeleted { get; set; } } }";
        var stubs = (string[])typeof(TenantFilterSourceGeneratorTests).GetProperty("AllStubs", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var result = SourceGeneratorTestHelper.RunGenerator<TenantFilterSourceGenerator>(source, additionalSources: stubs.Append(soft).ToArray());
        Assert.True(result.CompilationSuccess, string.Join(Environment.NewLine, result.GetErrors()));
    }
}
