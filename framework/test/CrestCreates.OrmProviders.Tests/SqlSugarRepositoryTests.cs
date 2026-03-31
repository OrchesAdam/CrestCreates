using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using CrestCreates.OrmProviders.SqlSugar.Repositories;
using CrestCreates.OrmProviders.SqlSugar.UnitOfWork;
using CrestCreates.Domain.Entities;
using SqlSugar;

namespace CrestCreates.OrmProviders.Tests
{
    // 具体的 SqlSugar 仓储实现
    public class TestSqlSugarRepository<TEntity, TKey> : SqlSugarRepository<TEntity, TKey>
        where TEntity : class, IEntity<TKey>, new()
        where TKey : IEquatable<TKey>
    {
        public TestSqlSugarRepository(ISqlSugarClient sqlSugarClient) : base(sqlSugarClient, null)
        {}
    }

    public class SqlSugarRepositoryTests : OrmTestBase
    {
        private readonly ISqlSugarClient _sqlSugarClient;

        public SqlSugarRepositoryTests()
        {
            // 创建内存数据库
            _sqlSugarClient = new SqlSugarClient(new ConnectionConfig
            {
                DbType = DbType.Sqlite,
                ConnectionString = "DataSource=:memory:",
                IsAutoCloseConnection = false // 保持连接打开，以便表结构创建和操作
            });
            
            // 打开连接
            _sqlSugarClient.Ado.Open();
            
            // 自动迁移表结构，使用特性配置忽略列
            _sqlSugarClient.CodeFirst.InitTables(typeof(SqlSugarTestCustomer));
            _sqlSugarClient.CodeFirst.InitTables(typeof(SqlSugarTestSoftDeleteEntity));
        }

        [Fact]
        public async Task AddAsync_ShouldAddEntity()
        {
            // Arrange
            var repository = new TestSqlSugarRepository<SqlSugarTestCustomer, int>(_sqlSugarClient);
            var customer = new SqlSugarTestCustomer(1, "John", "Doe", "john.doe@example.com");

            // Act
            var result = await repository.AddAsync(customer);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(1, result.Id);
            Assert.Equal("John", result.FirstName);
            Assert.Equal("Doe", result.LastName);
        }

        [Fact]
        public async Task GetByIdAsync_ShouldReturnEntity()
        {
            // Arrange
            var repository = new TestSqlSugarRepository<SqlSugarTestCustomer, int>(_sqlSugarClient);
            var customer = new SqlSugarTestCustomer(1, "John", "Doe", "john.doe@example.com");
            await repository.AddAsync(customer);

            // Act
            var result = await repository.GetByIdAsync(1);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(1, result.Id);
        }

        [Fact]
        public async Task UpdateAsync_ShouldUpdateEntity()
        {
            // Arrange
            var repository = new TestSqlSugarRepository<SqlSugarTestCustomer, int>(_sqlSugarClient);
            var customer = new SqlSugarTestCustomer(1, "John", "Doe", "john.doe@example.com");
            await repository.AddAsync(customer);
            customer.SetName("Jane", "Smith");

            // Act
            var result = await repository.UpdateAsync(customer);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Jane", result.FirstName);
            Assert.Equal("Smith", result.LastName);
        }

        [Fact]
        public async Task DeleteAsync_ShouldDeleteEntity()
        {
            // Arrange
            var repository = new TestSqlSugarRepository<SqlSugarTestCustomer, int>(_sqlSugarClient);
            var customer = new SqlSugarTestCustomer(1, "John", "Doe", "john.doe@example.com");
            await repository.AddAsync(customer);

            // Act
            await repository.DeleteAsync(customer);
            var result = await repository.GetByIdAsync(1);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task SoftDelete_ShouldMarkEntityAsDeleted()
        {
            // Arrange
            var repository = new TestSqlSugarRepository<SqlSugarTestSoftDeleteEntity, long>(_sqlSugarClient);
            var entity = new SqlSugarTestSoftDeleteEntity(1, "Test Entity");
            await repository.AddAsync(entity);

            // Act
            await repository.DeleteAsync(entity);
            var result = await repository.GetByIdAsync(1);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetAllAsync_ShouldReturnAllEntities()
        {
            // Arrange
            var repository = new TestSqlSugarRepository<SqlSugarTestCustomer, int>(_sqlSugarClient);
            await repository.AddAsync(new SqlSugarTestCustomer(1, "John", "Doe", "john.doe@example.com"));
            await repository.AddAsync(new SqlSugarTestCustomer(2, "Jane", "Smith", "jane.smith@example.com"));

            // Act
            var result = await repository.GetAllAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
        }
    }

    // 测试实体类 - 不继承AggregateRoot，避免DomainEvents问题
    public class SqlSugarTestCustomer : IEntity<int>
    {
        [SugarColumn(IsPrimaryKey = true)]
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public DateTime BirthDate { get; set; }
        public bool IsVip { get; set; }

        public SqlSugarTestCustomer()
        {
        }

        public SqlSugarTestCustomer(int id, string firstName, string lastName, string email)
        {
            Id = id;
            FirstName = firstName;
            LastName = lastName;
            Email = email;
        }

        public void SetName(string firstName, string lastName)
        {
            FirstName = firstName;
            LastName = lastName;
        }
    }

    // 测试软删除实体类 - 不继承FullyAuditedAggregateRoot，避免审计字段问题
    public class SqlSugarTestSoftDeleteEntity : IEntity<long>
    {
        [SugarColumn(IsPrimaryKey = true)]
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsDeleted { get; set; }

        public SqlSugarTestSoftDeleteEntity()
        {
        }

        public SqlSugarTestSoftDeleteEntity(long id, string name)
        {
            Id = id;
            Name = name;
        }
    }
}
