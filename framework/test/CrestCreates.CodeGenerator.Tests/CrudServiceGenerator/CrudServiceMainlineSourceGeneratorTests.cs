using System;
using System.Linq;
using CrestCreates.CodeGenerator.CrudServiceGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.CrudServiceGenerator;

public sealed class CrudServiceMainlineSourceGeneratorTests
{
    [Fact]
    public void GeneratedCrud_ShouldCompileForSampleEntity()
    {
        var result = RunProductGenerator();

        Assert.True(result.CompilationSuccess, string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
        Assert.True(result.ContainsFile("ProductDto.g.cs"));
        Assert.True(result.ContainsFile("CreateProductDto.g.cs"));
        Assert.True(result.ContainsFile("UpdateProductDto.g.cs"));
        Assert.True(result.ContainsFile("ProductListRequestDto.g.cs"));
        Assert.True(result.ContainsFile("IProductAppService.g.cs"));
        Assert.True(result.ContainsFile("ProductAppService.g.cs"));
        Assert.True(result.ContainsFile("ProductCrudPermissions.g.cs"));
        Assert.True(result.ContainsFile("ProductObjectMappings.g.cs"));
    }

    [Fact]
    public void GeneratedCrud_ShouldUseICrestRepositoryBase()
    {
        var result = RunProductGenerator();

        var source = result.GetSourceByFileName("ProductAppService.g.cs")!.SourceText;

        Assert.Contains("ICrestRepositoryBase<Product,", source);
        Assert.DoesNotContain("IProductRepository", source);
        Assert.DoesNotContain("ProductCrudServiceBase", source);
        Assert.DoesNotContain("abstract class ProductAppService", source);
    }

    [Fact]
    public void GeneratedCrud_Contract_ShouldUseAppServiceNaming()
    {
        var result = RunProductGenerator();

        var source = result.GetSourceByFileName("IProductAppService.g.cs")!.SourceText;

        Assert.Contains("public partial interface IProductAppService", source);
        Assert.Contains("ICrudAppService<", source);
        Assert.Contains("ProductDto", source);
        Assert.Contains("ProductListRequestDto", source);
        Assert.DoesNotContain("IProductCrudService", source);
    }

    [Fact]
    public void GeneratedCrud_ListRequest_ShouldOnlyUsePagedRequestDtoDescriptors()
    {
        var result = RunProductGenerator();

        var source = result.GetSourceByFileName("ProductListRequestDto.g.cs")!.SourceText;

        Assert.Contains("public partial class ProductListRequestDto : PagedRequestDto", source);
        Assert.DoesNotContain("Keyword", source);
        Assert.DoesNotContain("StartTime", source);
        Assert.DoesNotContain("EndTime", source);
        Assert.DoesNotContain("public string? Name", source);
        Assert.DoesNotContain("public string? Category", source);
    }

    [Fact]
    public void GeneratedCrud_ShouldGeneratePermissions()
    {
        var result = RunProductGenerator();

        Assert.True(result.ContainsFile("ProductCrudPermissions.g.cs"));
        var source = result.GetSourceByFileName("ProductCrudPermissions.g.cs")!.SourceText;

        Assert.Contains("Product.Create", source);
        Assert.Contains("Product.Get", source);
        Assert.Contains("Product.Search", source);
        Assert.Contains("Product.Update", source);
        Assert.Contains("Product.Delete", source);
        Assert.DoesNotContain("Product.View", source);
        Assert.DoesNotContain("Product.Export", source);
    }

    [Fact]
    public void GeneratedCrud_UpdateDto_ShouldIncludeConcurrencyStamp_AndCreateDtoShouldExcludeIt()
    {
        var result = RunProductGenerator();

        var updateSource = result.GetSourceByFileName("UpdateProductDto.g.cs")!.SourceText;
        var createSource = result.GetSourceByFileName("CreateProductDto.g.cs")!.SourceText;
        var outputSource = result.GetSourceByFileName("ProductDto.g.cs")!.SourceText;

        Assert.Contains("ConcurrencyStamp", updateSource);
        Assert.Contains("ConcurrencyStamp", outputSource);
        Assert.DoesNotContain("ConcurrencyStamp", createSource);
    }

    [Fact]
    public void GeneratedCrud_Delete_ShouldRequireExpectedStampForConcurrentEntity()
    {
        var result = RunProductGenerator();

        var source = result.GetSourceByFileName("ProductAppService.g.cs")!.SourceText;

        Assert.Contains("DeleteAsync(System.Guid id, string? expectedStamp = null", source);
        Assert.Contains("CrestPreconditionRequiredException", source);
    }

    [Fact]
    public void GeneratedCrud_ShouldNotWrapPlatformExceptions()
    {
        var result = RunProductGenerator();

        var source = result.GetSourceByFileName("ProductAppService.g.cs")!.SourceText;

        Assert.DoesNotContain("catch (System.Data.Common.DbException", source);
        Assert.DoesNotContain("catch (Exception ex)", source);
        Assert.DoesNotContain("throw new Exception(", source);
        Assert.Contains("CrestEntityNotFoundException", source);
    }

    private static SourceGeneratorResult RunProductGenerator()
    {
        const string source = """
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace;

[GenerateCrudService]
public class Product : AuditedAggregateRoot<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
}
""";

        const string support = """
using System;
using System.Collections.Generic;

namespace TestNamespace;

public interface IEntity<TKey>
{
    TKey Id { get; set; }
}

public interface IHasConcurrencyStamp
{
    string ConcurrencyStamp { get; set; }
}

public abstract class AuditedAggregateRoot<TKey> : IEntity<TKey>, IHasConcurrencyStamp
    where TKey : IEquatable<TKey>
{
    public TKey Id { get; set; } = default!;
    public DateTime CreationTime { get; set; }
    public Guid? CreatorId { get; set; }
    public DateTime? LastModificationTime { get; set; }
    public Guid? LastModifierId { get; set; }
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();
}

// Stub types for framework references in generated code
public interface ICrestRepositoryBase<TEntity, TKey> { }

public interface ICrudAppService<TKey, TDto, in TCreateDto, in TUpdateDto, in TListRequestDto>
    where TKey : IEquatable<TKey> { }

public class PagedRequestDto
{
    public int PageIndex { get; set; }
    public int PageSize { get; set; }
    public List<FilterDescriptor>? Filters { get; set; }
    public List<SortDescriptor>? Sorts { get; set; }
}

public class FilterDescriptor
{
    public string Field { get; set; } = string.Empty;
    public string Operator { get; set; } = "Equals";
    public object? Value { get; set; }

    public FilterDescriptor() { }
    public FilterDescriptor(string field, string @operator, object? value)
    {
        Field = field;
        Operator = @operator;
        Value = value;
    }
}

public class SortDescriptor
{
    public string Field { get; set; } = string.Empty;
    public string Direction { get; set; } = "Ascending";

    public SortDescriptor() { }
    public SortDescriptor(string field, string direction)
    {
        Field = field;
        Direction = direction;
    }
}

public class PagedResultDto<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public int TotalCount { get; set; }
    public int PageIndex { get; set; }
    public int PageSize { get; set; }

    public PagedResultDto() { }
    public PagedResultDto(IReadOnlyList<T> items, int totalCount, int pageIndex, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        PageIndex = pageIndex;
        PageSize = pageSize;
    }
}
""";

        return SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
            source,
            new[] { support });
    }
}
