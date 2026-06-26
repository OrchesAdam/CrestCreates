using System;
using System.Linq;
using Xunit;
using CrestCreates.CodeGenerator.ObjectMappingGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;

// semantic-string-guard: allow

namespace CrestCreates.CodeGenerator.Tests.ObjectMappingGenerator
{
    /// <summary>
    /// Mainline mapping behavior tests: DTO-side attributes, protected fields,
    /// simple conversions, navigation paths, and CRUD integration.
    /// </summary>
    public class ObjectMappingMainlineTests
    {
        [Fact]
        public void MapFrom_ShouldGenerateExpectedAssignment()
        {
            // Arrange
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Book { public string BookTitle { get; set; } = string.Empty; }
    public class BookDto
    {
        [MapFrom(""BookTitle"")]
        public string Title { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(Book), typeof(BookDto))]
    public static partial class BookMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            Assert.True(result.ContainsFile("BookMapper.g.cs"));
            var generatedSource = result.GetSourceByFileName("BookMapper.g.cs");
            Assert.NotNull(generatedSource);
            Assert.Contains("Title = source.BookTitle", generatedSource.SourceText);
        }

        [Fact]
        public void MapIgnore_ShouldSkipProperty()
        {
            // Arrange
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Book { public string Title { get; set; } = string.Empty; public string InternalCode { get; set; } = string.Empty; }
    public class BookDto
    {
        public string Title { get; set; } = string.Empty;
        [MapIgnore]
        public string InternalCode { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(Book), typeof(BookDto))]
    public static partial class BookMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            var generatedSource = result.GetSourceByFileName("BookMapper.g.cs");
            Assert.NotNull(generatedSource);
            Assert.Contains("Title = source.Title", generatedSource.SourceText);
            Assert.DoesNotContain("InternalCode = source.InternalCode", generatedSource.SourceText);
        }

        [Fact]
        public void MapName_ShouldMapInputDtoPropertyToEntityProperty()
        {
            // Arrange
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class CreateBookDto
    {
        [MapName(""BookTitle"")]
        public string Title { get; set; } = string.Empty;
    }
    public class Book { public string BookTitle { get; set; } = string.Empty; }

    [GenerateObjectMapping(typeof(CreateBookDto), typeof(Book), Direction = MapDirection.Create)]
    public static partial class CreateBookMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            var generatedSource = result.GetSourceByFileName("CreateBookMapper.g.cs");
            Assert.NotNull(generatedSource);
            Assert.Contains("BookTitle = source.Title", generatedSource.SourceText);
        }

        [Fact]
        public void CreateDtoToEntity_ShouldNotAssignProtectedFields()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class CreateBookDto
    {
        public string Title { get; set; } = string.Empty;
    }

    public class Book
    {
        public Guid Id { get; set; }
        public string? TenantId { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTime CreationTime { get; set; }
        public Guid? CreatorId { get; set; }
        public DateTime? LastModificationTime { get; set; }
        public Guid? LastModifierId { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletionTime { get; set; }
        public Guid? DeleterId { get; set; }
        public string ConcurrencyStamp { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(CreateBookDto), typeof(Book), Direction = MapDirection.Create)]
    public static partial class CreateBookMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            Assert.True(result.ContainsFile("CreateBookMapper.g.cs"));
            var generatedSource = result.GetSourceByFileName("CreateBookMapper.g.cs");
            Assert.NotNull(generatedSource);

            Assert.Contains("Title = source.Title", generatedSource.SourceText);
            Assert.DoesNotContain("Id =", generatedSource.SourceText);
            Assert.DoesNotContain("TenantId =", generatedSource.SourceText);
            Assert.DoesNotContain("CreationTime =", generatedSource.SourceText);
            Assert.DoesNotContain("CreatorId =", generatedSource.SourceText);
            Assert.DoesNotContain("LastModificationTime =", generatedSource.SourceText);
            Assert.DoesNotContain("LastModifierId =", generatedSource.SourceText);
            Assert.DoesNotContain("IsDeleted =", generatedSource.SourceText);
            Assert.DoesNotContain("DeletionTime =", generatedSource.SourceText);
            Assert.DoesNotContain("DeleterId =", generatedSource.SourceText);
            Assert.DoesNotContain("ConcurrencyStamp =", generatedSource.SourceText);
        }

