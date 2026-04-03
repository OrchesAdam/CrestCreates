using System;
using System.Linq;
using Xunit;
using CrestCreates.CodeGenerator.RepositoryGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;

namespace CrestCreates.CodeGenerator.Tests.RepositoryGenerator
{
    /// <summary>
    /// 仓储源代码生成器测试
    /// </summary>
    public class RepositorySourceGeneratorTests
    {
        #region 仓储接口生成测试

        [Fact]
        public void Should_Generate_Repository_Interface()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateRepository]
    public class TestEntity : Entity<Guid>
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
            var result = SourceGeneratorTestHelper.RunGenerator<RepositorySourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            Assert.True(result.ContainsSource("ITestEntityRepository"));
            Assert.True(result.ContainsFile("ITestEntityRepository.g.cs"));
            Assert.True(result.ContainsSource("interface ITestEntityRepository"));
        }

        [Fact]
        public void Should_Generate_Repository_Interface_With_Correct_Methods()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateRepository]
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
            var result = SourceGeneratorTestHelper.RunGenerator<RepositorySourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var interfaceSource = result.GetSourceByFileName("IProductRepository.g.cs");
            Assert.NotNull(interfaceSource);
            Assert.Contains("Task<Product?> GetByIdAsync", interfaceSource.SourceText);
            Assert.Contains("Task<List<Product>> GetAllAsync", interfaceSource.SourceText);
            Assert.Contains("Task<Product> AddAsync", interfaceSource.SourceText);
            Assert.Contains("Task<Product> UpdateAsync", interfaceSource.SourceText);
            Assert.Contains("Task DeleteAsync", interfaceSource.SourceText);
            Assert.Contains("Task DeleteByIdAsync", interfaceSource.SourceText);
            Assert.Contains("Task<List<Product>> FindAsync", interfaceSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Repository_Interface_With_Entity_Specific_Methods()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateRepository]
    public class Customer : Entity<int>
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string CustomerCode { get; set; }
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
            var result = SourceGeneratorTestHelper.RunGenerator<RepositorySourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var interfaceSource = result.GetSourceByFileName("ICustomerRepository.g.cs");
            Assert.NotNull(interfaceSource);
            Assert.Contains("Task<List<Customer>> FindByNameAsync", interfaceSource.SourceText);
            Assert.Contains("Task<List<Customer>> FindByEmailAsync", interfaceSource.SourceText);
            Assert.Contains("Task<List<Customer>> FindByNameContainsAsync", interfaceSource.SourceText);
            Assert.Contains("Task<Customer?> GetByEmailAsync", interfaceSource.SourceText);
            Assert.Contains("Task<Customer?> GetByCustomerCodeAsync", interfaceSource.SourceText);
        }

        [Fact]
        public void Should_Generate_Repository_Interface_With_Int_Id()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateRepository]
    public class Order : Entity<int>
    {
        public string OrderNumber { get; set; }
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
            var result = SourceGeneratorTestHelper.RunGenerator<RepositorySourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var interfaceSource = result.GetSourceByFileName("IOrderRepository.g.cs");
            Assert.NotNull(interfaceSource);
            Assert.Contains("IRepository<Order, int>", interfaceSource.SourceText);
            Assert.Contains("Task<Order?> GetByIdAsync(int id", interfaceSource.SourceText);
        }

        #endregion

        #region EF Core 仓储实现生成测试

        [Fact]
        public void Should_Generate_EfCore_Repository_Implementation()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateRepository(OrmProvider = OrmProvider.EfCore)]
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

    public enum OrmProvider { EfCore, SqlSugar, FreeSql }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<RepositorySourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            Assert.True(result.ContainsFile("ProductRepository.g.cs"));
            var implSource = result.GetSourceByFileName("ProductRepository.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("class ProductRepository : EfCoreRepository<Product, Guid>, IProductRepository", implSource.SourceText);
            Assert.Contains("DbContext", implSource.SourceText);
            Assert.Contains("EntityFrameworkCore.Repositories", implSource.SourceText);
        }

        [Fact]
        public void Should_Generate_EfCore_Repository_With_Correct_CRUD_Methods()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateRepository]
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
            var result = SourceGeneratorTestHelper.RunGenerator<RepositorySourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var implSource = result.GetSourceByFileName("CategoryRepository.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("DbContext.Set<Category>().FindAsync", implSource.SourceText);
            Assert.Contains("DbContext.Set<Category>().ToListAsync", implSource.SourceText);
            Assert.Contains("DbContext.Set<Category>().AddAsync", implSource.SourceText);
            Assert.Contains("DbContext.Set<Category>().Update", implSource.SourceText);
            Assert.Contains("DbContext.Set<Category>().Remove", implSource.SourceText);
            Assert.Contains("DbContext.SaveChangesAsync", implSource.SourceText);
        }

        [Fact]
        public void Should_Generate_EfCore_Repository_With_Paging_Methods()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateRepository]
    public class Item : Entity<Guid>
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
            var result = SourceGeneratorTestHelper.RunGenerator<RepositorySourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var implSource = result.GetSourceByFileName("ItemRepository.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("GetPagedListAsync", implSource.SourceText);
            Assert.Contains("Skip((pageNumber - 1) * pageSize)", implSource.SourceText);
            Assert.Contains("Take(pageSize)", implSource.SourceText);
            Assert.Contains("CountAsync", implSource.SourceText);
        }

        #endregion

        #region SqlSugar 仓储实现生成测试

        [Fact]
        public void Should_Generate_SqlSugar_Repository_Implementation()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateRepository(OrmProvider = OrmProvider.SqlSugar)]
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

    public enum OrmProvider { EfCore, SqlSugar, FreeSql }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<RepositorySourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            Assert.True(result.ContainsFile("SqlSugarProductRepository.g.cs"));
            var implSource = result.GetSourceByFileName("SqlSugarProductRepository.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("class ProductRepository : IProductRepository", implSource.SourceText);
            Assert.Contains("ISqlSugarClient", implSource.SourceText);
            Assert.Contains("SqlSugar.Repositories", implSource.SourceText);
        }

        [Fact]
        public void Should_Generate_SqlSugar_Repository_With_Correct_CRUD_Methods()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateRepository(OrmProvider = OrmProvider.SqlSugar)]
    public class Order : Entity<int>
    {
        public string OrderNumber { get; set; }
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

    public enum OrmProvider { EfCore, SqlSugar, FreeSql }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<RepositorySourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var implSource = result.GetSourceByFileName("SqlSugarOrderRepository.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("Db.Queryable<Order>().InSingleAsync", implSource.SourceText);
            Assert.Contains("Db.Queryable<Order>().ToListAsync", implSource.SourceText);
            Assert.Contains("Db.Insertable", implSource.SourceText);
            Assert.Contains("Db.Updateable", implSource.SourceText);
            Assert.Contains("Db.Deleteable", implSource.SourceText);
        }

        [Fact]
        public void Should_Generate_SqlSugar_Repository_With_Query_Methods()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateRepository(OrmProvider = OrmProvider.SqlSugar)]
    public class Customer : Entity<Guid>
    {
        public string Name { get; set; }
        public string Email { get; set; }
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

    public enum OrmProvider { EfCore, SqlSugar, FreeSql }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<RepositorySourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var implSource = result.GetSourceByFileName("SqlSugarCustomerRepository.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("FindByNameAsync", implSource.SourceText);
            Assert.Contains("FindByEmailAsync", implSource.SourceText);
            Assert.Contains("FindByNameContainsAsync", implSource.SourceText);
            Assert.Contains("GetByEmailAsync", implSource.SourceText);
            Assert.Contains("Db.Queryable<Customer>().Where", implSource.SourceText);
        }

        #endregion

        #region FreeSql 仓储实现生成测试

        [Fact]
        public void Should_Generate_FreeSql_Repository_Implementation()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateRepository(OrmProvider = OrmProvider.FreeSql)]
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

    public enum OrmProvider { EfCore, SqlSugar, FreeSql }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<RepositorySourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            Assert.True(result.ContainsFile("FreeSqlProductRepository.g.cs"));
            var implSource = result.GetSourceByFileName("FreeSqlProductRepository.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("class ProductRepository : IProductRepository", implSource.SourceText);
            Assert.Contains("IFreeSql", implSource.SourceText);
            Assert.Contains("FreeSql.Repositories", implSource.SourceText);
        }

        [Fact]
        public void Should_Generate_FreeSql_Repository_With_Correct_CRUD_Methods()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateRepository(OrmProvider = OrmProvider.FreeSql)]
    public class Article : Entity<int>
    {
        public string Title { get; set; }
        public string Content { get; set; }
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

    public enum OrmProvider { EfCore, SqlSugar, FreeSql }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<RepositorySourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var implSource = result.GetSourceByFileName("FreeSqlArticleRepository.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("Db.Select<Article>().Where", implSource.SourceText);
            Assert.Contains("Db.Select<Article>().ToListAsync", implSource.SourceText);
            Assert.Contains("Db.Insert<Article>().AppendData", implSource.SourceText);
            Assert.Contains("Db.Update<Article>().SetSource", implSource.SourceText);
            Assert.Contains("Db.Delete<Article>().Where", implSource.SourceText);
        }

        [Fact]
        public void Should_Generate_FreeSql_Repository_With_Paging()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateRepository(OrmProvider = OrmProvider.FreeSql)]
    public class Log : Entity<Guid>
    {
        public string Message { get; set; }
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

    public enum OrmProvider { EfCore, SqlSugar, FreeSql }
}
";

            // Act
            var result = SourceGeneratorTestHelper.RunGenerator<RepositorySourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var implSource = result.GetSourceByFileName("FreeSqlLogRepository.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("GetPagedListAsync", implSource.SourceText);
            Assert.Contains("Skip((pageNumber - 1) * pageSize)", implSource.SourceText);
            Assert.Contains("Take(pageSize)", implSource.SourceText);
            Assert.Contains("CountAsync", implSource.SourceText);
        }

        #endregion

        #region 软删除支持测试

        [Fact]
        public void Should_Generate_Soft_Delete_Methods_For_Audited_Entity()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateRepository]
    public class AuditedEntity : Entity<Guid>
    {
        public string Name { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletionTime { get; set; }
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
            var result = SourceGeneratorTestHelper.RunGenerator<RepositorySourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var interfaceSource = result.GetSourceByFileName("IAuditedEntityRepository.g.cs");
            Assert.NotNull(interfaceSource);
            Assert.Contains("SoftDeleteAsync", interfaceSource.SourceText);
            Assert.Contains("RestoreAsync", interfaceSource.SourceText);
            Assert.Contains("GetNotDeletedAsync", interfaceSource.SourceText);
            Assert.Contains("GetDeletedAsync", interfaceSource.SourceText);
        }

        #endregion

        #region 批量操作测试

        [Fact]
        public void Should_Generate_Batch_Operations()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateRepository]
    public class Document : Entity<Guid>
    {
        public string Title { get; set; }
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
            var result = SourceGeneratorTestHelper.RunGenerator<RepositorySourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var interfaceSource = result.GetSourceByFileName("IDocumentRepository.g.cs");
            Assert.NotNull(interfaceSource);
            Assert.Contains("AddRangeAsync", interfaceSource.SourceText);
            Assert.Contains("UpdateRangeAsync", interfaceSource.SourceText);
            Assert.Contains("DeleteRangeAsync", interfaceSource.SourceText);
            Assert.Contains("DeleteByIdsAsync", interfaceSource.SourceText);
        }

        #endregion

        #region 存在性检查测试

        [Fact]
        public void Should_Generate_Existence_Check_Methods()
        {
            // Arrange
            var source = @"
using System;
using System.Linq.Expressions;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateRepository]
    public class User : Entity<Guid>
    {
        public string Username { get; set; }
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
            var result = SourceGeneratorTestHelper.RunGenerator<RepositorySourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            var interfaceSource = result.GetSourceByFileName("IUserRepository.g.cs");
            Assert.NotNull(interfaceSource);
            Assert.Contains("ExistsAsync", interfaceSource.SourceText);
            Assert.Contains("CountAsync", interfaceSource.SourceText);
        }

        #endregion

        #region 多实体测试

        [Fact]
        public void Should_Generate_Repositories_For_Multiple_Entities()
        {
            // Arrange
            var source1 = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateRepository]
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
    [GenerateRepository]
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
            var result = SourceGeneratorTestHelper.RunGenerator<RepositorySourceGenerator>(
                new[] { source1, source2 },
                new[] { entitySource });

            // Assert
            Assert.True(result.ContainsFile("IProductRepository.g.cs"));
            Assert.True(result.ContainsFile("ICategoryRepository.g.cs"));
            Assert.True(result.ContainsFile("ProductRepository.g.cs"));
            Assert.True(result.ContainsFile("CategoryRepository.g.cs"));
        }

        #endregion

        #region 默认 ORM 提供者测试

        [Fact]
        public void Should_Default_To_EfCore_When_Provider_Not_Specified()
        {
            // Arrange
            var source = @"
using System;
using CrestCreates.Domain.Shared.Attributes;

namespace TestNamespace
{
    [GenerateRepository]
    public class Item : Entity<Guid>
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
            var result = SourceGeneratorTestHelper.RunGenerator<RepositorySourceGenerator>(
                source,
                new[] { entitySource });

            // Assert
            Assert.True(result.ContainsFile("ItemRepository.g.cs"));
            Assert.False(result.ContainsFile("SqlSugarItemRepository.g.cs"));
            Assert.False(result.ContainsFile("FreeSqlItemRepository.g.cs"));
            var implSource = result.GetSourceByFileName("ItemRepository.g.cs");
            Assert.NotNull(implSource);
            Assert.Contains("EfCoreRepository", implSource.SourceText);
        }

        #endregion
    }
}
