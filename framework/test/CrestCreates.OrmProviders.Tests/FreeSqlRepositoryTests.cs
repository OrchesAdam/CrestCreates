using System;
using System.Threading.Tasks;
using Xunit;
using CrestCreates.OrmProviders.FreeSqlProvider.Repositories;
using CrestCreates.Domain.Entities;

namespace CrestCreates.OrmProviders.Tests
{
    public class FreeSqlRepositoryTests
    {
        [Fact]
        public async Task AddAsync_ShouldAddEntity()
        {
            // Arrange
            var repository = new FreeSqlRepository<TestOrder, long>();
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
            var repository = new FreeSqlRepository<TestOrder, long>();
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
            var repository = new FreeSqlRepository<TestOrder, long>();
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
            var repository = new FreeSqlRepository<TestOrder, long>();
            var order = new TestOrder(1, "ORD-001", Guid.NewGuid(), 100.00m);
            await repository.AddAsync(order);

            // Act
            await repository.DeleteAsync(1);
            var result = await repository.GetByIdAsync(1);

            // Assert
            Assert.Null(result);
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
