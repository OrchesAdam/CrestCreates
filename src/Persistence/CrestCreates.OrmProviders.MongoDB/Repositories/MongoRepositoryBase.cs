using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.DataFilter.Entities;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.DTOs;
using CrestCreates.Domain.Shared.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.MultiTenancy.Abstract;
using MongoDB.Driver;

namespace CrestCreates.OrmProviders.MongoDB.Repositories;

/// <summary>
/// Standalone MongoDB repository; formal managed UoW operations are unsupported.
/// No session/transaction binding or cross-document atomicity is provided.
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
        EnsureStandalone();
        var tenant = TenantId;
        return new GuardedQueryable<TEntity>(Collection.AsQueryable().Where(BuildPolicy(true)), () =>
        {
            EnsureStandalone();
            if (tenant != TenantId) throw new InvalidOperationException("The tenant context changed after this MongoDB query was created.");
        });
    }

    public async Task<List<TEntity>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var filter = BuildReadFilterDefinition();
        return await Collection.Find(filter).ToListAsync(cancellationToken);
    }

    public async Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var filter = CombineFilters(BuildReadFilterDefinition(), predicate);
        return await Collection.Find(filter).ToListAsync(cancellationToken);
    }

    public async Task<List<TEntity>> GetListAsync(
        Expression<Func<TEntity, bool>> predicate,
        Expression<Func<TEntity, object>> orderBy,
        bool ascending = true,
        CancellationToken cancellationToken = default)
    {
        var filter = CombineFilters(BuildReadFilterDefinition(), predicate);
        var find = Collection.Find(filter);

        find = find.Sort(BuildSort(orderBy, ascending));

        return await find.ToListAsync(cancellationToken);
    }

    public async Task<TEntity?> GetAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var idFilter = Builders<TEntity>.Filter.Eq(e => e.Id, id);
        var filter = CombineFilterDefinitions(idFilter, BuildReadFilterDefinition());
        return await Collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<TEntity?> GetAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var filter = CombineFilters(BuildReadFilterDefinition(), predicate);
        return await Collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<TEntity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        EnsureStandalone();
        SetTenantId(entity);
        ValidateTenantBoundary(entity);
        SetCreationAudit(entity);
        await Collection.InsertOneAsync(entity, cancellationToken: cancellationToken);
        return entity;
    }

    public async Task<IEnumerable<TEntity>> InsertRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        EnsureStandalone();
        var entityList = entities.ToList();
        foreach (var entity in entityList)
        {
            SetTenantId(entity);
            ValidateTenantBoundary(entity);
        }
        foreach (var entity in entityList) SetCreationAudit(entity);
        if (entityList.Count == 0) return entityList;
        await Collection.InsertManyAsync(entityList, cancellationToken: cancellationToken);
        return entityList;
    }

    public async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        EnsureStandalone();
        ValidateTenantBoundary(entity);
        var filter = BuildWriteFilter(entity.Id);
        var stamped = entity as IHasConcurrencyStamp;
        var oldStamp = stamped?.ConcurrencyStamp;
        if (stamped != null) filter &= StampFilter(oldStamp!);
        SetModificationAudit(entity);
        if (stamped != null) stamped.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        try
        {
            var result = await Collection.ReplaceOneAsync(filter, entity, cancellationToken: cancellationToken);
            if (result.MatchedCount == 0 && stamped != null) throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
        }
        catch
        {
            if (stamped != null) stamped.ConcurrencyStamp = oldStamp!;
            throw;
        }
        return entity;
    }

    public async Task<IEnumerable<TEntity>> UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        EnsureStandalone();
        if (HasTenant) throw new NotSupportedException("MongoDB tenant UpdateRange is not supported. Update individual entities.");
        var results = new List<TEntity>();
        foreach (var entity in entities)
        {
            results.Add(await UpdateAsync(entity, cancellationToken));
        }
        return results;
    }

    public async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        EnsureStandalone();
        ValidateTenantBoundary(entity);
        var filter = BuildWriteFilter(entity.Id);
        var stamped = entity as IHasConcurrencyStamp;
        if (stamped != null) filter &= StampFilter(stamped.ConcurrencyStamp);
        if (entity is ISoftDelete softDelete)
        {
            var now = DateTime.UtcNow;
            var nextStamp = stamped == null ? null : Guid.NewGuid().ToString("N");
            var result = await Collection.UpdateOneAsync(filter, SoftDeleteUpdate(now, nextStamp), cancellationToken: cancellationToken);
            if (result.MatchedCount == 0 && stamped != null) throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
            if (result.MatchedCount > 0)
            {
                softDelete.IsDeleted = true;
                softDelete.DeletionTime = now;
                if (stamped != null) stamped.ConcurrencyStamp = nextStamp!;
            }
        }
        else
        {
            var result = await Collection.DeleteOneAsync(filter, cancellationToken);
            if (result.DeletedCount == 0 && stamped != null) throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
        }
    }

    public async Task DeleteAsync(TKey id, CancellationToken cancellationToken = default)
    {
        EnsureStandalone();
        var entity = await GetAsync(id, cancellationToken);
        if (entity != null) await DeleteAsync(entity, cancellationToken);
    }

    public async Task DeleteAsync(TKey id, string expectedStamp, CancellationToken cancellationToken = default)
    {
        EnsureStandalone();
        if (typeof(IHasConcurrencyStamp).IsAssignableFrom(typeof(TEntity)))
        {
            var entity = await GetAsync(id, cancellationToken);
            if (entity is not IHasConcurrencyStamp stamped || string.IsNullOrEmpty(expectedStamp) || stamped.ConcurrencyStamp != expectedStamp)
                throw new CrestConcurrencyException(typeof(TEntity).Name, id);
            await DeleteAsync(entity, cancellationToken);
        }
        else await DeleteAsync(id, cancellationToken);
    }

    public async Task DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        EnsureStandalone();
        RejectStampedBatch();
        var items = entities.ToList();
        foreach (var entity in items) ValidateTenantBoundary(entity);
        foreach (var entity in items) await DeleteAsync(entity, cancellationToken);
    }

    public async Task DeleteRangeAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        EnsureStandalone();
        RejectStampedBatch();
        var filter = CombineFilters(Builders<TEntity>.Filter.Where(BuildPolicy(false)), predicate);
        if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
            await Collection.UpdateManyAsync(filter, SoftDeleteUpdate(DateTime.UtcNow, null), cancellationToken: cancellationToken);
        else await Collection.DeleteManyAsync(filter, cancellationToken);
    }

    private static void RejectStampedBatch()
    {
        if (typeof(IHasConcurrencyStamp).IsAssignableFrom(typeof(TEntity)))
            throw new NotSupportedException("DeleteRange has no per-row expectedStamp. Delete stamped entities individually with DeleteAsync(id, expectedStamp).");
    }

    public async Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        var skip = ValidatePage(pageIndex, pageSize);
        var filter = BuildReadFilterDefinition();
        var total = await Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);

        var items = await Collection.Find(filter)
            .Sort(BuildSort(null, true))
            .Skip(skip)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TEntity>(items, checked((int)total), pageIndex, pageSize);
    }

    public async Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var skip = ValidatePage(pageIndex, pageSize);
        var filter = CombineFilters(BuildReadFilterDefinition(), predicate);
        var total = await Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);

        var items = await Collection.Find(filter)
            .Sort(BuildSort(null, true))
            .Skip(skip)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TEntity>(items, checked((int)total), pageIndex, pageSize);
    }

    public async Task<PagedResult<TEntity>> GetPagedAsync(
        int pageIndex,
        int pageSize,
        Expression<Func<TEntity, bool>> predicate,
        Expression<Func<TEntity, object>> orderBy,
        bool ascending = true,
        CancellationToken cancellationToken = default)
    {
        var skip = ValidatePage(pageIndex, pageSize);
        var filter = CombineFilters(BuildReadFilterDefinition(), predicate);
        var total = await Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);

        var find = Collection.Find(filter);

        find = find.Sort(BuildSort(orderBy, ascending));

        var items = await find
            .Skip(skip)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TEntity>(items, checked((int)total), pageIndex, pageSize);
    }

    public async Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default)
    {
        var skip = ValidatePage(pageIndex, pageSize);
        var filter = BuildReadFilterDefinition();
        var total = await Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);

        var find = Collection.Find(filter);

        find = find.Sort(BuildSort(orderBy, ascending));

        var items = await find
            .Skip(skip)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TEntity>(items, checked((int)total), pageIndex, pageSize);
    }

    public async Task<long> GetCountAsync(CancellationToken cancellationToken = default)
    {
        var filter = BuildReadFilterDefinition();
        return await Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
    }

    public async Task<long> GetCountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var filter = CombineFilters(BuildReadFilterDefinition(), predicate);
        return await Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
    }

    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        var filter = BuildReadFilterDefinition();
        var count = await Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        return count > 0;
    }

    public async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var filter = CombineFilters(BuildReadFilterDefinition(), predicate);
        var count = await Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        return count > 0;
    }

    public async Task<bool> ExistsAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var entity = await GetAsync(id, cancellationToken);
        return entity != null;
    }

    #region Protected Methods

    private static bool HasTenant => typeof(IMustHaveTenant).IsAssignableFrom(typeof(TEntity)) || typeof(IMultiTenant).IsAssignableFrom(typeof(TEntity));
    private string? TenantId => string.IsNullOrEmpty(CurrentTenant?.Id) ? null : CurrentTenant.Id;

    private static void EnsureStandalone()
    {
        if (UnitOfWorkExecutionContext.ResolveParticipation(null).Decision != UnitOfWorkParticipationDecision.NoVisibleWindow)
            throw new NotSupportedException("MongoDB repository has no managed unit-of-work binding. Use standalone operations outside the managed scope.");
    }

    private static int ValidatePage(int pageIndex, int pageSize)
    {
        EnsureStandalone();
        if (pageIndex < 0) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
        return checked(pageIndex * pageSize);
    }

    private static SortDefinition<TEntity> BuildSort(Expression<Func<TEntity, object>>? orderBy, bool ascending)
    {
        var sort = Builders<TEntity>.Sort;
        var idSort = sort.Ascending(e => e.Id);
        if (orderBy == null) return idSort;
        Expression body = orderBy.Body;
        if (body is UnaryExpression conversion) body = conversion.Operand;
        var primary = ascending ? sort.Ascending(orderBy) : sort.Descending(orderBy);
        return body is MemberExpression member && member.Member.Name == nameof(IEntity<TKey>.Id) && member.Expression == orderBy.Parameters[0]
            ? primary : sort.Combine(primary, idSort);
    }

    private FilterDefinition<TEntity> BuildReadFilterDefinition()
    {
        EnsureStandalone();
        return Builders<TEntity>.Filter.Where(BuildPolicy(true));
    }

    private FilterDefinition<TEntity> BuildWriteFilter(TKey id) =>
        Builders<TEntity>.Filter.Where(BuildPolicy(false)) & Builders<TEntity>.Filter.Eq(e => e.Id, id);

    // Concrete member expressions let the BSON serializer resolve BsonElement/class-map names.
    private Expression<Func<TEntity, bool>> BuildPolicy(bool includeShared)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "e");
        Expression body = BuildTenantExpression(parameter, includeShared) ?? Expression.Constant(true);
        if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
            body = Expression.AndAlso(body, Expression.Not(Expression.Property(parameter, nameof(ISoftDelete.IsDeleted))));
        return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
    }

    private Expression? BuildTenantExpression(ParameterExpression parameter, bool includeShared)
    {
        if (!HasTenant) return null;
        var tenantId = TenantId;
        if (typeof(IMustHaveTenant).IsAssignableFrom(typeof(TEntity)) && tenantId == null) return Expression.Constant(false);
        var member = Expression.Property(parameter, nameof(IMultiTenant.TenantId));
        var own = Expression.Equal(member, Expression.Constant(tenantId, typeof(string)));
        return includeShared && tenantId != null && !typeof(IMustHaveTenant).IsAssignableFrom(typeof(TEntity))
            ? Expression.OrElse(own, Expression.Equal(member, Expression.Constant(null, typeof(string)))) : own;
    }

    protected FilterDefinition<TEntity>? BuildTenantFilterDefinition() =>
        BuildTenantFilter() is { } filter ? Builders<TEntity>.Filter.Where(filter) : null;

    protected Expression<Func<TEntity, bool>>? BuildTenantFilter()
    {
        var parameter = Expression.Parameter(typeof(TEntity), "e");
        var body = BuildTenantExpression(parameter, true);
        return body == null ? null : Expression.Lambda<Func<TEntity, bool>>(body, parameter);
    }

    private static Expression<Func<TEntity, T>> Field<T>(string property)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "e");
        return Expression.Lambda<Func<TEntity, T>>(Expression.Property(parameter, property), parameter);
    }

    private static FilterDefinition<TEntity> StampFilter(string stamp) =>
        Builders<TEntity>.Filter.Eq(Field<string>(nameof(IHasConcurrencyStamp.ConcurrencyStamp)), stamp);

    private static UpdateDefinition<TEntity> SoftDeleteUpdate(DateTime now, string? stamp)
    {
        var update = Builders<TEntity>.Update.Set(Field<bool>(nameof(ISoftDelete.IsDeleted)), true)
            .Set(Field<DateTime?>(nameof(ISoftDelete.DeletionTime)), now);
        if (stamp != null) update = update.Set(Field<string>(nameof(IHasConcurrencyStamp.ConcurrencyStamp)), stamp);
        return update;
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
        var tenantId = TenantId;
        if (tenantId == null) return;
        if (entity is IMustHaveTenant required && string.IsNullOrEmpty(required.TenantId)) required.TenantId = tenantId;
        else if (entity is IMultiTenant optional && string.IsNullOrEmpty(optional.TenantId)) optional.TenantId = tenantId;
    }

    protected void ValidateTenantBoundary(TEntity entity)
    {
        if (!HasTenant) return;
        var tenantId = TenantId;
        var owner = entity is IMustHaveTenant required ? required.TenantId : ((IMultiTenant)entity).TenantId;
        if ((entity is IMustHaveTenant && tenantId == null) || owner != tenantId)
            throw new InvalidOperationException("Cannot modify a MongoDB document outside the current tenant ownership boundary.");
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

    // Keep guarded roots in the expression tree so LINQ composition cannot discard a source guard.
    // Standard synchronous IQueryable only; repository async methods are the async entry points.
    private interface IGuardedQuerySource
    {
        IQueryable NativeQuery { get; }
        void Validate();
    }

    private sealed class GuardedQueryable<T> : IOrderedQueryable<T>, IGuardedQuerySource
    {
        private readonly IQueryable<T> _inner;
        private readonly Action _guard;
        private readonly GuardedProvider _provider;
        public GuardedQueryable(IQueryable<T> inner, Action guard)
        {
            _inner = inner;
            _guard = guard;
            _provider = new GuardedProvider(inner.Provider, guard);
        }
        public Type ElementType => typeof(T);
        public Expression Expression => Expression.Constant(this);
        public IQueryProvider Provider => _provider;
        IQueryable IGuardedQuerySource.NativeQuery => _inner;
        void IGuardedQuerySource.Validate() => _guard();
        public IEnumerator<T> GetEnumerator()
        {
            _guard();
            return Enumerate().GetEnumerator();
        }
        private IEnumerable<T> Enumerate()
        {
            _guard();
            var native = GuardedProvider.PrepareQuery<T>(_inner.Provider, Expression);
            using var iterator = native.GetEnumerator();
            while (true)
            {
                _guard();
                if (!iterator.MoveNext()) yield break;
                yield return iterator.Current;
            }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class GuardedUntypedQueryable : IOrderedQueryable, IGuardedQuerySource
    {
        private readonly IQueryable _inner;
        private readonly Action _guard;
        private readonly GuardedProvider _provider;
        public GuardedUntypedQueryable(IQueryable inner, Action guard)
        {
            _inner = inner;
            _guard = guard;
            _provider = new GuardedProvider(inner.Provider, guard);
        }
        public Type ElementType => _inner.ElementType;
        public Expression Expression => Expression.Constant(this);
        public IQueryProvider Provider => _provider;
        IQueryable IGuardedQuerySource.NativeQuery => _inner;
        void IGuardedQuerySource.Validate() => _guard();
        public IEnumerator GetEnumerator()
        {
            _guard();
            return Enumerate().GetEnumerator();
        }
        private IEnumerable Enumerate()
        {
            _guard();
            var native = GuardedProvider.PrepareQuery(_inner.Provider, Expression);
            var iterator = native.GetEnumerator();
            try
            {
                while (true)
                {
                    _guard();
                    if (!iterator.MoveNext()) yield break;
                    yield return iterator.Current;
                }
            }
            finally { (iterator as IDisposable)?.Dispose(); }
        }
    }

    private sealed class GuardedComposedQueryable : IOrderedQueryable
    {
        private readonly IQueryProvider _inner;
        private readonly Expression _expression;
        private readonly Action _guard;
        public GuardedComposedQueryable(IQueryProvider inner, Expression expression, Action guard)
        {
            _inner = inner;
            _expression = expression;
            _guard = guard;
        }
        public Type ElementType => _expression.Type.GetGenericArguments()[0];
        public Expression Expression => _expression;
        public IQueryProvider Provider => new GuardedProvider(_inner, _guard);
        public IEnumerator GetEnumerator()
        {
            _guard();
            return GuardedProvider.PrepareQuery(_inner, _expression).GetEnumerator();
        }
    }

    private sealed class GuardedComposedQueryable<T> : IOrderedQueryable<T>
    {
        private readonly IQueryProvider _inner;
        private readonly Expression _expression;
        private readonly Action _guard;
        public GuardedComposedQueryable(IQueryProvider inner, Expression expression, Action guard)
        {
            _inner = inner;
            _expression = expression;
            _guard = guard;
        }
        public Type ElementType => typeof(T);
        public Expression Expression => _expression;
        public IQueryProvider Provider => new GuardedProvider(_inner, _guard);
        public IEnumerator<T> GetEnumerator()
        {
            _guard();
            return GuardedProvider.PrepareQuery<T>(_inner, _expression).GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class GuardedProvider : IQueryProvider
    {
        private readonly IQueryProvider _inner;
        private readonly Action _guard;
        public GuardedProvider(IQueryProvider inner, Action guard) { _inner = inner; _guard = guard; }
        public IQueryable CreateQuery(Expression expression) => ContainsMultipleSources(expression)
            ? new GuardedComposedQueryable(_inner, expression, _guard)
            : new GuardedUntypedQueryable(_inner.CreateQuery(PrepareExpression(_inner, expression)), _guard);
        public IQueryable<T> CreateQuery<T>(Expression expression) => ContainsMultipleSources(expression)
            ? new GuardedComposedQueryable<T>(_inner, expression, _guard)
            : new GuardedQueryable<T>(_inner.CreateQuery<T>(PrepareExpression(_inner, expression)), _guard);
        public object? Execute(Expression expression) => _inner.Execute(PrepareExpression(_inner, expression));
        public T Execute<T>(Expression expression) => _inner.Execute<T>(PrepareExpression(_inner, expression));

        public static IQueryable PrepareQuery(IQueryProvider provider, Expression expression)
        {
            var nativeExpression = PrepareExpression(provider, expression);
            return ReferenceEquals(nativeExpression, expression) && expression is ConstantExpression { Value: IQueryable query }
                ? query : provider.CreateQuery(nativeExpression);
        }

        public static IQueryable<T> PrepareQuery<T>(IQueryProvider provider, Expression expression)
        {
            var nativeExpression = PrepareExpression(provider, expression);
            return ReferenceEquals(nativeExpression, expression) && expression is ConstantExpression { Value: IQueryable<T> query }
                ? query : provider.CreateQuery<T>(nativeExpression);
        }

        private static bool ContainsMultipleSources(Expression expression)
        {
            var visitor = new SourceCountVisitor();
            visitor.Visit(expression);
            return visitor.SourceCount > 1;
        }

        private static Expression PrepareExpression(IQueryProvider provider, Expression expression)
        {
            var sources = new SourceCollectionVisitor();
            sources.Visit(expression);
            if (sources.Count == 0)
                throw new NotSupportedException("A MongoDB query must retain a guarded query source.");
            foreach (var source in sources.Items) source.Validate();
            if (sources.Count > 1)
                throw new NotSupportedException("MongoDB queries with multiple guarded MongoDB query sources are not supported.");
            var nativeExpression = new SourceUnwrapVisitor(provider).Visit(expression);
            return nativeExpression;
        }

        private sealed class SourceCountVisitor : ExpressionVisitor
        {
            public int SourceCount { get; private set; }
            protected override Expression VisitConstant(ConstantExpression node)
            {
                if (node.Value is IGuardedQuerySource) SourceCount++;
                return node;
            }
        }

        private sealed class SourceCollectionVisitor : ExpressionVisitor
        {
            public List<IGuardedQuerySource> Items { get; } = new();
            public int Count => Items.Count;
            protected override Expression VisitConstant(ConstantExpression node)
            {
                if (node.Value is IGuardedQuerySource source) Items.Add(source);
                return node;
            }
        }

        private sealed class SourceUnwrapVisitor : ExpressionVisitor
        {
            private readonly IQueryProvider _provider;
            public SourceUnwrapVisitor(IQueryProvider provider) => _provider = provider;
            protected override Expression VisitConstant(ConstantExpression node)
            {
                if (node.Value is not IGuardedQuerySource source) return node;
                var native = source.NativeQuery;
                if (!ReferenceEquals(native.Provider, _provider))
                    throw new NotSupportedException("MongoDB query sources must use the same query provider.");
                return native.Expression;
            }
        }
    }
}