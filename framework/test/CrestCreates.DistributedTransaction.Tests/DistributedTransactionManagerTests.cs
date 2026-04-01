using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.CAP.Implementations;
using Moq;
using Xunit;

namespace CrestCreates.DistributedTransaction.Tests
{
    public class DistributedTransactionManagerTests
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IDistributedTransactionManager _transactionManager;

        public DistributedTransactionManagerTests()
        {
            // 创建一个简单的服务提供者
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddScoped<ITransactionLogger, TransactionLogger>();
            serviceCollection.AddScoped<ITransactionCompensator, DefaultTransactionCompensator>();
            _serviceProvider = serviceCollection.BuildServiceProvider();
            
            _transactionManager = new DistributedTransactionManager(_serviceProvider);
        }

        [Fact]
        public async Task CreateTransactionAsync_ShouldCreateTransaction()
        {
            // Act
            var transaction = await _transactionManager.CreateTransactionAsync();

            // Assert
            Assert.NotNull(transaction);
            Assert.NotEqual(Guid.Empty, transaction.TransactionId);
            Assert.True(_transactionManager.HasActiveTransaction);
            Assert.Equal(transaction, _transactionManager.CurrentTransaction);
        }

        [Fact]
        public async Task ExecuteInTransactionAsync_ShouldExecuteActionInTransaction()
        {
            // Arrange
            var executed = false;

            // Act
            await _transactionManager.ExecuteInTransactionAsync(async () =>
            {
                executed = true;
                Assert.True(_transactionManager.HasActiveTransaction);
                await Task.CompletedTask;
            });

            // Assert
            Assert.True(executed);
            Assert.False(_transactionManager.HasActiveTransaction);
        }

        [Fact]
        public async Task ExecuteInTransactionAsync_WithResult_ShouldReturnResult()
        {
            // Arrange
            const string expectedResult = "test result";

            // Act
            var result = await _transactionManager.ExecuteInTransactionAsync(async () =>
            {
                Assert.True(_transactionManager.HasActiveTransaction);
                await Task.CompletedTask;
                return expectedResult;
            });

            // Assert
            Assert.Equal(expectedResult, result);
            Assert.False(_transactionManager.HasActiveTransaction);
        }

        [Fact]
        public async Task ExecuteInTransactionAsync_WhenExceptionThrown_ShouldRollbackTransaction()
        {
            // Act & Assert
            await Assert.ThrowsAsync<Exception>(async () =>
            {
                await _transactionManager.ExecuteInTransactionAsync(async () =>
                {
                    Assert.True(_transactionManager.HasActiveTransaction);
                    await Task.CompletedTask;
                    throw new Exception("Test exception");
                });
            });

            Assert.False(_transactionManager.HasActiveTransaction);
        }
    }
}
