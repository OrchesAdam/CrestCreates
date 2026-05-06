using System;
using System.Linq;
using Xunit;
using CrestCreates.CodeGenerator.CrudServiceGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;

namespace CrestCreates.CodeGenerator.Tests.CrudServiceGenerator
{
    /// <summary>
    /// CRUD 服务源代码生成器测试
    /// </summary>
    public class CrudServiceSourceGeneratorTests
    {
        #region DTO 生成测试

        [Fact]
        public void Should_Generate_Entity_Dto()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateCrudService]
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
            var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            Assert.True(result.ContainsFile("ProductDto.g.cs"));
            var dtoSource = result.GetSourceByFileName("ProductDto.g.cs");
            Assert.NotNull(dtoSource);
            Assert.Contains("class ProductDto", dtoSource.SourceText);
            Assert.Contains("public string Name { get; set; }", dtoSource.SourceText);
            Assert.Contains("public decimal Price { get; set; }", dtoSource.SourceText);
            Assert.Contains("public int StockQuantity { get; set; }", dtoSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Create_Entity_Dto()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateCrudService]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
        public decimal Price { get; set; }
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
            var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            Assert.True(result.ContainsFile("CreateProductDto.g.cs"));
            var dtoSource = result.GetSourceByFileName("CreateProductDto.g.cs");
            Assert.NotNull(dtoSource);
            Assert.Contains("class CreateProductDto", dtoSource.SourceText);
            Assert.Contains("public string Name { get; set; }", dtoSource.SourceText);
            Assert.Contains("public decimal Price { get; set; }", dtoSource.SourceText);
            Assert.Contains("public string Description { get; set; }", dtoSource.SourceText);
            Assert.DoesNotContain("public Guid Id", dtoSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Update_Entity_Dto()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateCrudService]
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
            var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            Assert.True(result.ContainsFile("UpdateProductDto.g.cs"));
            var dtoSource = result.GetSourceByFileName("UpdateProductDto.g.cs");
            Assert.NotNull(dtoSource);
            Assert.Contains("class UpdateProductDto", dtoSource.SourceText);
            Assert.DoesNotContain("public System.Guid Id", dtoSource.SourceText);
            Assert.Contains("public string Name { get; set; }", dtoSource.SourceText);
            Assert.Contains("public decimal Price { get; set; }", dtoSource.SourceText);
        }

        [Fact]
        public void Should_Generate_List_Request_Dto()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateCrudService]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public decimal Price { get; set; }
        public DateTime CreationTime { get; set; }
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
            var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            Assert.True(result.ContainsFile("ProductListRequestDto.g.cs"));
            var dtoSource = result.GetSourceByFileName("ProductListRequestDto.g.cs");
            Assert.NotNull(dtoSource);
            Assert.Contains("class ProductListRequestDto : PagedRequestDto", dtoSource.SourceText);
            Assert.DoesNotContain("Keyword", dtoSource.SourceText);
            Assert.DoesNotContain("StartTime", dtoSource.SourceText);
            Assert.DoesNotContain("EndTime", dtoSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Dto_With_Validation_Attributes()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateCrudService]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
        public string Code { get; set; }
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
            var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var createDtoSource = result.GetSourceByFileName("CreateProductDto.g.cs");
            Assert.NotNull(createDtoSource);
            Assert.Contains("[Required]", createDtoSource.SourceText);
            Assert.Contains("[StringLength(255)]", createDtoSource.SourceText);
        }

        [Fact]
        public void Should_Exclude_Audit_Properties_From_Create_Dto()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateCrudService]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
        public DateTime CreationTime { get; set; }
        public Guid CreatorId { get; set; }
        public DateTime? LastModificationTime { get; set; }
        public Guid? LastModifierId { get; set; }
        public bool IsDeleted { get; set; }
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
            var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var createDtoSource = result.GetSourceByFileName("CreateProductDto.g.cs");
            Assert.NotNull(createDtoSource);
            Assert.Contains("public string Name { get; set; }", createDtoSource.SourceText);
            Assert.DoesNotContain("public DateTime CreationTime", createDtoSource.SourceText);
            Assert.DoesNotContain("public Guid CreatorId", createDtoSource.SourceText);
            Assert.DoesNotContain("public bool IsDeleted", createDtoSource.SourceText);
        }

        #endregion

        #region 服务接口生成测试

        [Fact]
        public void Should_Generate_Crud_Service_Interface()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateCrudService]
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
            var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            Assert.True(result.ContainsFile("IProductAppService.g.cs"));
            var interfaceSource = result.GetSourceByFileName("IProductAppService.g.cs");
            Assert.NotNull(interfaceSource);
            Assert.Contains("interface IProductAppService", interfaceSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Service_Interface_With_Correct_Methods()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateCrudService]
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
            var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert - mainline interface inherits ICrudAppService directly
            var interfaceSource = result.GetSourceByFileName("IProductAppService.g.cs");
            Assert.NotNull(interfaceSource);
            Assert.Contains("interface IProductAppService : ICrudAppService<System.Guid, ProductDto, CreateProductDto, UpdateProductDto, ProductListRequestDto>", interfaceSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Service_Interface_With_Int_Id()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateCrudService]
    public class Category : Entity<int>
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
            var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert - mainline interface inherits ICrudAppService with int id type
            var interfaceSource = result.GetSourceByFileName("ICategoryAppService.g.cs");
            Assert.NotNull(interfaceSource);
            Assert.Contains("interface ICategoryAppService : ICrudAppService<int, CategoryDto, CreateCategoryDto, UpdateCategoryDto, CategoryListRequestDto>", interfaceSource.SourceText);
        }

        #endregion

        #region 服务实现生成测试

        [Fact]
        public void Should_Generate_Crud_Service_Implementation()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateCrudService]
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
            var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            Assert.True(result.ContainsFile("ProductAppService.g.cs"));
            var implSource = result.GetSourceByFileName("ProductAppService.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("class ProductAppService", implSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Service_With_Repository_Injection()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateCrudService]
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
            var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var implSource = result.GetSourceByFileName("ProductAppService.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("protected readonly ICrestRepositoryBase<Product,", implSource.SourceText);
            Assert.Contains("ICrestRepositoryBase<Product, System.Guid> repository", implSource.SourceText);
            Assert.DoesNotContain("IMapper", implSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Create_Method()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateCrudService]
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
            var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var implSource = result.GetSourceByFileName("ProductAppService.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("public virtual async Task<ProductDto> CreateAsync(CreateProductDto input, CancellationToken cancellationToken = default)", implSource.SourceText);
            Assert.Contains("ProductObjectMappings.ToTarget(input)", implSource.SourceText);
            Assert.Contains("Repository.InsertAsync(entity, cancellationToken)", implSource.SourceText);
            Assert.Contains("ProductObjectMappings.ToTarget(created)", implSource.SourceText);
        }

        [Fact]
        public void Should_Generate_GetById_Method()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateCrudService]
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
            var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var implSource = result.GetSourceByFileName("ProductAppService.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("public virtual async Task<ProductDto?> GetByIdAsync(System.Guid id, CancellationToken cancellationToken = default)", implSource.SourceText);
            Assert.Contains("Repository.GetQueryable()", implSource.SourceText);
        }

        [Fact]
        public void Should_Generate_GetList_Method_With_Paging()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateCrudService]
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
            var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var implSource = result.GetSourceByFileName("ProductAppService.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("public virtual async Task<PagedResultDto<ProductDto>> GetListAsync(ProductListRequestDto input, CancellationToken cancellationToken = default)", implSource.SourceText);
            Assert.Contains("QueryExecutor<Product>.ApplyFilters", implSource.SourceText);
            Assert.Contains("input.PageIndex", implSource.SourceText);
            Assert.Contains("input.PageSize", implSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Update_Method()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateCrudService]
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
            var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var implSource = result.GetSourceByFileName("ProductAppService.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("public virtual async Task<ProductDto> UpdateAsync(System.Guid id, UpdateProductDto input, CancellationToken cancellationToken = default)", implSource.SourceText);
            Assert.Contains("Repository.GetQueryable()", implSource.SourceText);
            Assert.Contains("EntityNotFoundException", implSource.SourceText);
            Assert.Contains("ProductObjectMappings.Apply(input, entity)", implSource.SourceText);
            Assert.Contains("Repository.UpdateAsync(entity, cancellationToken)", implSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Delete_Method()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateCrudService]
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
            var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var implSource = result.GetSourceByFileName("ProductAppService.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("public virtual async Task DeleteAsync(System.Guid id, string? expectedStamp = null", implSource.SourceText);
            Assert.Contains("Repository.GetAsync(id, cancellationToken)", implSource.SourceText);
            Assert.Contains("Repository.DeleteAsync(entity, cancellationToken)", implSource.SourceText);
            Assert.Contains("CrestEntityNotFoundException", implSource.SourceText);
        }

        #endregion

        #region 映射配置生成测试

        [Fact]
        public void Should_Generate_Mapping_Profile()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateCrudService]
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
            var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            Assert.True(result.ContainsFile("ProductAppService.g.cs"));
            var implSource = result.GetSourceByFileName("ProductAppService.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("ProductObjectMappings.ToTarget(input)", implSource.SourceText);
            Assert.Contains("ProductObjectMappings.Apply(input, entity)", implSource.SourceText);
        }

        #endregion

        #region 搜索过滤测试

        [Fact]
        public void Should_Not_Generate_Keyword_Search_In_GetList()
        {
            // The CRUD mainline uses descriptor-based querying only.
            // Keyword, guessed string filters, and date ranges are NOT auto-generated.
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateCrudService]
    public class Product : Entity<Guid>
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Sku { get; set; }
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

            var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
                source,
                new[] { entitySource });

            var implSource = result.GetSourceByFileName("ProductAppService.g.cs");
            Assert.NotNull(implSource);
            Assert.DoesNotContain("input.Keyword", implSource.SourceText);
            Assert.DoesNotContain("StartTime", implSource.SourceText);
            Assert.DoesNotContain("EndTime", implSource.SourceText);
        }

        #endregion

        #region 多实体测试

        [Fact]
        public void Should_Generate_Services_For_Multiple_Entities()
        {
            // Arrange
            var source1 = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateCrudService]
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
    [GenerateCrudService]
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
            var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
                new[] { source1, source2 },
                new[] { entitySource });

            // Assert
            Assert.True(result.ContainsFile("IProductAppService.g.cs"));
            Assert.True(result.ContainsFile("ICategoryAppService.g.cs"));
            Assert.True(result.ContainsFile("ProductAppService.g.cs"));
            Assert.True(result.ContainsFile("CategoryAppService.g.cs"));
            Assert.True(result.ContainsFile("ProductDto.g.cs"));
            Assert.True(result.ContainsFile("CategoryDto.g.cs"));
        }

        #endregion

        #region 异常处理测试

        [Fact]
        public void Should_Generate_Null_Checks_In_Service_Methods()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateCrudService]
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
            var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var implSource = result.GetSourceByFileName("ProductAppService.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("if (input == null)", implSource.SourceText);
            Assert.Contains("throw new ArgumentNullException", implSource.SourceText);
        }

        [Fact]
        public void Should_Generate_EntityNotFound_Exception_For_Update()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateCrudService]
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
            var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var implSource = result.GetSourceByFileName("ProductAppService.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("CrestEntityNotFoundException(typeof(Product).Name, id)", implSource.SourceText);
        }

        #endregion
    }
}
