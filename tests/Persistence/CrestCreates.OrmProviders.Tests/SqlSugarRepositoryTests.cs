using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using CrestCreates.Data.SqlSugar.Repositories;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Shared.Entities;
using SqlSugar;

namespace CrestCreates.OrmProviders.Tests
{
    public class TestSqlSugarRepository<TEntity, TKey> : SqlSugarRepository<TEntity, TKey>
        where TEntity : class, IEntity<TKey>, new()
        where TKey : IEquatable<TKey>
    {
        public TestSqlSugarRepository(ISqlSugarClient sqlSugarClient) : base(sqlSugarClient, null)
        {}

        public override IQueryable<TEntity> GetQueryableUnfiltered()
        {
            throw new NotImplementedException();
        }

        public override Task DeleteAsync(TKey id, string expectedStamp, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("SqlSugar concurrency delete not yet implemented.");
        }
    }

    public class SqlSugarRepositoryTests : OrmTestBase
    {
        private readonly ISqlSugarClient _sqlSugarClient;

        public SqlSugarRepositoryTests()
        {
            _sqlSugarClient = new SqlSugarClient(new ConnectionConfig
            {
                DbType = DbType.Sqlite,
                ConnectionString = "DataSource=:memory:",
                IsAutoCloseConnection = false
            });
            
            _sqlSugarClient.Ado.Open();
            
            _sqlSugarClient.CodeFirst.InitTables(typeof(SqlSugarTestCustomer));
            _sqlSugarClient.CodeFirst.InitTables(typeof(SqlSugarTestSoftDeleteEntity));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public async Task GetPagedAsync_ShouldUseStableTypedOrdering(int overload)
        {
            var repository = new TestSqlSugarRepository<SqlSugarTestCustomer, int>(_sqlSugarClient);
            for (var id = 6; id >= 1; id--)
                await repository.InsertAsync(new SqlSugarTestCustomer(id, id <= 3 ? "A" : "B", "Customer", "customer@example.com"));

            var statements = new List<string>();
            _sqlSugarClient.Aop.OnLogExecuting = (sql, _) => statements.Add(sql);
            var ids = new List<int>();
            for (var page = 0; page < 3; page++)
            {
                var result = overload switch
                {
                    0 => await repository.GetPagedAsync(page, 2),
                    1 => await repository.GetPagedAsync(page, 2, entity => entity.Id > 0),
                    2 => await repository.GetPagedAsync(page, 2, entity => entity.Id > 0, entity => entity.FirstName, false),
                    _ => await repository.GetPagedAsync(page, 2, entity => entity.FirstName, false)
                };
                ids.AddRange(result.Items.Select(entity => entity.Id));
            }
            Assert.Equal(overload < 2 ? new[] { 1, 2, 3, 4, 5, 6 } : new[] { 4, 5, 6, 1, 2, 3 }, ids);
            var pagedSql = statements.Where(sql => sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.Equal(3, pagedSql.Count);
            foreach (var sql in pagedSql)
            {
                var order = sql.Substring(sql.IndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase));
                Assert.Matches(@"(?is)ORDER\s+BY.*[\""\[`. ]Id[\""\]` ]", order);
                if (overload >= 2)
                    Assert.Matches(@"(?is)FirstName.*DESC\s*,.*Id", order);
            }
        }

        [Fact]
        public async Task InsertAsync_ShouldAddEntity()
        {
            var repository = new TestSqlSugarRepository<SqlSugarTestCustomer, int>(_sqlSugarClient);
            var customer = new SqlSugarTestCustomer(1, "John", "Doe", "john.doe@example.com");

            var result = await repository.InsertAsync(customer);

            Assert.NotNull(result);
            Assert.Equal(1, result.Id);
            Assert.Equal("John", result.FirstName);
            Assert.Equal("Doe", result.LastName);
        }

        [Fact]
        public async Task GetAsync_ShouldReturnEntity()
        {
            var repository = new TestSqlSugarRepository<SqlSugarTestCustomer, int>(_sqlSugarClient);
            var customer = new SqlSugarTestCustomer(1, "John", "Doe", "john.doe@example.com");
            await repository.InsertAsync(customer);

            var result = await repository.GetAsync(1);

            Assert.NotNull(result);
            Assert.Equal(1, result.Id);
        }

        [Fact]
        public async Task UpdateAsync_ShouldUpdateEntity()
        {
            var repository = new TestSqlSugarRepository<SqlSugarTestCustomer, int>(_sqlSugarClient);
            var customer = new SqlSugarTestCustomer(1, "John", "Doe", "john.doe@example.com");
            await repository.InsertAsync(customer);
            customer.SetName("Jane", "Smith");

            var result = await repository.UpdateAsync(customer);

            Assert.NotNull(result);
            Assert.Equal("Jane", result.FirstName);
            Assert.Equal("Smith", result.LastName);
        }

        [Fact]
        public async Task DeleteAsync_ShouldDeleteEntity()
        {
            var repository = new TestSqlSugarRepository<SqlSugarTestCustomer, int>(_sqlSugarClient);
            var customer = new SqlSugarTestCustomer(1, "John", "Doe", "john.doe@example.com");
            await repository.InsertAsync(customer);

            await repository.DeleteAsync(customer);
            var result = await repository.GetAsync(1);

            Assert.Null(result);
        }

        [Fact]
        public async Task SoftDelete_ShouldMarkEntityAsDeleted()
        {
            var repository = new TestSqlSugarRepository<SqlSugarTestSoftDeleteEntity, long>(_sqlSugarClient);
            var entity = new SqlSugarTestSoftDeleteEntity(1, "Test Entity");
            await repository.InsertAsync(entity);

            await repository.DeleteAsync(entity);
            var result = await repository.GetAsync(1);

            Assert.Null(result);
        }

        [Fact]
        public async Task GetListAsync_ShouldReturnAllEntities()
        {
            var repository = new TestSqlSugarRepository<SqlSugarTestCustomer, int>(_sqlSugarClient);
            await repository.InsertAsync(new SqlSugarTestCustomer(1, "John", "Doe", "john.doe@example.com"));
            await repository.InsertAsync(new SqlSugarTestCustomer(2, "Jane", "Smith", "jane.smith@example.com"));

            var result = await repository.GetListAsync();

            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
        }
    }

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
