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
            Assert.Contains("public Guid Id { get; set; }", dtoSource.SourceText);
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
            Assert.Contains("public string? Keyword { get; set; }", dtoSource.SourceText);
            Assert.Contains("public DateTime? StartTime { get; set; }", dtoSource.SourceText);
            Assert.Contains("public DateTime? EndTime { get; set; }", dtoSource.SourceText);
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
            Assert.True(result.ContainsFile("IProductCrudService.g.cs"));
            var interfaceSource = result.GetSourceByFileName("IProductCrudService.g.cs");
            Assert.NotNull(interfaceSource);
            Assert.Contains("interface IProductCrudService", interfaceSource.SourceText);
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

            // Assert
            var interfaceSource = result.GetSourceByFileName("IProductCrudService.g.cs");
            Assert.NotNull(interfaceSource);
            Assert.Contains("Task<ProductDto> CreateAsync(CreateProductDto input)", interfaceSource.SourceText);
            Assert.Contains("Task<ProductDto?> GetByIdAsync(Guid id)", interfaceSource.SourceText);
            Assert.Contains("Task<PagedResult<ProductDto>> GetListAsync(ProductListRequestDto input)", interfaceSource.SourceText);
            Assert.Contains("Task<ProductDto> UpdateAsync(UpdateProductDto input)", interfaceSource.SourceText);
            Assert.Contains("Task DeleteAsync(Guid id)", interfaceSource.SourceText);
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

            // Assert
            var interfaceSource = result.GetSourceByFileName("ICategoryCrudService.g.cs");
            Assert.NotNull(interfaceSource);
            Assert.Contains("Task<CategoryDto?> GetByIdAsync(int id)", interfaceSource.SourceText);
            Assert.Contains("Task DeleteAsync(int id)", interfaceSource.SourceText);
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
            Assert.True(result.ContainsFile("ProductCrudService.g.cs"));
            var implSource = result.GetSourceByFileName("ProductCrudService.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("class ProductCrudService : IProductCrudService", implSource.SourceText);
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
            var implSource = result.GetSourceByFileName("ProductCrudService.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("private readonly IProductRepository _repository", implSource.SourceText);
            Assert.Contains("private readonly IMapper _mapper", implSource.SourceText);
            Assert.Contains("IProductRepository repository, IMapper mapper", implSource.SourceText);
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
            var implSource = result.GetSourceByFileName("ProductCrudService.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("public async Task<ProductDto> CreateAsync(CreateProductDto input)", implSource.SourceText);
            Assert.Contains("_mapper.Map<Product>(input)", implSource.SourceText);
            Assert.Contains("_repository.AddAsync(entity)", implSource.SourceText);
            Assert.Contains("_mapper.Map<ProductDto>(entity)", implSource.SourceText);
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
            var implSource = result.GetSourceByFileName("ProductCrudService.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("public async Task<ProductDto?> GetByIdAsync(Guid id)", implSource.SourceText);
            Assert.Contains("_repository.GetByIdAsync(id)", implSource.SourceText);
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
            var implSource = result.GetSourceByFileName("ProductCrudService.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("public async Task<PagedResult<ProductDto>> GetListAsync(ProductListRequestDto input)", implSource.SourceText);
            Assert.Contains("_repository.GetPagedListAsync", implSource.SourceText);
            Assert.Contains("input.PageNumber", implSource.SourceText);
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
            var implSource = result.GetSourceByFileName("ProductCrudService.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("public async Task<ProductDto> UpdateAsync(UpdateProductDto input)", implSource.SourceText);
            Assert.Contains("_repository.GetByIdAsync(input.Id)", implSource.SourceText);
            Assert.Contains("EntityNotFoundException", implSource.SourceText);
            Assert.Contains("_mapper.Map(input, entity)", implSource.SourceText);
            Assert.Contains("_repository.UpdateAsync(entity)", implSource.SourceText);
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
            var implSource = result.GetSourceByFileName("ProductCrudService.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("public async Task DeleteAsync(Guid id)", implSource.SourceText);
            Assert.Contains("_repository.GetByIdAsync(id)", implSource.SourceText);
            Assert.Contains("_repository.DeleteAsync(entity)", implSource.SourceText);
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
            Assert.True(result.ContainsFile("ProductMappingProfile.g.cs"));
            var profileSource = result.GetSourceByFileName("ProductMappingProfile.g.cs");
            Assert.NotNull(profileSource);
            Assert.Contains("class ProductMappingProfile : Profile", profileSource.SourceText);
            Assert.Contains("CreateMap<Product, ProductDto>()", profileSource.SourceText);
            Assert.Contains("CreateMap<CreateProductDto, Product>()", profileSource.SourceText);
            Assert.Contains("CreateMap<UpdateProductDto, Product>()", profileSource.SourceText);
        }

        #endregion

        #region 搜索过滤测试

        [Fact]
        public void Should_Generate_Keyword_Search_In_GetList()
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

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<CrudServiceSourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var implSource = result.GetSourceByFileName("ProductCrudService.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("input.Keyword", implSource.SourceText);
            Assert.Contains("Contains", implSource.SourceText);
        }

        [Fact]
        public void Should_Generate_DateRange_Filter()
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
            var implSource = result.GetSourceByFileName("ProductCrudService.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("input.StartTime", implSource.SourceText);
            Assert.Contains("input.EndTime", implSource.SourceText);
            Assert.Contains("e.CreationTime >= input.StartTime.Value", implSource.SourceText);
            Assert.Contains("e.CreationTime <= input.EndTime.Value", implSource.SourceText);
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
            Assert.True(result.ContainsFile("IProductCrudService.g.cs"));
            Assert.True(result.ContainsFile("ICategoryCrudService.g.cs"));
            Assert.True(result.ContainsFile("ProductCrudService.g.cs"));
            Assert.True(result.ContainsFile("CategoryCrudService.g.cs"));
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
            var implSource = result.GetSourceByFileName("ProductCrudService.g.cs");
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
            var implSource = result.GetSourceByFileName("ProductCrudService.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("EntityNotFoundException(typeof(Product), input.Id)", implSource.SourceText);
        }

        #endregion
    }
}
