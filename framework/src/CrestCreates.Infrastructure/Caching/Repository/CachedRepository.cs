using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Infrastructure.Caching.Repository
{
    public class CachedRepository<TEntity, TId> : IRepository<TEntity, TId>
        where TEntity : class, IEntity<TId>
        where TId : IEquatable<TId>
    {
        private readonly IRepository<TEntity, TId> _innerRepository;
        private readonly ICache _cache;
        private readonly ILogger<CachedRepository<TEntity, TId>> _logger;
        private readonly string _cacheKeyPrefix;
        private readonly TimeSpan _defaultExpiration;

        public CachedRepository(
            IRepository<TEntity, TId> innerRepository,
            ICache cache,
            ILogger<CachedRepository<TEntity, TId>> logger,
            TimeSpan? defaultExpiration = null)
        {
            _innerRepository = innerRepository ?? throw new ArgumentNullException(nameof(innerRepository));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheKeyPrefix = $"repo:{typeof(TEntity).Name.ToLowerInvariant()}";
            _defaultExpiration = defaultExpiration ?? TimeSpan.FromMinutes(10);
        }

        public async Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken = default)
        {
            var cacheKey = GenerateCacheKey("byid", id);
            
            var cached = await _cache.GetAsync<TEntity>(cacheKey);
            if (cached != null)
            {
                _logger.LogDebug("Cache hit for {EntityType} with id {Id}", typeof(TEntity).Name, id);
                return cached;
            }

            var entity = await _innerRepository.GetByIdAsync(id, cancellationToken);
            if (entity != null)
            {
                await _cache.SetAsync(cacheKey, entity, _defaultExpiration);
                _logger.LogDebug("Cached {EntityType} with id {Id}", typeof(TEntity).Name, id);
            }

            return entity;
        }

        public async Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var cacheKey = GenerateCacheKey("all");
            
            var cached = await _cache.GetAsync<List<TEntity>>(cacheKey);
            if (cached != null)
            {
                _logger.LogDebug("Cache hit for all {EntityType}", typeof(TEntity).Name);
                return cached;
            }

            var entities = await _innerRepository.GetAllAsync(cancellationToken);
            await _cache.SetAsync(cacheKey, entities, _defaultExpiration);
            _logger.LogDebug("Cached all {EntityType}, count: {Count}", typeof(TEntity).Name, entities.Count);

            return entities;
        }

        public async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            var result = await _innerRepository.AddAsync(entity, cancellationToken);
            await InvalidateEntityCacheAsync(entity.Id);
            await InvalidateAllCacheAsync();
            _logger.LogDebug("Invalidated cache after adding {EntityType} with id {Id}", typeof(TEntity).Name, entity.Id);
            return result;
        }

        public async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            var result = await _innerRepository.UpdateAsync(entity, cancellationToken);
            await InvalidateEntityCacheAsync(entity.Id);
            await InvalidateAllCacheAsync();
            _logger.LogDebug("Invalidated cache after updating {EntityType} with id {Id}", typeof(TEntity).Name, entity.Id);
            return result;
        }

        public async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            await _innerRepository.DeleteAsync(entity, cancellationToken);
            await InvalidateEntityCacheAsync(entity.Id);
            await InvalidateAllCacheAsync();
            _logger.LogDebug("Invalidated cache after deleting {EntityType} with id {Id}", typeof(TEntity).Name, entity.Id);
        }

        public async Task<List<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var predicateKey = predicate.ToString().GetHashCode().ToString("x");
            var cacheKey = GenerateCacheKey("find", predicateKey);
            
            var cached = await _cache.GetAsync<List<TEntity>>(cacheKey);
            if (cached != null)
            {
                _logger.LogDebug("Cache hit for find {EntityType} with predicate {PredicateKey}", typeof(TEntity).Name, predicateKey);
                return cached;
            }

            var entities = await _innerRepository.FindAsync(predicate, cancellationToken);
            await _cache.SetAsync(cacheKey, entities, _defaultExpiration);
            _logger.LogDebug("Cached find {EntityType} with predicate {PredicateKey}, count: {Count}", typeof(TEntity).Name, predicateKey, entities.Count);

            return entities;
        }

        private string GenerateCacheKey(params object[] parts)
        {
            var keyParts = new List<string> { _cacheKeyPrefix };
            keyParts.AddRange(parts.Select(p => p?.ToString() ?? "null"));
            return string.Join(":", keyParts);
        }

        private async Task InvalidateEntityCacheAsync(TId id)
        {
            var cacheKey = GenerateCacheKey("byid", id);
            await _cache.RemoveAsync(cacheKey);
        }

        private async Task InvalidateAllCacheAsync()
        {
            var allCacheKey = GenerateCacheKey("all");
            await _cache.RemoveAsync(allCacheKey);
        }
    }
}
