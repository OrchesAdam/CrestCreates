using System;
using System.Linq.Expressions;
using Xunit;
using CrestCreates.CodeGenerator.ObjectMappingGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;

namespace CrestCreates.CodeGenerator.Tests.ObjectMappingGenerator
{
    /// <summary>
    /// Runtime behavior tests for generated mappers.
    /// These tests verify that the generated code has the correct structure.
    /// </summary>
    public class ObjectMappingBehaviorTests
    {
        [Fact]
        public void ToTarget_Should_Create_New_Instance_With_Mapped_Properties()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Book
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
    }

    public class BookDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(Book), typeof(BookDto))]
    public static partial class BookToBookDtoMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert - verify generated code structure
            Assert.True(result.ContainsFile("BookToBookDtoMapper.g.cs"));
            var generatedSource = result.GetSourceByFileName("BookToBookDtoMapper.g.cs");
            Assert.NotNull(generatedSource);

            // Verify ToTarget method creates new instance with mapped properties
            Assert.Contains("public static TestNamespace.BookDto ToTarget(TestNamespace.Book source)", generatedSource.SourceText);
            Assert.Contains("var result = new TestNamespace.BookDto", generatedSource.SourceText);
            Assert.Contains("Id = source.Id", generatedSource.SourceText);
            Assert.Contains("Title = source.Title", generatedSource.SourceText);
            Assert.Contains("Author = source.Author", generatedSource.SourceText);
            Assert.Contains("return result;", generatedSource.SourceText);
        }

        [Fact]
        public void Apply_Should_Update_Existing_Instance()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class UpdateBookDto
    {
        public string Title { get; set; } = string.Empty;
    }

    public class Book
    {
        public string Title { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(UpdateBookDto), typeof(Book), Direction = MapDirection.Apply)]
    public static partial class UpdateBookDtoToBookMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert - verify generated Apply method
            Assert.True(result.ContainsFile("UpdateBookDtoToBookMapper.g.cs"));
            var generatedSource = result.GetSourceByFileName("UpdateBookDtoToBookMapper.g.cs");
            Assert.NotNull(generatedSource);

            // Verify Apply method updates existing instance
            Assert.Contains("public static void Apply(TestNamespace.UpdateBookDto source, TestNamespace.Book destination)", generatedSource.SourceText);
            Assert.Contains("destination.Title = source.Title", generatedSource.SourceText);
            Assert.Contains("BeforeApply(source, destination)", generatedSource.SourceText);
        }

        [Fact]
        public void ToTargetExpression_Should_Be_Valid_Expression()
        {
            // Arrange
            var source = @"
using System;
using System.Linq.Expressions;
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source { public int Value { get; set; } }
    public class Target { public int Value { get; set; } }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert - verify expression is correctly generated
            Assert.True(result.ContainsFile("TestMapper.g.cs"));
            var generatedSource = result.GetSourceByFileName("TestMapper.g.cs");
            Assert.NotNull(generatedSource);

            // Verify Expression type and structure
            Assert.Contains("public static Expression<Func<TestNamespace.Source, TestNamespace.Target>> ToTargetExpression", generatedSource.SourceText);
            Assert.Contains("source => new TestNamespace.Target", generatedSource.SourceText);
            Assert.Contains("Value = source.Value", generatedSource.SourceText);
        }

        [Fact]
        public void Generated_Code_Should_Not_Use_Reflection()
        {
            // Arrange
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source { public string Value { get; set; } = string.Empty; }
    public class Target { public string Value { get; set; } = string.Empty; }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            var generatedSource = result.GetSourceByFileName("TestMapper.g.cs");
            Assert.NotNull(generatedSource);

            // Verify no reflection usage - generated code should be AOT-friendly
            Assert.DoesNotContain("System.Reflection", generatedSource.SourceText);
            Assert.DoesNotContain("GetProperty", generatedSource.SourceText);
            Assert.DoesNotContain("GetTypeInfo", generatedSource.SourceText);
            Assert.DoesNotContain("Activator", generatedSource.SourceText);

            // Verify static code generation
            Assert.Contains("public static", generatedSource.SourceText);
            Assert.Contains("new TestNamespace.Target", generatedSource.SourceText);
        }
    }
}
