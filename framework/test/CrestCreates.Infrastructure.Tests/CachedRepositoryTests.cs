using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Entities;
using CrestCreates.Infrastructure.Caching;
using CrestCreates.Infrastructure.Caching.Repository;

namespace CrestCreates.Infrastructure.Tests
{
    public class CachedRepositoryTests
    {
        private readonly Mock<IRepository<TestEntity, Guid>> _innerRepositoryMock;
        private readonly Mock<ICache> _cacheMock;
        private readonly Mock<ILogger<CachedRepository<TestEntity, Guid>>> _loggerMock;
        private readonly CachedRepository<TestEntity, Guid> _cachedRepository;

        public CachedRepositoryTests()
        {
            _innerRepositoryMock = new Mock<IRepository<TestEntity, Guid>>();
            _cacheMock = new Mock<ICache>();
            _loggerMock = new Mock<ILogger<CachedRepository<TestEntity, Guid>>>();
            _cachedRepository = new CachedRepository<TestEntity, Guid>(
                _innerRepositoryMock.Object,
                _cacheMock.Object,
                _loggerMock.Object,
                TimeSpan.FromMinutes(10));
        }

        [Fact]
        public async Task GetByIdAsync_Should_Return_From_Cache_When_Available()
        {
            var id = Guid.NewGuid();
            var expectedEntity = new TestEntity { Id = id };
            var cacheKey = $"repo:testentity:byid:{id}";

            _cacheMock.Setup(c => c.GetAsync<TestEntity>(cacheKey, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedEntity);

            var result = await _cachedRepository.GetByIdAsync(id);

            result.Should().Be(expectedEntity);
            _innerRepositoryMock.Verify(r => r.GetByIdAsync(id), Times.Never);
        }

        [Fact]
        public async Task GetByIdAsync_Should_Get_From_Repository_And_Cache_When_Not_Available()
        {
            var id = Guid.NewGuid();
            var expectedEntity = new TestEntity { Id = id };
            var cacheKey = $"repo:testentity:byid:{id}";

            _cacheMock.Setup(c => c.GetAsync<TestEntity>(cacheKey, It.IsAny<CancellationToken>()))
                .ReturnsAsync((TestEntity?)null);
            _innerRepositoryMock.Setup(r => r.GetByIdAsync(id))
                .ReturnsAsync(expectedEntity);

            var result = await _cachedRepository.GetByIdAsync(id);

            result.Should().Be(expectedEntity);
            _innerRepositoryMock.Verify(r => r.GetByIdAsync(id), Times.Once);
            _cacheMock.Verify(c => c.SetAsync(cacheKey, expectedEntity, TimeSpan.FromMinutes(10), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetAllAsync_Should_Return_From_Cache_When_Available()
        {
            var expectedEntities = new List<TestEntity>
            {
                new TestEntity { Id = Guid.NewGuid() },
                new TestEntity { Id = Guid.NewGuid() }
            };
            var cacheKey = "repo:testentity:all";

            _cacheMock.Setup(c => c.GetAsync<List<TestEntity>>(cacheKey, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedEntities);

            var result = await _cachedRepository.GetAllAsync();

            result.Should().BeEquivalentTo(expectedEntities);
            _innerRepositoryMock.Verify(r => r.GetAllAsync(), Times.Never);
        }

        [Fact]
        public async Task AddAsync_Should_Add_To_Repository_And_Invalidate_Cache()
        {
            var entity = new TestEntity { Id = Guid.NewGuid() };
            var entityCacheKey = $"repo:testentity:byid:{entity.Id}";
            var allCacheKey = "repo:testentity:all";

            _innerRepositoryMock.Setup(r => r.AddAsync(entity))
                .ReturnsAsync(entity);

            var result = await _cachedRepository.AddAsync(entity);

            result.Should().Be(entity);
            _innerRepositoryMock.Verify(r => r.AddAsync(entity), Times.Once);
            _cacheMock.Verify(c => c.RemoveAsync(entityCacheKey, It.IsAny<CancellationToken>()), Times.Once);
            _cacheMock.Verify(c => c.RemoveAsync(allCacheKey, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdateAsync_Should_Update_Repository_And_Invalidate_Cache()
        {
            var entity = new TestEntity { Id = Guid.NewGuid() };
            var entityCacheKey = $"repo:testentity:byid:{entity.Id}";
            var allCacheKey = "repo:testentity:all";

            _innerRepositoryMock.Setup(r => r.UpdateAsync(entity))
                .ReturnsAsync(entity);

            var result = await _cachedRepository.UpdateAsync(entity);

            result.Should().Be(entity);
            _innerRepositoryMock.Verify(r => r.UpdateAsync(entity), Times.Once);
            _cacheMock.Verify(c => c.RemoveAsync(entityCacheKey, It.IsAny<CancellationToken>()), Times.Once);
            _cacheMock.Verify(c => c.RemoveAsync(allCacheKey, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteAsync_Should_Delete_From_Repository_And_Invalidate_Cache()
        {
            var entity = new TestEntity { Id = Guid.NewGuid() };
            var entityCacheKey = $"repo:testentity:byid:{entity.Id}";
            var allCacheKey = "repo:testentity:all";

            var result = _cachedRepository.DeleteAsync(entity);
            await result;

            _innerRepositoryMock.Verify(r => r.DeleteAsync(entity), Times.Once);
            _cacheMock.Verify(c => c.RemoveAsync(entityCacheKey, It.IsAny<CancellationToken>()), Times.Once);
            _cacheMock.Verify(c => c.RemoveAsync(allCacheKey, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task FindAsync_Should_Return_From_Cache_When_Available()
        {
            var predicate = (Expression<Func<TestEntity, bool>>)(e => e.Id != Guid.Empty);
            var predicateHash = predicate.ToString().GetHashCode().ToString("x");
            var cacheKey = $"repo:testentity:find:{predicateHash}";
            var expectedEntities = new List<TestEntity> { new TestEntity { Id = Guid.NewGuid() } };

            _cacheMock.Setup(c => c.GetAsync<List<TestEntity>>(cacheKey, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedEntities);

            var result = await _cachedRepository.FindAsync(predicate);

            result.Should().BeEquivalentTo(expectedEntities);
            _innerRepositoryMock.Verify(r => r.FindAsync(predicate), Times.Never);
        }

        [Fact]
        public async Task FindAsync_Should_Get_From_Repository_And_Cache_When_Not_Available()
        {
            var predicate = (Expression<Func<TestEntity, bool>>)(e => e.Id != Guid.Empty);
            var predicateHash = predicate.ToString().GetHashCode().ToString("x");
            var cacheKey = $"repo:testentity:find:{predicateHash}";
            var expectedEntities = new List<TestEntity> { new TestEntity { Id = Guid.NewGuid() } };

            _cacheMock.Setup(c => c.GetAsync<List<TestEntity>>(cacheKey, It.IsAny<CancellationToken>()))
                .ReturnsAsync((List<TestEntity>?)null);
            _innerRepositoryMock.Setup(r => r.FindAsync(predicate))
                .ReturnsAsync(expectedEntities);

            var result = await _cachedRepository.FindAsync(predicate);

            result.Should().BeEquivalentTo(expectedEntities);
            _innerRepositoryMock.Verify(r => r.FindAsync(predicate), Times.Once);
            _cacheMock.Verify(c => c.SetAsync(cacheKey, expectedEntities, TimeSpan.FromMinutes(10), It.IsAny<CancellationToken>()), Times.Once);
        }

        public class TestEntity : IEntity<Guid>
        {
            public Guid Id { get; set; }
        }
    }
}
