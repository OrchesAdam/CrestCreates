using System;
using System.Threading.Tasks;
using Xunit;
using Moq;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SqlSugar;
using FreeSql;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Entities;
using CrestCreates.OrmProviders.EFCore.UnitOfWork;
using CrestCreates.OrmProviders.SqlSugar.UnitOfWork;
using CrestCreates.OrmProviders.FreeSqlProvider.UnitOfWork;
using CrestCreates.EventBus.Local;
using CrestCreates.EventBus.Tests.Events;

namespace CrestCreates.EventBus.Tests
{
    public class UnitOfWorkEventTests
    {
        [Fact]
        public async Task EfCoreUnitOfWork_Should_Publish_DomainEvents()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase(databaseName: "TestDatabase")
                .Options;

            var dbContext = new TestDbContext(options);
            var mediatorMock = new Mock<IMediator>();
            var domainEventPublisher = new DomainEventPublisher(mediatorMock.Object);
            var unitOfWork = new EfCoreUnitOfWork(dbContext, domainEventPublisher);

            var entity = new TestEntity(Guid.NewGuid()) { Name = "Test Entity" };
            var domainEvent = new TestDomainEvent(entity.Id);
            entity.AddDomainEvent(domainEvent);

            // Act
            await unitOfWork.BeginTransactionAsync();
            dbContext.Add(entity);
            await unitOfWork.CommitTransactionAsync();

            // Assert
            mediatorMock.Verify(m => m.Publish(It.IsAny<IDomainEvent>(), default), Times.Once);
            entity.DomainEvents.Should().BeEmpty();
        }

        [Fact]
        public async Task EfCoreUnitOfWork_Should_Not_Publish_DomainEvents_On_Rollback()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase(databaseName: "TestDatabase_Rollback")
                .Options;

            var dbContext = new TestDbContext(options);
            var mediatorMock = new Mock<IMediator>();
            var domainEventPublisher = new DomainEventPublisher(mediatorMock.Object);
            var unitOfWork = new EfCoreUnitOfWork(dbContext, domainEventPublisher);

            var entity = new TestEntity(Guid.NewGuid()) { Name = "Test Entity" };
            var domainEvent = new TestDomainEvent(entity.Id);
            entity.AddDomainEvent(domainEvent);

            // Act
            await unitOfWork.BeginTransactionAsync();
            dbContext.Add(entity);
            await unitOfWork.RollbackTransactionAsync();

            // Assert
            mediatorMock.Verify(m => m.Publish(It.IsAny<object>(), default), Times.Never);
        }

        [Fact]
        public async Task EfCoreUnitOfWork_Should_Retry_Event_Publishing_On_Failure()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase(databaseName: "TestDatabase_Retry")
                .Options;

            var dbContext = new TestDbContext(options);
            var mediatorMock = new Mock<IMediator>();
            mediatorMock.Setup(m => m.Publish(It.IsAny<IDomainEvent>(), default))
                .Throws(new Exception("Publishing failed"))
                .Verifiable();
            
            var domainEventPublisher = new DomainEventPublisher(mediatorMock.Object);
            var unitOfWork = new EfCoreUnitOfWork(dbContext, domainEventPublisher);

            var entity = new TestEntity(Guid.NewGuid()) { Name = "Test Entity" };
            var domainEvent = new TestDomainEvent(entity.Id);
            entity.AddDomainEvent(domainEvent);

            // Act & Assert
            await unitOfWork.BeginTransactionAsync();
            dbContext.Add(entity);
            await unitOfWork.CommitTransactionAsync();

            // Should retry 3 times
            mediatorMock.Verify(m => m.Publish(It.IsAny<IDomainEvent>(), default), Times.Exactly(3));
        }

        [Fact]
        public async Task SqlSugarUnitOfWork_Should_Publish_DomainEvents()
        {
            // Arrange
            var connectionString = "Data Source=:memory:";
            var sqlSugarClient = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = connectionString,
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true
            });

            var mediatorMock = new Mock<IMediator>();
            var domainEventPublisher = new DomainEventPublisher(mediatorMock.Object);
            var unitOfWork = new SqlSugarUnitOfWork(sqlSugarClient, domainEventPublisher);

            var entity = new TestEntity(Guid.NewGuid()) { Name = "Test Entity" };
            var domainEvent = new TestDomainEvent(entity.Id);
            entity.AddDomainEvent(domainEvent);

            // Act
            await unitOfWork.BeginTransactionAsync();
            unitOfWork.TrackEntity<TestEntity, Guid>(entity);
            await unitOfWork.CommitTransactionAsync();

            // Assert
            mediatorMock.Verify(m => m.Publish(It.IsAny<IDomainEvent>(), default), Times.Once);
            entity.DomainEvents.Should().BeEmpty();
        }

        [Fact]
        public async Task FreeSqlUnitOfWork_Should_Publish_DomainEvents()
        {
            // Arrange
            var connectionString = "Data Source=:memory:";
            var freeSql = new FreeSqlBuilder()
                .UseConnectionString(DataType.Sqlite, connectionString)
                .UseAutoSyncStructure(true)
                .Build();

            var mediatorMock = new Mock<IMediator>();
            var domainEventPublisher = new DomainEventPublisher(mediatorMock.Object);
            var unitOfWork = new FreeSqlUnitOfWork(freeSql, domainEventPublisher);

            var entity = new TestEntity(Guid.NewGuid()) { Name = "Test Entity" };
            var domainEvent = new TestDomainEvent(entity.Id);
            entity.AddDomainEvent(domainEvent);

            // Act
            await unitOfWork.BeginTransactionAsync();
            unitOfWork.TrackEntity<TestEntity, Guid>(entity);
            await unitOfWork.CommitTransactionAsync();

            // Assert
            mediatorMock.Verify(m => m.Publish(It.IsAny<IDomainEvent>(), default), Times.Once);
            entity.DomainEvents.Should().BeEmpty();
        }
    }

    // 测试 DbContext
    public class TestDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public TestDbContext(Microsoft.EntityFrameworkCore.DbContextOptions<TestDbContext> options) : base(options)
        {}

        public Microsoft.EntityFrameworkCore.DbSet<TestEntity> TestEntities { get; set; }

        protected override void OnConfiguring(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.ConfigureWarnings(warnings => warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));
        }

        protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestEntity>().HasKey(e => e.Id);
        }
    }
}
