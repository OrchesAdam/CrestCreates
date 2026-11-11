using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using CrestCreates.Data.FreeSql.Repositories;
using CrestCreates.Data.FreeSql.UnitOfWork;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Shared.Entities;
using FreeSql;
using Microsoft.Extensions.Logging;

namespace CrestCreates.OrmProviders.Tests
{
    public class TestFreeSqlRepository<TEntity, TKey> : FreeSqlRepository<TEntity, TKey>
        where TEntity : class, IEntity<TKey>
        where TKey : IEquatable<TKey>
    {
        public TestFreeSqlRepository(FreeSqlUnitOfWorkManager uowManager, ILogger<FreeSqlRepository<TEntity, TKey>> logger = null) : base(uowManager, logger)
        {
        }

        public override IQueryable<TEntity> GetQueryableUnfiltered()
        {
            throw new NotImplementedException();
        }

        public override Task DeleteAsync(TKey id, string expectedStamp, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("FreeSql concurrency delete not yet implemented.");
        }
    }

    public class FreeSqlRepositoryTests : OrmTestBase
    {
        private readonly IFreeSql _freeSql;
        private readonly FreeSqlUnitOfWorkManager _uowManager;

        public FreeSqlRepositoryTests()
        {
            _freeSql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, "DataSource=:memory:")
                .Build();
            
            _freeSql.CodeFirst.SyncStructure<TestOrder>();
            _freeSql.CodeFirst.SyncStructure<TestSoftDeleteEntity>();
            
            _uowManager = new FreeSqlUnitOfWorkManager(_freeSql);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public async Task GetPagedAsync_ShouldUseStableTypedOrdering(int overload)
        {
            var repository = new TestFreeSqlRepository<TestOrder, long>(_uowManager);
            for (var id = 6; id >= 1; id--)
                await repository.InsertAsync(new TestOrder(id, id <= 3 ? "A" : "B", Guid.NewGuid(), 100m));

            var statements = new System.Collections.Generic.List<string>();
            _freeSql.Aop.CommandBefore += (_, args) => statements.Add(args.Command.CommandText);
            var ids = new System.Collections.Generic.List<long>();
            for (var page = 0; page < 3; page++)
            {
                var result = overload switch
                {
                    0 => await repository.GetPagedAsync(page, 2),
                    1 => await repository.GetPagedAsync(page, 2, entity => entity.Id > 0),
                    2 => await repository.GetPagedAsync(page, 2, entity => entity.Id > 0, entity => entity.OrderNumber, false),
                    _ => await repository.GetPagedAsync(page, 2, entity => entity.OrderNumber, false)
                };
                ids.AddRange(result.Items.Select(entity => entity.Id));
            }
            Assert.Equal(overload < 2 ? new long[] { 1, 2, 3, 4, 5, 6 } : new long[] { 4, 5, 6, 1, 2, 3 }, ids);
            var pagedSql = statements.Where(sql => sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.Equal(3, pagedSql.Count);
            foreach (var sql in pagedSql)
            {
                var order = sql.Substring(sql.IndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase));
                Assert.Matches(@"(?is)ORDER\s+BY.*[\""\[`. ]Id[\""\]` ]", order);
                if (overload >= 2)
                    Assert.Matches(@"(?is)OrderNumber.*DESC\s*,.*Id", order);
            }
        }

        [Fact]
        public async Task InsertAsync_ShouldAddEntity()
        {
            var repository = new TestFreeSqlRepository<TestOrder, long>(_uowManager);
            var order = new TestOrder(1, "ORD-001", Guid.NewGuid(), 100.00m);

            var result = await repository.InsertAsync(order);

            Assert.NotNull(result);
            Assert.Equal(1, result.Id);
            Assert.Equal("ORD-001", result.OrderNumber);
        }

        [Fact]
        public async Task GetAsync_ShouldReturnEntity()
        {
            var repository = new TestFreeSqlRepository<TestOrder, long>(_uowManager);
            var order = new TestOrder(1, "ORD-001", Guid.NewGuid(), 100.00m);
            await repository.InsertAsync(order);

            var result = await repository.GetAsync(1);

            Assert.NotNull(result);
            Assert.Equal(1, result.Id);
        }

        [Fact]
        public async Task UpdateAsync_ShouldUpdateEntity()
        {
            var repository = new TestFreeSqlRepository<TestOrder, long>(_uowManager);
            var order = new TestOrder(1, "ORD-001", Guid.NewGuid(), 100.00m);
            await repository.InsertAsync(order);
            order.SetOrderNumber("ORD-002");

            var result = await repository.UpdateAsync(order);

            Assert.NotNull(result);
            Assert.Equal("ORD-002", result.OrderNumber);
        }

        [Fact]
        public async Task DeleteAsync_ShouldDeleteEntity()
        {
            var repository = new TestFreeSqlRepository<TestOrder, long>(_uowManager);
            var order = new TestOrder(1, "ORD-001", Guid.NewGuid(), 100.00m);
            await repository.InsertAsync(order);

            await repository.DeleteAsync(order);
            var result = await repository.GetAsync(1);

            Assert.Null(result);
        }

        [Fact]
        public async Task SoftDelete_ShouldMarkEntityAsDeleted()
        {
            var repository = new TestFreeSqlRepository<TestSoftDeleteEntity, long>(_uowManager);
            var entity = new TestSoftDeleteEntity(1, "Test Entity");
            await repository.InsertAsync(entity);

            await repository.DeleteAsync(entity);
            var result = await repository.GetAsync(1);

            Assert.Null(result);
        }

        [Fact]
        public async Task GetListAsync_ShouldReturnAllEntities()
        {
            var repository = new TestFreeSqlRepository<TestOrder, long>(_uowManager);
            await repository.InsertAsync(new TestOrder(1, "Order 1", Guid.NewGuid(), 100.00m));
            await repository.InsertAsync(new TestOrder(2, "Order 2", Guid.NewGuid(), 200.00m));

            var result = await repository.GetListAsync();

            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
        }
    }

    public class TestOrder : AggregateRoot<long>
    {
        public string OrderNumber { get; set; } = string.Empty;
        public Guid CustomerId { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime OrderDate { get; set; }
        public OrderStatus Status { get; set; }
        public string Notes { get; set; } = string.Empty;

        public TestOrder()
        {
        }

        public TestOrder(long id, string orderNumber, Guid customerId, decimal totalAmount)
        {
            Id = id;
            OrderNumber = orderNumber;
            CustomerId = customerId;
            TotalAmount = totalAmount;
            OrderDate = DateTime.Now;
            Status = OrderStatus.Pending;
        }

        public void SetOrderNumber(string orderNumber)
        {
            OrderNumber = orderNumber;
        }
    }

    public enum OrderStatus
    {
        Pending = 1,
        Confirmed = 2,
        Shipped = 3,
        Completed = 4,
        Cancelled = 5
    }
}
