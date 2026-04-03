using System;
using System.Linq;
using Xunit;
using CrestCreates.CodeGenerator.QueryBuilderGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;

namespace CrestCreates.CodeGenerator.Tests.QueryBuilderGenerator
{
    /// <summary>
    /// 查询构建器源代码生成器测试
    /// </summary>
    public class QueryBuilderSourceGeneratorTests
    {
        #region 过滤构建器生成测试

        [Fact]
        public void Should_Generate_Filter_Builder()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
        public decimal Price { get; set; }
        public int StockQuantity { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            Assert.True(result.ContainsFile("ProductFilterBuilder.g.cs"));
            var filterSource = result.GetSourceByFileName("ProductFilterBuilder.g.cs");
            Assert.NotNull(filterSource);
            Assert.Contains("class ProductFilterBuilder", filterSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Filter_Builder_With_Equality_Methods()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
        public decimal Price { get; set; }
        public int StockQuantity { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var filterSource = result.GetSourceByFileName("ProductFilterBuilder.g.cs");
            Assert.NotNull(filterSource);
            Assert.Contains("public ProductFilterBuilder WhereName(string value)", filterSource.SourceText);
            Assert.Contains("public ProductFilterBuilder WherePrice(decimal value)", filterSource.SourceText);
            Assert.Contains("public ProductFilterBuilder WhereStockQuantity(int value)", filterSource.SourceText);
            Assert.Contains("FilterOperator.Equals", filterSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Filter_Builder_With_String_Methods()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
        public string Description { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var filterSource = result.GetSourceByFileName("ProductFilterBuilder.g.cs");
            Assert.NotNull(filterSource);
            Assert.Contains("public ProductFilterBuilder WhereNameContains(string value)", filterSource.SourceText);
            Assert.Contains("public ProductFilterBuilder WhereNameStartsWith(string value)", filterSource.SourceText);
            Assert.Contains("public ProductFilterBuilder WhereNameEndsWith(string value)", filterSource.SourceText);
            Assert.Contains("FilterOperator.Contains", filterSource.SourceText);
            Assert.Contains("FilterOperator.StartsWith", filterSource.SourceText);
            Assert.Contains("FilterOperator.EndsWith", filterSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Filter_Builder_With_Range_Methods()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
        public decimal Price { get; set; }
        public int StockQuantity { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var filterSource = result.GetSourceByFileName("ProductFilterBuilder.g.cs");
            Assert.NotNull(filterSource);
            Assert.Contains("public ProductFilterBuilder WherePriceBetween(decimal from, decimal to)", filterSource.SourceText);
            Assert.Contains("public ProductFilterBuilder WherePriceGreaterThan(decimal value)", filterSource.SourceText);
            Assert.Contains("public ProductFilterBuilder WherePriceLessThan(decimal value)", filterSource.SourceText);
            Assert.Contains("public ProductFilterBuilder WhereCreatedAtBetween(DateTime from, DateTime to)", filterSource.SourceText);
            Assert.Contains("FilterOperator.GreaterThan", filterSource.SourceText);
            Assert.Contains("FilterOperator.LessThan", filterSource.SourceText);
            Assert.Contains("FilterOperator.GreaterThanOrEqual", filterSource.SourceText);
            Assert.Contains("FilterOperator.LessThanOrEqual", filterSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Filter_Builder_With_Build_Method()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var filterSource = result.GetSourceByFileName("ProductFilterBuilder.g.cs");
            Assert.NotNull(filterSource);
            Assert.Contains("public List<FilterDescriptor> Build()", filterSource.SourceText);
            Assert.Contains("return new List<FilterDescriptor>(_filters)", filterSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Filter_Builder_With_Clear_Method()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var filterSource = result.GetSourceByFileName("ProductFilterBuilder.g.cs");
            Assert.NotNull(filterSource);
            Assert.Contains("public ProductFilterBuilder Clear()", filterSource.SourceText);
            Assert.Contains("_filters.Clear()", filterSource.SourceText);
        }

        #endregion

        #region 排序构建器生成测试

        [Fact]
        public void Should_Generate_Sort_Builder()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
        public decimal Price { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            Assert.True(result.ContainsFile("ProductSortBuilder.g.cs"));
            var sortSource = result.GetSourceByFileName("ProductSortBuilder.g.cs");
            Assert.NotNull(sortSource);
            Assert.Contains("class ProductSortBuilder", sortSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Sort_Builder_With_OrderBy_Methods()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
        public decimal Price { get; set; }
        public int StockQuantity { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var sortSource = result.GetSourceByFileName("ProductSortBuilder.g.cs");
            Assert.NotNull(sortSource);
            Assert.Contains("public ProductSortBuilder OrderByName()", sortSource.SourceText);
            Assert.Contains("public ProductSortBuilder OrderByPrice()", sortSource.SourceText);
            Assert.Contains("public ProductSortBuilder OrderByStockQuantity()", sortSource.SourceText);
            Assert.Contains("SortDirection.Ascending", sortSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Sort_Builder_With_OrderByDescending_Methods()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
        public decimal Price { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var sortSource = result.GetSourceByFileName("ProductSortBuilder.g.cs");
            Assert.NotNull(sortSource);
            Assert.Contains("public ProductSortBuilder OrderByNameDescending()", sortSource.SourceText);
            Assert.Contains("public ProductSortBuilder OrderByPriceDescending()", sortSource.SourceText);
            Assert.Contains("SortDirection.Descending", sortSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Sort_Builder_With_ThenBy_Methods()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
        public decimal Price { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var sortSource = result.GetSourceByFileName("ProductSortBuilder.g.cs");
            Assert.NotNull(sortSource);
            Assert.Contains("public ProductSortBuilder ThenByName()", sortSource.SourceText);
            Assert.Contains("public ProductSortBuilder ThenByNameDescending()", sortSource.SourceText);
            Assert.Contains("public ProductSortBuilder ThenByPrice()", sortSource.SourceText);
            Assert.Contains("public ProductSortBuilder ThenByPriceDescending()", sortSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Sort_Builder_With_Build_Method()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var sortSource = result.GetSourceByFileName("ProductSortBuilder.g.cs");
            Assert.NotNull(sortSource);
            Assert.Contains("public List<SortDescriptor> Build()", sortSource.SourceText);
            Assert.Contains("return new List<SortDescriptor>(_sorts)", sortSource.SourceText);
        }

        #endregion

        #region 查询请求生成测试

        [Fact]
        public void Should_Generate_Query_Request()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            Assert.True(result.ContainsFile("ProductQueryRequest.g.cs"));
            var requestSource = result.GetSourceByFileName("ProductQueryRequest.g.cs");
            Assert.NotNull(requestSource);
            Assert.Contains("class ProductQueryRequest", requestSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Query_Request_With_Properties()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var requestSource = result.GetSourceByFileName("ProductQueryRequest.g.cs");
            Assert.NotNull(requestSource);
            Assert.Contains("public List<FilterDescriptor> Filters { get; set; } = new()", requestSource.SourceText);
            Assert.Contains("public List<SortDescriptor> Sorts { get; set; } = new()", requestSource.SourceText);
            Assert.Contains("public PagedRequestDto Paging { get; set; } = new()", requestSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Query_Request_With_Constructors()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var requestSource = result.GetSourceByFileName("ProductQueryRequest.g.cs");
            Assert.NotNull(requestSource);
            Assert.Contains("public ProductQueryRequest()", requestSource.SourceText);
            Assert.Contains("public ProductQueryRequest(ProductFilterBuilder filterBuilder)", requestSource.SourceText);
            Assert.Contains("public ProductQueryRequest(ProductSortBuilder sortBuilder)", requestSource.SourceText);
            Assert.Contains("public ProductQueryRequest(ProductFilterBuilder filterBuilder, ProductSortBuilder sortBuilder)", requestSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Query_Request_With_Paging_Method()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var requestSource = result.GetSourceByFileName("ProductQueryRequest.g.cs");
            Assert.NotNull(requestSource);
            Assert.Contains("public ProductQueryRequest WithPaging(int pageIndex, int pageSize)", requestSource.SourceText);
            Assert.Contains("Paging.PageIndex = pageIndex", requestSource.SourceText);
            Assert.Contains("Paging.PageSize = pageSize", requestSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Query_Request_With_Add_Methods()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var requestSource = result.GetSourceByFileName("ProductQueryRequest.g.cs");
            Assert.NotNull(requestSource);
            Assert.Contains("public ProductQueryRequest AddFilter(FilterDescriptor filter)", requestSource.SourceText);
            Assert.Contains("public ProductQueryRequest AddSort(SortDescriptor sort)", requestSource.SourceText);
            Assert.Contains("Filters.Add(filter)", requestSource.SourceText);
            Assert.Contains("Sorts.Add(sort)", requestSource.SourceText);
        }

        #endregion

        #region 查询执行器生成测试

        [Fact]
        public void Should_Generate_Query_Executor()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            Assert.True(result.ContainsFile("ProductQueryExecutor.g.cs"));
            var executorSource = result.GetSourceByFileName("ProductQueryExecutor.g.cs");
            Assert.NotNull(executorSource);
            Assert.Contains("class ProductQueryExecutor", executorSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Query_Executor_With_ApplyFilters()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var executorSource = result.GetSourceByFileName("ProductQueryExecutor.g.cs");
            Assert.NotNull(executorSource);
            Assert.Contains("public static IQueryable<Product> ApplyFilters(IQueryable<Product> query, ProductQueryRequest request)", executorSource.SourceText);
            Assert.Contains("ApplyFilter", executorSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Query_Executor_With_ApplySorts()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var executorSource = result.GetSourceByFileName("ProductQueryExecutor.g.cs");
            Assert.NotNull(executorSource);
            Assert.Contains("public static IQueryable<Product> ApplySorts(IQueryable<Product> query, ProductQueryRequest request)", executorSource.SourceText);
            Assert.Contains("ApplySort", executorSource.SourceText);
            Assert.Contains("OrderBy", executorSource.SourceText);
            Assert.Contains("ThenBy", executorSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Query_Executor_With_ApplyPaging()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var executorSource = result.GetSourceByFileName("ProductQueryExecutor.g.cs");
            Assert.NotNull(executorSource);
            Assert.Contains("public static IQueryable<Product> ApplyPaging(IQueryable<Product> query, ProductQueryRequest request)", executorSource.SourceText);
            Assert.Contains("Skip", executorSource.SourceText);
            Assert.Contains("Take", executorSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Query_Executor_With_Execute_Method()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var executorSource = result.GetSourceByFileName("ProductQueryExecutor.g.cs");
            Assert.NotNull(executorSource);
            Assert.Contains("public static IQueryable<Product> Execute(IQueryable<Product> query, ProductQueryRequest request)", executorSource.SourceText);
            Assert.Contains("ApplyFilters", executorSource.SourceText);
            Assert.Contains("ApplySorts", executorSource.SourceText);
            Assert.Contains("ApplyPaging", executorSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Query_Executor_With_Count_Methods()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var executorSource = result.GetSourceByFileName("ProductQueryExecutor.g.cs");
            Assert.NotNull(executorSource);
            Assert.Contains("public static int GetTotalCount(IQueryable<Product> query, ProductQueryRequest request)", executorSource.SourceText);
            Assert.Contains("public static async Task<int> GetTotalCountAsync(IQueryable<Product> query, ProductQueryRequest request", executorSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Query_Executor_With_Filter_Operators()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var executorSource = result.GetSourceByFileName("ProductQueryExecutor.g.cs");
            Assert.NotNull(executorSource);
            Assert.Contains("case FilterOperator.Equals:", executorSource.SourceText);
            Assert.Contains("case FilterOperator.NotEquals:", executorSource.SourceText);
            Assert.Contains("case FilterOperator.GreaterThan:", executorSource.SourceText);
            Assert.Contains("case FilterOperator.GreaterThanOrEqual:", executorSource.SourceText);
            Assert.Contains("case FilterOperator.LessThan:", executorSource.SourceText);
            Assert.Contains("case FilterOperator.LessThanOrEqual:", executorSource.SourceText);
            Assert.Contains("case FilterOperator.Contains:", executorSource.SourceText);
            Assert.Contains("case FilterOperator.StartsWith:", executorSource.SourceText);
            Assert.Contains("case FilterOperator.EndsWith:", executorSource.SourceText);
        }

        #endregion

        #region 多实体测试

        [Fact]
        public void Should_Generate_Query_Builders_For_Multiple_Entities()
        {
            // Arrange
            var source1 = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
    }
}
";

            var source2 = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Category : Entity<Guid>
    {
        public string Name { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                new[] { source1, source2 },
                new[] { entitySource });

            // Assert
            Assert.True(result.ContainsFile("ProductFilterBuilder.g.cs"));
            Assert.True(result.ContainsFile("CategoryFilterBuilder.g.cs"));
            Assert.True(result.ContainsFile("ProductSortBuilder.g.cs"));
            Assert.True(result.ContainsFile("CategorySortBuilder.g.cs"));
            Assert.True(result.ContainsFile("ProductQueryRequest.g.cs"));
            Assert.True(result.ContainsFile("CategoryQueryRequest.g.cs"));
            Assert.True(result.ContainsFile("ProductQueryExecutor.g.cs"));
            Assert.True(result.ContainsFile("CategoryQueryExecutor.g.cs"));
        }

        #endregion

        #region 复杂属性类型测试

        [Fact]
        public void Should_Skip_Navigation_Properties()
        {
            // Arrange
            var source = @"
using System;
using System.Collections.Generic;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
        public Category Category { get; set; }
        public List<OrderItem> OrderItems { get; set; }
    }

    public class Category { }
    public class OrderItem { }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var filterSource = result.GetSourceByFileName("ProductFilterBuilder.g.cs");
            Assert.NotNull(filterSource);
            Assert.Contains("WhereName", filterSource.SourceText);
            Assert.DoesNotContain("WhereCategory", filterSource.SourceText);
            Assert.DoesNotContain("WhereOrderItems", filterSource.SourceText);
        }

        [Fact]
        public void Should_Include_Simple_Types()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public double Weight { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid CategoryId { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var filterSource = result.GetSourceByFileName("ProductFilterBuilder.g.cs");
            Assert.NotNull(filterSource);
            Assert.Contains("WhereName", filterSource.SourceText);
            Assert.Contains("WhereQuantity", filterSource.SourceText);
            Assert.Contains("WherePrice", filterSource.SourceText);
            Assert.Contains("WhereWeight", filterSource.SourceText);
            Assert.Contains("WhereIsActive", filterSource.SourceText);
            Assert.Contains("WhereCreatedAt", filterSource.SourceText);
            Assert.Contains("WhereCategoryId", filterSource.SourceText);
        }

        #endregion

        #region 可空类型测试

        [Fact]
        public void Should_Handle_Nullable_Types()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateQueryBuilder]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
        public decimal? DiscountPrice { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}
";

            var entitySource = @"
using System;

namespace TestNamespace
{
    public class Entity<TId> where TId : IEquatable<TId>
    {
        public TId Id { get; set; }
    }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<QueryBuilderSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var filterSource = result.GetSourceByFileName("ProductFilterBuilder.g.cs");
            Assert.NotNull(filterSource);
            Assert.Contains("WhereDiscountPrice", filterSource.SourceText);
            Assert.Contains("WhereDeletedAt", filterSource.SourceText);
            Assert.Contains("WhereDiscountPriceBetween", filterSource.SourceText);
            Assert.Contains("WhereDeletedAtBetween", filterSource.SourceText);
        }

        #endregion
    }
}