        [Fact]
        public void UpdateDtoApplyTo_ShouldNotOverwriteProtectedFields()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class UpdateBookDto { public string Title { get; set; } = string.Empty; }
    public class Book
    {
        public Guid Id { get; set; }
        public string? TenantId { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTime CreationTime { get; set; }
        public Guid? CreatorId { get; set; }
        public DateTime? LastModificationTime { get; set; }
        public Guid? LastModifierId { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletionTime { get; set; }
        public Guid? DeleterId { get; set; }
        public string ConcurrencyStamp { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(UpdateBookDto), typeof(Book), Direction = MapDirection.Apply)]
    public static partial class UpdateBookMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            Assert.True(result.ContainsFile("UpdateBookMapper.g.cs"));
            var generatedSource = result.GetSourceByFileName("UpdateBookMapper.g.cs");
            Assert.NotNull(generatedSource);

            // Title should be mapped
            Assert.Contains("destination.Title = source.Title", generatedSource.SourceText);
            // Protected fields should NOT be assigned
            Assert.DoesNotContain("destination.Id", generatedSource.SourceText);
            Assert.DoesNotContain("destination.TenantId", generatedSource.SourceText);
            Assert.DoesNotContain("destination.CreationTime", generatedSource.SourceText);
            Assert.DoesNotContain("destination.CreatorId", generatedSource.SourceText);
            Assert.DoesNotContain("destination.LastModificationTime", generatedSource.SourceText);
            Assert.DoesNotContain("destination.LastModifierId", generatedSource.SourceText);
            Assert.DoesNotContain("destination.IsDeleted", generatedSource.SourceText);
            Assert.DoesNotContain("destination.DeletionTime", generatedSource.SourceText);
            Assert.DoesNotContain("destination.DeleterId", generatedSource.SourceText);
            Assert.DoesNotContain("destination.ConcurrencyStamp", generatedSource.SourceText);
        }

        [Fact]
        public void ProtectedInputField_ShouldEmitOM009()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class UpdateDto
    {
        [MapName(""CreationTime"")]
        public DateTime ClientCreationTime { get; set; }
    }
    public class Entity
    {
        public string Name { get; set; } = string.Empty;
        public DateTime CreationTime { get; set; }
    }

    [GenerateObjectMapping(typeof(UpdateDto), typeof(Entity), Direction = MapDirection.Apply)]
    public static partial class TestMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            var errors = result.GetErrors().ToList();
            Assert.Contains(errors, e => e.Id == "OM009");
        }

        [Fact]
        public void EnumToString_ShouldGenerateConversionExpression()
        {
            // Arrange
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public enum BookStatus { Available, CheckedOut }
    public class Book { public BookStatus Status { get; set; } }
    public class BookDto { public string Status { get; set; } = string.Empty; }

    [GenerateObjectMapping(typeof(Book), typeof(BookDto))]
    public static partial class BookMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            var generatedSource = result.GetSourceByFileName("BookMapper.g.cs");
            Assert.NotNull(generatedSource);
            Assert.Contains("Status = source.Status.ToString()", generatedSource.SourceText);
        }

        [Fact]
        public void NavigationPath_ShouldGenerateNullSafeExpression()
        {
            // Arrange
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Category { public string Name { get; set; } = string.Empty; }
    public class Book { public Category Category { get; set; } = new(); }
    public class BookDto
    {
        [MapFrom(""Category.Name"")]
        public string CategoryName { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(Book), typeof(BookDto))]
    public static partial class BookMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            var generatedSource = result.GetSourceByFileName("BookMapper.g.cs");
            Assert.NotNull(generatedSource);
            // Should use null-safe access for navigation
            Assert.Contains("CategoryName = source.Category?.Name", generatedSource.SourceText);
        }

        [Fact]
        public void MapConvert_ShouldInvokeCustomConverter()
        {
            // Arrange
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public static class MyConverter { public static string Convert(int v) => v.ToString(); }
    public class Book { public int Pages { get; set; } }
    public class BookDto
    {
        [MapConvert(typeof(MyConverter))]
        public string Pages { get; set; } = string.Empty;
    }

    [GenerateObjectMapping(typeof(Book), typeof(BookDto))]
    public static partial class BookMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            var generatedSource = result.GetSourceByFileName("BookMapper.g.cs");
            Assert.NotNull(generatedSource);
            Assert.Contains("TestNamespace.MyConverter.Convert(source.Pages)", generatedSource.SourceText);
        }

        [Fact]
        public void MultiDeclaration_ShouldGenerateSeparateFiles()
        {
            // Arrange
            var source = @"
using CrestCreates.Domain.Shared.ObjectMapping;

namespace TestNamespace
{
    public class Book { public string Title { get; set; } = string.Empty; }
    public class BookDto { public string Title { get; set; } = string.Empty; }
    public class Author { public string Name { get; set; } = string.Empty; }
    public class AuthorDto { public string Name { get; set; } = string.Empty; }

    [GenerateObjectMapping(typeof(Book), typeof(BookDto))]
    [GenerateObjectMapping(typeof(Author), typeof(AuthorDto))]
    public static partial class MyMapper { }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<ObjectMappingSourceGenerator>(source);

            // Assert
            Assert.True(result.GeneratedSources.Count >= 2, "Should generate at least two files for two declarations");
        }
    }
}
