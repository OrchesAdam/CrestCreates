using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.DataFilter.Entities;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.DTOs;
using CrestCreates.Domain.Shared.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.MultiTenancy.Abstract;
using MongoDB.Driver;

namespace CrestCreates.OrmProviders.MongoDB.Repositories;

/// <summary>
/// MongoDB 仓储实现
/// 注意：MongoDB 是文档数据库，部分关系型语义不支持
/// </summary>
public class MongoRepositoryBase<TEntity, TKey> : ICrestRepositoryBase<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : IEquatable<TKey>
{
    protected readonly IMongoCollection<TEntity> Collection;
    protected readonly ICurrentTenant? CurrentTenant;

    public MongoRepositoryBase(IMongoDatabase database, ICurrentTenant? currentTenant = null)
    {
        var collectionName = GetCollectionName();
        Collection = database.GetCollection<TEntity>(collectionName);
        CurrentTenant = currentTenant;
    }

    protected virtual string GetCollectionName()
    {
        return typeof(TEntity).Name;
    }

    public virtual IQueryable<TEntity> GetQueryable()
    {
        var filter = BuildTenantFilter();
        return filter != null
            ? Collection.AsQueryable().Where(filter)
            : Collection.AsQueryable();
    }

    public async Task<List<TEntity>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var filter = BuildTenantFilterDefinition();
        return await Collection.Find(filter).ToListAsync(cancellationToken);
    }

    public async Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var filter = CombineFilters(BuildTenantFilterDefinition(), predicate);
        return await Collection.Find(filter).ToListAsync(cancellationToken);
    }

    public async Task<List<TEntity>> GetListAsync(
        Expression<Func<TEntity, bool>> predicate,
        Expression<Func<TEntity, object>> orderBy,
        bool ascending = true,
        CancellationToken cancellationToken = default)
    {
        var filter = CombineFilters(BuildTenantFilterDefinition(), predicate);
        var find = Collection.Find(filter);

        if (ascending)
        {
            find = find.SortBy(orderBy);
        }
        else
        {
            find = find.SortByDescending(orderBy);
        }

        return await find.ToListAsync(cancellationToken);
    }

    public async Task<TEntity?> GetAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var idFilter = Builders<TEntity>.Filter.Eq(e => e.Id, id);
        var filter = CombineFilterDefinitions(idFilter, BuildTenantFilterDefinition());
        return await Collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<TEntity?> GetAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var filter = CombineFilters(BuildTenantFilterDefinition(), predicate);
        return await Collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<TEntity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        SetTenantId(entity);
        SetCreationAudit(entity);
        await Collection.InsertOneAsync(entity, cancellationToken: cancellationToken);
        return entity;
    }

    public async Task<IEnumerable<TEntity>> InsertRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        var entityList = entities.ToList();
        foreach (var entity in entityList)
        {
            SetTenantId(entity);
            SetCreationAudit(entity);
        }
        await Collection.InsertManyAsync(entityList, cancellationToken: cancellationToken);
        return entityList;
    }

    public async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ValidateTenantBoundary(entity);
        SetModificationAudit(entity);

        var filter = Builders<TEntity>.Filter.Eq(e => e.Id, entity.Id);

        // 处理并发控制 - 只有当实体实现了 IHasConcurrencyStamp 才需要检查
        if (entity is IHasConcurrencyStamp concurrentEntity)
        {
            // 使用字符串字段名避免对非并发实体的类型转换问题
            var stampFilter = Builders<TEntity>.Filter.Eq("ConcurrencyStamp", concurrentEntity.ConcurrencyStamp);
            filter = Builders<TEntity>.Filter.And(filter, stampFilter);

            concurrentEntity.ConcurrencyStamp = Guid.NewGuid().ToString();
        }

        var result = await Collection.ReplaceOneAsync(filter, entity, cancellationToken: cancellationToken);

        if (result.MatchedCount == 0 && entity is IHasConcurrencyStamp)
        {
            throw new InvalidOperationException("Concurrency conflict: entity was modified by another process.");
        }

        return entity;
    }

    public async Task<IEnumerable<TEntity>> UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        // MongoDB 不支持批量更新多个不同实体的原子操作
        // 这里逐个更新
        var results = new List<TEntity>();
        foreach (var entity in entities)
        {
            results.Add(await UpdateAsync(entity, cancellationToken));
        }
        return results;
    }

    public async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ValidateTenantBoundary(entity);

        if (entity is ISoftDelete softDelete)
        {
            // 软删除
            softDelete.IsDeleted = true;
            softDelete.DeletionTime = DateTime.UtcNow;
            await UpdateAsync(entity, cancellationToken);
        }
        else
        {
            var filter = Builders<TEntity>.Filter.Eq(e => e.Id, entity.Id);
            await Collection.DeleteOneAsync(filter, cancellationToken);
        }
    }

    public async Task DeleteAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var entity = await GetAsync(id, cancellationToken);
        if (entity != null)
        {
            await DeleteAsync(entity, cancellationToken);
        }
    }

    public async Task DeleteAsync(TKey id, string expectedStamp, CancellationToken cancellationToken = default)
    {
        var entity = await GetAsync(id, cancellationToken);
        if (entity == null) return;

        if (entity is IHasConcurrencyStamp concurrentEntity)
        {
            if (string.IsNullOrEmpty(expectedStamp))
            {
                throw new InvalidOperationException("If-Match header required for concurrent entities.");
            }

            if (concurrentEntity.ConcurrencyStamp != expectedStamp)
            {
                throw new InvalidOperationException("Concurrency conflict: entity was modified by another process.");
            }
        }

        await DeleteAsync(entity, cancellationToken);
    }

    public async Task DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        foreach (var entity in entities)
        {
            await DeleteAsync(entity, cancellationToken);
        }
    }

    public async Task DeleteRangeAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var filter = CombineFilters(BuildTenantFilterDefinition(), predicate);

        if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
        {
            // 软删除 - 逐个处理以确保正确的审计字段设置
            // 注意：批量更新字符串字段名可能不匹配 BsonElement 映射
            var entities = await Collection.Find(filter).ToListAsync(cancellationToken);
            foreach (var entity in entities)
            {
                if (entity is ISoftDelete softDelete)
                {
                    softDelete.IsDeleted = true;
                    softDelete.DeletionTime = DateTime.UtcNow;
                    await UpdateAsync(entity, cancellationToken);
                }
            }
        }
        else
        {
            await Collection.DeleteManyAsync(filter, cancellationToken);
        }
    }

    public async Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        var filter = BuildTenantFilterDefinition();
        var total = await Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);

        var items = await Collection.Find(filter)
            .Skip((pageIndex - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TEntity>(items, (int)total, pageIndex, pageSize);
    }

    public async Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var filter = CombineFilters(BuildTenantFilterDefinition(), predicate);
        var total = await Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);

        var items = await Collection.Find(filter)
            .Skip((pageIndex - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TEntity>(items, (int)total, pageIndex, pageSize);
    }

    public async Task<PagedResult<TEntity>> GetPagedAsync(
        int pageIndex,
        int pageSize,
        Expression<Func<TEntity, bool>> predicate,
        Expression<Func<TEntity, object>> orderBy,
        bool ascending = true,
        CancellationToken cancellationToken = default)
    {
        var filter = CombineFilters(BuildTenantFilterDefinition(), predicate);
        var total = await Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);

        var find = Collection.Find(filter);

        if (ascending)
        {
            find = find.SortBy(orderBy);
        }
        else
        {
            find = find.SortByDescending(orderBy);
        }

        var items = await find
            .Skip((pageIndex - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TEntity>(items, (int)total, pageIndex, pageSize);
    }

    public async Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default)
    {
        var filter = BuildTenantFilterDefinition();
        var total = await Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);

        var find = Collection.Find(filter);

        if (ascending)
        {
            find = find.SortBy(orderBy);
        }
        else
        {
            find = find.SortByDescending(orderBy);
        }

        var items = await find
            .Skip((pageIndex - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TEntity>(items, (int)total, pageIndex, pageSize);
    }

    public async Task<long> GetCountAsync(CancellationToken cancellationToken = default)
    {
        var filter = BuildTenantFilterDefinition();
        return await Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
    }

    public async Task<long> GetCountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var filter = CombineFilters(BuildTenantFilterDefinition(), predicate);
        return await Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
    }

    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        var filter = BuildTenantFilterDefinition();
        var count = await Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        return count > 0;
    }

    public async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var filter = CombineFilters(BuildTenantFilterDefinition(), predicate);
        var count = await Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        return count > 0;
    }

    public async Task<bool> ExistsAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var entity = await GetAsync(id, cancellationToken);
        return entity != null;
    }

    #region Protected Methods

    protected FilterDefinition<TEntity>? BuildTenantFilterDefinition()
    {
        if (CurrentTenant == null || string.IsNullOrEmpty(CurrentTenant.Id))
        {
            return null;
        }

        if (typeof(IMustHaveTenant).IsAssignableFrom(typeof(TEntity)))
        {
            return Builders<TEntity>.Filter.Eq("TenantId", CurrentTenant.Id);
        }

        if (typeof(IMultiTenant).IsAssignableFrom(typeof(TEntity)))
        {
            return Builders<TEntity>.Filter.Or(
                Builders<TEntity>.Filter.Eq<string?>("TenantId", null),
                Builders<TEntity>.Filter.Eq<string>("TenantId", CurrentTenant.Id)
            );
        }

        return null;
    }

    protected Expression<Func<TEntity, bool>>? BuildTenantFilter()
    {
        if (CurrentTenant == null || string.IsNullOrEmpty(CurrentTenant.Id))
        {
            return null;
        }

        var tenantId = CurrentTenant.Id;

        if (typeof(IMustHaveTenant).IsAssignableFrom(typeof(TEntity)))
        {
            return e => ((IMustHaveTenant)e).TenantId == tenantId;
        }

        if (typeof(IMultiTenant).IsAssignableFrom(typeof(TEntity)))
        {
            return e => ((IMultiTenant)e).TenantId == null || ((IMultiTenant)e).TenantId == tenantId;
        }

        return null;
    }

    protected FilterDefinition<TEntity> CombineFilters(FilterDefinition<TEntity>? baseFilter, Expression<Func<TEntity, bool>> predicate)
    {
        if (baseFilter == null)
        {
            return Builders<TEntity>.Filter.Where(predicate);
        }
        return Builders<TEntity>.Filter.And(baseFilter, Builders<TEntity>.Filter.Where(predicate));
    }

    protected FilterDefinition<TEntity> CombineFilterDefinitions(FilterDefinition<TEntity> filter1, FilterDefinition<TEntity>? filter2)
    {
        if (filter2 == null) return filter1;
        return Builders<TEntity>.Filter.And(filter1, filter2);
    }

    protected void SetTenantId(TEntity entity)
    {
        if (CurrentTenant == null || string.IsNullOrEmpty(CurrentTenant.Id))
        {
            return;
        }

        if (entity is IMustHaveTenant mustHaveTenant && string.IsNullOrEmpty(mustHaveTenant.TenantId))
        {
            mustHaveTenant.TenantId = CurrentTenant.Id;
        }
        else if (entity is IMultiTenant multiTenant && string.IsNullOrEmpty(multiTenant.TenantId))
        {
            multiTenant.TenantId = CurrentTenant.Id;
        }
    }

    protected void ValidateTenantBoundary(TEntity entity)
    {
        if (CurrentTenant == null || string.IsNullOrEmpty(CurrentTenant.Id))
        {
            return;
        }

        string? entityTenantId = null;
        if (entity is IMustHaveTenant mustHaveTenant)
        {
            entityTenantId = mustHaveTenant.TenantId;
        }
        else if (entity is IMultiTenant multiTenant)
        {
            entityTenantId = multiTenant.TenantId;
        }

        if (!string.IsNullOrEmpty(entityTenantId) && entityTenantId != CurrentTenant.Id)
        {
            throw new InvalidOperationException(
                $"Cannot modify entity from tenant '{entityTenantId}' while current tenant is '{CurrentTenant.Id}'");
        }
    }

    protected void SetCreationAudit(TEntity entity)
    {
        if (entity is IAuditedEntity audited)
        {
            audited.CreationTime = DateTime.UtcNow;
        }
    }

    protected void SetModificationAudit(TEntity entity)
    {
        if (entity is IAuditedEntity audited)
        {
            audited.LastModificationTime = DateTime.UtcNow;
        }
    }

    #endregion
}