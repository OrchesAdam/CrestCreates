using System;
using System.Linq;
using Xunit;
using CrestCreates.CodeGenerator.ObjectMappingGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;

namespace CrestCreates.CodeGenerator.Tests.ObjectMappingGenerator
{
    public class ObjectMappingSourceGeneratorTests
    {
        [Fact]
        public void Should_Generate_ToTarget_Method()
        {
            // Arrange
            var source = @"
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

            // Assert
            Assert.True(result.ContainsFile("BookToBookDtoMapper.g.cs"));
            var generatedSource = result.GetSourceByFileName("BookToBookDtoMapper.g.cs");
            Assert.NotNull(generatedSource);
            Assert.Contains("ToTarget(TestNamespace.Book source)", generatedSource.SourceText);
            Assert.Contains("Apply(TestNamespace.Book source, TestNamespace.BookDto destination)", generatedSource.SourceText);
            Assert.Contains("Expression<Func<TestNamespace.Book, TestNamespace.BookDto>>", generatedSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Only_Create_Method_When_Direction_Is_Create()
        {
            // Arrange
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class CreateBookDto
    {
        public string Title { get; set; } = string.Empty;
    }

    public class Book
    {
        public string Title { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(CreateBookDto), typeof(Book), Direction = MapDirection.Create)]
    public static partial class CreateBookDtoToBookMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            var generatedSource = result.GetSourceByFileName("CreateBookDtoToBookMapper.g.cs");
            Assert.NotNull(generatedSource);
            Assert.Contains("ToTarget(TestNamespace.CreateBookDto source)", generatedSource.SourceText);
            Assert.DoesNotContain("public static void Apply", generatedSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Only_Apply_Method_When_Direction_Is_Apply()
        {
            // Arrange
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class UpdateBookDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    public class Book
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(UpdateBookDto), typeof(Book), Direction = MapDirection.Apply)]
    public static partial class UpdateBookDtoToBookMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            var generatedSource = result.GetSourceByFileName("UpdateBookDtoToBookMapper.g.cs");
            Assert.NotNull(generatedSource);
            Assert.DoesNotContain("ToTarget(TestNamespace.UpdateBookDto source)", generatedSource.SourceText);
            Assert.Contains("Apply(TestNamespace.UpdateBookDto source, TestNamespace.Book destination)", generatedSource.SourceText);
        }

        [Fact]
        public void Should_Map_Properties_With_Same_Name()
        {
            // Arrange
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    public class Target
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class SourceToTargetMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            var generatedSource = result.GetSourceByFileName("SourceToTargetMapper.g.cs");
            Assert.NotNull(generatedSource);
            Assert.Contains("Name = source.Name", generatedSource.SourceText);
            Assert.Contains("Age = source.Age", generatedSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Partial_Hooks()
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
            Assert.Contains("partial void AfterToTarget(TestNamespace.Source source, TestNamespace.Target destination)", generatedSource.SourceText);
            Assert.Contains("partial void BeforeApply(TestNamespace.Source source, TestNamespace.Target destination)", generatedSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Null_Checks()
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
            Assert.Contains("if (source is null)", generatedSource.SourceText);
            Assert.Contains("throw new ArgumentNullException(nameof(source))", generatedSource.SourceText);
        }

        [Fact]
        public void Should_Respect_MapIgnore_Attribute()
        {
            // Arrange
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source
    {
        public string Name { get; set; } = string.Empty;
        public string Secret { get; set; } = string.Empty;
    }

    public class Target
    {
        public string Name { get; set; } = string.Empty;

        [MapIgnore]
        public string Secret { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            var generatedSource = result.GetSourceByFileName("TestMapper.g.cs");
            Assert.NotNull(generatedSource);
            Assert.Contains("Name = source.Name", generatedSource.SourceText);
            // Secret should not be mapped
            var lines = generatedSource.SourceText.Split('\n');
            var mappingLines = lines.Where(l => l.Contains("= source.")).ToList();
            Assert.DoesNotContain(mappingLines, l => l.Contains("Secret"));
        }

        [Fact]
        public void Should_Respect_MapName_Attribute()
        {
            // Arrange
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source
    {
        public string FullName { get; set; } = string.Empty;
    }

    public class Target
    {
        [MapName(""FullName"")]
        public string Name { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            var generatedSource = result.GetSourceByFileName("TestMapper.g.cs");
            Assert.NotNull(generatedSource);
            Assert.Contains("Name = source.FullName", generatedSource.SourceText);
        }

        [Fact]
        public void Should_Respect_MapFrom_Attribute()
        {
            // Arrange
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source
    {
        public string Title { get; set; } = string.Empty;
    }

    public class Target
    {
        [MapFrom(nameof(Source.Title))]
        public string Name { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            var generatedSource = result.GetSourceByFileName("TestMapper.g.cs");
            Assert.NotNull(generatedSource);
            Assert.Contains("Name = source.Title", generatedSource.SourceText);
        }

        [Fact]
        public void Should_Generate_AfterApply_Hook()
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
            Assert.Contains("partial void AfterApply(TestNamespace.Source source, TestNamespace.Target destination)", generatedSource.SourceText);
            Assert.Contains("AfterApply(source, destination)", generatedSource.SourceText);
        }

        [Fact]
        public void Should_Generate_NullSafe_Code_For_Nullable_To_NonNullable()
        {
            // Arrange
            var source = @"
#nullable enable
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source
    {
        public int? Count { get; set; }
    }

    public class Target
    {
        public int Count { get; set; }
    }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            var generatedSource = result.GetSourceByFileName("TestMapper.g.cs");
            Assert.NotNull(generatedSource);

            // Print the generated source for debugging
            var sourceText = generatedSource.SourceText;

            // Should generate null-coalescing for nullable-to-non-nullable value types
            Assert.Contains("Count = source.Count ?? 0", sourceText);
        }

        [Fact]
        public void Should_Map_Collection_Properties()
        {
            // Arrange
            var source = @"
using System.Collections.Generic;
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source
    {
        public List<string> Tags { get; set; } = new();
        public int[] Numbers { get; set; } = System.Array.Empty<int>();
    }

    public class Target
    {
        public List<string> Tags { get; set; } = new();
        public int[] Numbers { get; set; } = System.Array.Empty<int>();
    }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            var generatedSource = result.GetSourceByFileName("TestMapper.g.cs");
            Assert.NotNull(generatedSource);
            Assert.Contains("Tags = source.Tags", generatedSource.SourceText);
            Assert.Contains("Numbers = source.Numbers", generatedSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Direct_Assignment_For_NonNullable_To_Nullable()
        {
            // Arrange
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Source
    {
        public int Count { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class Target
    {
        public int? Count { get; set; }
        public string? Name { get; set; }
    }

    [GenerateObjectMapping(typeof(Source), typeof(Target))]
    public static partial class TestMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            var generatedSource = result.GetSourceByFileName("TestMapper.g.cs");
            Assert.NotNull(generatedSource);
            // Non-nullable to nullable should be direct assignment
            Assert.Contains("Count = source.Count", generatedSource.SourceText);
            Assert.Contains("Name = source.Name", generatedSource.SourceText);
        }
    }
}
