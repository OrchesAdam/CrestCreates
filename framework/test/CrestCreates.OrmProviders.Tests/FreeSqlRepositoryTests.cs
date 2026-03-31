using System;
using System.Threading.Tasks;
using Xunit;
using CrestCreates.OrmProviders.FreeSqlProvider.Repositories;
using CrestCreates.OrmProviders.FreeSqlProvider.UnitOfWork;
using CrestCreates.Domain.Entities;
using FreeSql;

namespace CrestCreates.OrmProviders.Tests
{
    // 具体的 FreeSql 仓储实现
    public class TestFreeSqlRepository<TEntity, TKey> : FreeSqlRepository<TEntity, TKey>
        where TEntity : class, IEntity<TKey>
        where TKey : IEquatable<TKey>
    {
        public TestFreeSqlRepository(FreeSqlUnitOfWorkManager uowManager) : base(uowManager, null)
        {}
    }

    public class FreeSqlRepositoryTests : OrmTestBase
    {
        private readonly IFreeSql _freeSql;
        private readonly FreeSqlUnitOfWorkManager _uowManager;

        public FreeSqlRepositoryTests()
        {
            // 创建内存数据库
            _freeSql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, "DataSource=:memory:")
                .Build();
            
            // 自动迁移表结构
            _freeSql.CodeFirst.SyncStructure<TestOrder>();
            _freeSql.CodeFirst.SyncStructure<TestSoftDeleteEntity>();
            
            _uowManager = new FreeSqlUnitOfWorkManager(_freeSql);
        }

        [Fact]
        public async Task AddAsync_ShouldAddEntity()
        {
            // Arrange
            var repository = new TestFreeSqlRepository<TestOrder, long>(_uowManager);
            var order = new TestOrder(1, "ORD-001", Guid.NewGuid(), 100.00m);

            // Act
            var result = await repository.AddAsync(order);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(1, result.Id);
            Assert.Equal("ORD-001", result.OrderNumber);
        }

        [Fact]
        public async Task GetByIdAsync_ShouldReturnEntity()
        {
            // Arrange
            var repository = new TestFreeSqlRepository<TestOrder, long>(_uowManager);
            var order = new TestOrder(1, "ORD-001", Guid.NewGuid(), 100.00m);
            await repository.AddAsync(order);

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
            var repository = new TestFreeSqlRepository<TestOrder, long>(_uowManager);
            var order = new TestOrder(1, "ORD-001", Guid.NewGuid(), 100.00m);
            await repository.AddAsync(order);
            order.SetOrderNumber("ORD-002");

            // Act
            var result = await repository.UpdateAsync(order);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("ORD-002", result.OrderNumber);
        }

        [Fact]
        public async Task DeleteAsync_ShouldDeleteEntity()
        {
            // Arrange
            var repository = new TestFreeSqlRepository<TestOrder, long>(_uowManager);
            var order = new TestOrder(1, "ORD-001", Guid.NewGuid(), 100.00m);
            await repository.AddAsync(order);

            // Act
            await repository.DeleteAsync(order);
            var result = await repository.GetByIdAsync(1);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task SoftDelete_ShouldMarkEntityAsDeleted()
        {
            // Arrange
            var repository = new TestFreeSqlRepository<TestSoftDeleteEntity, long>(_uowManager);
            var entity = new TestSoftDeleteEntity(1, "Test Entity");
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
            var repository = new TestFreeSqlRepository<TestOrder, long>(_uowManager);
            await repository.AddAsync(new TestOrder(1, "Order 1", Guid.NewGuid(), 100.00m));
            await repository.AddAsync(new TestOrder(2, "Order 2", Guid.NewGuid(), 200.00m));

            // Act
            var result = await repository.GetAllAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
        }
    }

    // 测试实体类
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
