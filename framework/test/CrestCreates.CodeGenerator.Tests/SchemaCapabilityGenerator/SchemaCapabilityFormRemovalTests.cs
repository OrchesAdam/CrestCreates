using CrestCreates.CodeGenerator.SchemaCapabilityGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.SchemaCapabilityGenerator;

public class SchemaCapabilityFormRemovalTests
{
    [Fact]
    public void GeneratedFormProvider_Is_Not_Emitted()
    {
        var source = """
            using CrestCreates.Form.Abstractions;
            using CrestCreates.Metadata.Abstractions;
            using CrestCreates.Schema.Abstractions;

            namespace Test;

            public class TestFormProvider : IFormDescriptorProvider
            {
                public FormDescriptor GetFormDescriptor() => new()
                {
                    Id = "form_01",
                    Name = "TestForm",
                    Version = 1,
                    Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1)
                };
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<SchemaCapabilitySourceGenerator>(
            source,
            additionalReferences: new[]
            {
                "CrestCreates.Form.Abstractions",
                "CrestCreates.Schema.Abstractions",
                "CrestCreates.Metadata.Abstractions",
                "CrestCreates.Metadata"
            });

        Assert.True(result.CompilationSuccess,
            "Test source must compile successfully.");

        foreach (var gen in result.GeneratedSources)
        {
            Assert.DoesNotContain("GeneratedFormProvider", gen.SourceText);
            Assert.DoesNotContain("new FormDescriptor", gen.SourceText);
        }
    }
}
