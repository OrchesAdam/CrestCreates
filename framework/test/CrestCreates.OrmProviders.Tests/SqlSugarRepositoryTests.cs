using System;
using System.Threading.Tasks;
using Xunit;
using CrestCreates.OrmProviders.SqlSugar.Repositories;
using CrestCreates.Domain.Entities;

namespace CrestCreates.OrmProviders.Tests
{
    public class SqlSugarRepositoryTests
    {
        [Fact]
        public async Task AddAsync_ShouldAddEntity()
        {
            // Arrange
            var repository = new SqlSugarRepository<TestCustomer, int>();
            var customer = new TestCustomer(1, "John", "Doe", "john.doe@example.com");

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
            var repository = new SqlSugarRepository<TestCustomer, int>();
            var customer = new TestCustomer(1, "John", "Doe", "john.doe@example.com");
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
            var repository = new SqlSugarRepository<TestCustomer, int>();
            var customer = new TestCustomer(1, "John", "Doe", "john.doe@example.com");
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
            var repository = new SqlSugarRepository<TestCustomer, int>();
            var customer = new TestCustomer(1, "John", "Doe", "john.doe@example.com");
            await repository.AddAsync(customer);

            // Act
            await repository.DeleteAsync(1);
            var result = await repository.GetByIdAsync(1);

            // Assert
            Assert.Null(result);
        }
    }

    // 测试实体类
    public class TestCustomer : AggregateRoot<int>
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public DateTime BirthDate { get; set; }
        public bool IsVip { get; set; }

        public TestCustomer()
        {
        }

        public TestCustomer(int id, string firstName, string lastName, string email)
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
}
