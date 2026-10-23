using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using SqlSugar;
using CrestCreates.Data.Abstractions;
using CrestCreates.DataFilter.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Shared.DTOs;
using CrestCreates.Domain.Shared.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Data.SqlSugar.Repositories
{
    public abstract class SqlSugarRepository<TEntity, TKey> : CrestRepositoryBase<TEntity, TKey>
        where TEntity : class, IEntity<TKey>, new()
        where TKey : IEquatable<TKey>
    {
        protected readonly ISqlSugarClient _sqlSugarClient;
        protected readonly ILogger<SqlSugarRepository<TEntity, TKey>>? _logger;
        private readonly ICurrentUserProvider? _currentUserProvider;
        private readonly ICurrentTenant? _currentTenant;
        private static readonly bool HasTenantMarker =
            typeof(IMustHaveTenant).IsAssignableFrom(typeof(TEntity)) ||
            typeof(IMultiTenant).IsAssignableFrom(typeof(TEntity));
        private static readonly bool HasRequiredTenantMarker =
            typeof(IMustHaveTenant).IsAssignableFrom(typeof(TEntity));

        protected SqlSugarRepository(
            ISqlSugarClient sqlSugarClient,
            ILogger<SqlSugarRepository<TEntity, TKey>>? logger = null,
            ICurrentUserProvider? currentUserProvider = null,
            ICurrentTenant? currentTenant = null)
        {
            _sqlSugarClient = sqlSugarClient;
            _logger = logger;
            _currentUserProvider = currentUserProvider;
            _currentTenant = currentTenant;

            SqlSugarSoftDeleteFilterRegistration.EnsureRegistered(_sqlSugarClient, typeof(TEntity));
        }

        private string ColumnName(string clrName)
        {
            var mappedName = _sqlSugarClient.EntityMaintenance.GetDbColumnName(clrName, typeof(TEntity));
            return global::SqlSugar.InstanceFactory
                .GetSqlBuilderWithContext(_sqlSugarClient)
                .GetTranslationColumnName(mappedName);
        }



        private void ApplyCreationAudit(TEntity entity)
        {
            if (entity is not IAuditedEntity audited)
            {
                return;
            }

            audited.CreationTime = DateTime.UtcNow;
            audited.CreatorId ??= _currentUserProvider?.GetCurrentUserId();
        }

        private void ApplyModificationAudit(TEntity entity)
        {
            if (entity is not IAuditedEntity audited)
            {
                return;
            }

            audited.LastModificationTime = DateTime.UtcNow;
            audited.LastModifierId = _currentUserProvider?.GetCurrentUserId();
        }

        private (string Sql, Dictionary<string, object> Parameters)? TenantFilter()
        {
            if (!HasTenantMarker)
            {
                return null;
            }

            var tenantId = _currentTenant?.Id;
            var column = ColumnName(nameof(IMustHaveTenant.TenantId));
            var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
            if (HasRequiredTenantMarker)
            {
                if (string.IsNullOrEmpty(tenantId))
                {
                    throw new InvalidOperationException(
                        $"{typeof(TEntity).Name} requires a current tenant for this operation.");
                }

                parameters["tenantId"] = tenantId;
                return ($"{column} = @tenantId", parameters);
            }

            if (string.IsNullOrEmpty(tenantId))
            {
                return ($"{column} IS NULL", parameters);
            }

            parameters["tenantId"] = tenantId;
            return ($"({column} = @tenantId OR {column} IS NULL)", parameters);
        }

        private (string Sql, Dictionary<string, object> Parameters)? WriteTenantFilter()
        {
            if (!HasTenantMarker)
            {
                return null;
            }

            var tenantId = _currentTenant?.Id;
            var column = ColumnName(nameof(IMustHaveTenant.TenantId));
            var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
            if (HasRequiredTenantMarker)
            {
                if (string.IsNullOrEmpty(tenantId))
                {
                    throw new InvalidOperationException(
                        $"{typeof(TEntity).Name} requires a current tenant for this write.");
                }

                parameters["tenantId"] = tenantId;
                return ($"{column} = @tenantId", parameters);
            }

            if (string.IsNullOrEmpty(tenantId))
            {
                return ($"{column} IS NULL", parameters);
            }

            parameters["tenantId"] = tenantId;
            return ($"{column} = @tenantId", parameters);
        }

        private void AddTenantCondition(List<string> conditions, Dictionary<string, object> parameters)
        {
            var filter = WriteTenantFilter();
            if (filter is null)
            {
                return;
            }

            conditions.Add(filter.Value.Sql);
            foreach (var parameter in filter.Value.Parameters)
            {
                parameters[parameter.Key] = parameter.Value;
            }
        }

        private void ApplyTenantOnInsert(TEntity entity)
        {
            if (!HasTenantMarker)
            {
                return;
            }

            var tenantId = _currentTenant?.Id;
            if (HasRequiredTenantMarker)
            {
                if (string.IsNullOrEmpty(tenantId))
                {
                    throw new InvalidOperationException(
                        $"{typeof(TEntity).Name} requires a current tenant for insert.");
                }

                var mustHaveTenant = (IMustHaveTenant)entity;
                if (string.IsNullOrEmpty(mustHaveTenant.TenantId))
                {
                    mustHaveTenant.TenantId = tenantId;
                }
                else if (!string.Equals(mustHaveTenant.TenantId, tenantId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Cannot insert {typeof(TEntity).Name} for tenant '{mustHaveTenant.TenantId}' while the current tenant is '{tenantId}'.");
                }
                return;
            }

            var multiTenant = (IMultiTenant)entity;
            if (string.IsNullOrEmpty(multiTenant.TenantId) && !string.IsNullOrEmpty(tenantId))
            {
                multiTenant.TenantId = tenantId;
            }
            else if (!string.IsNullOrEmpty(multiTenant.TenantId) &&
                     !string.Equals(multiTenant.TenantId, tenantId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Cannot insert {typeof(TEntity).Name} for tenant '{multiTenant.TenantId}' while the current tenant is '{tenantId}'.");
            }
        }

        private static string? ReadTenantId(TEntity entity)
        {
            return entity switch
            {
                IMustHaveTenant mustHaveTenant => mustHaveTenant.TenantId,
                IMultiTenant multiTenant => multiTenant.TenantId,
                _ => null
            };
        }

        private async Task<TEntity?> FindPersistedForTenantCheckAsync(TKey id)
        {
            if (!HasTenantMarker)
            {
                return null;
            }

            var query = _sqlSugarClient.Queryable<TEntity>()
                .ClearFilter()
                .Where($"{ColumnName("Id")} = @entityId", new { entityId = id });
            if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
            {
                query = query.Where($"{ColumnName(nameof(ISoftDelete.IsDeleted))} = 0", new { });
            }

            return await query.FirstAsync();
        }

        private void EnsurePersistedTenant(TEntity? persisted, TKey id)
        {
            if (!HasTenantMarker || persisted is null)
            {
                return;
            }

            var currentTenant = _currentTenant?.Id;
            var persistedTenant = ReadTenantId(persisted);
            if (!string.Equals(persistedTenant, currentTenant, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{typeof(TEntity).Name} (Id={id}) belongs to tenant '{persistedTenant}', not the current tenant '{currentTenant}'.");
            }
        }

        private void EnsureSubmittedTenant(TEntity entity, TEntity? persisted)
        {
            if (!HasTenantMarker)
            {
                return;
            }

            var currentTenant = _currentTenant?.Id;
            var submittedTenant = ReadTenantId(entity);
            if (!string.Equals(submittedTenant, currentTenant, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Cannot write {typeof(TEntity).Name} for tenant '{submittedTenant}' while the current tenant is '{currentTenant}'.");
            }

        }

        public override IQueryable<TEntity> GetQueryable()
        {
            throw new NotSupportedException(
                "SqlSugar repositories do not expose a composable IQueryable. Use the asynchronous repository query methods or the SDK query API directly.");
        }

        private ISugarQueryable<TEntity> Queryable()
        {
            var query = _sqlSugarClient.Queryable<TEntity>();
            var tenantFilter = TenantFilter();
            return tenantFilter is null
                ? query
                : query.Where(tenantFilter.Value.Sql, tenantFilter.Value.Parameters);
        }

        public override async Task<List<TEntity>> GetListAsync(CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            cancellationToken = effective;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Getting all entities {EntityType}", typeof(TEntity).Name);
                var result = await Queryable()
                    .ToListAsync();
                _logger?.LogDebug("Got {Count} entities {EntityType}", result.Count, typeof(TEntity).Name);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting all entities {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            cancellationToken = effective;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Finding entities {EntityType}", typeof(TEntity).Name);
                var result = await Queryable()
                    .Where(predicate)
                    .ToListAsync();
                _logger?.LogDebug("Found {Count} entities {EntityType}", result.Count, typeof(TEntity).Name);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error finding entities {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            cancellationToken = effective;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Finding entities {EntityType} with order", typeof(TEntity).Name);
                var query = Queryable().Where(predicate);
                query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
                var result = await query.ToListAsync();
                _logger?.LogDebug("Found {Count} entities {EntityType}", result.Count, typeof(TEntity).Name);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error finding entities {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<TEntity?> GetAsync(TKey id, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            cancellationToken = effective;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Getting entity {EntityType} by id: {Id}", typeof(TEntity).Name, id);
                var result = await Queryable()
                    .InSingleAsync(id);
                _logger?.LogDebug("Got entity {EntityType} by id: {Id}", typeof(TEntity).Name, id);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting entity {EntityType} by id: {Id}", typeof(TEntity).Name, id);
                throw;
            }
        }

        public override async Task<TEntity?> GetAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            cancellationToken = effective;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Getting entity {EntityType} by predicate", typeof(TEntity).Name);
                var result = await Queryable()
                    .Where(predicate)
                    .FirstAsync();
                _logger?.LogDebug("Got entity {EntityType}", typeof(TEntity).Name);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting entity {EntityType} by predicate", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<TEntity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            cancellationToken = effective;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Adding entity {EntityType}", typeof(TEntity).Name);
                ApplyTenantOnInsert(entity);
                ApplyCreationAudit(entity);
                await _sqlSugarClient.Insertable(entity).IgnoreColumns("DomainEvents").ExecuteCommandAsync();
                _logger?.LogDebug("Added entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
                return entity;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error adding entity {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<IEnumerable<TEntity>> InsertRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            cancellationToken = effective;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Inserting many entities {EntityType}", typeof(TEntity).Name);
                var entityList = entities.ToList();
                foreach (var item in entityList)
                {
                    ApplyTenantOnInsert(item);
                    ApplyCreationAudit(item);
                }
                await _sqlSugarClient.Insertable(entityList).IgnoreColumns("DomainEvents").ExecuteCommandAsync();
                _logger?.LogDebug("Inserted {Count} entities {EntityType}", entityList.Count, typeof(TEntity).Name);
                return entityList;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error inserting many entities {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            cancellationToken = effective;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Updating entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
                if (HasTenantMarker)
                {
                    var persistedForTenant = await FindPersistedForTenantCheckAsync(entity.Id);
                    EnsurePersistedTenant(persistedForTenant, entity.Id);
                    EnsureSubmittedTenant(entity, persistedForTenant);
                }
                ApplyModificationAudit(entity);
                if (entity is IHasConcurrencyStamp stamp)
                {
                    var oldStamp = stamp.ConcurrencyStamp;
                    stamp.ConcurrencyStamp = Guid.NewGuid().ToString();
                    var conditions = new List<string>
                    {
                        $"{ColumnName("Id")} = @entityId",
                        $"{ColumnName(nameof(IHasConcurrencyStamp.ConcurrencyStamp))} = @oldStamp"
                    };
                    var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["entityId"] = entity.Id,
                        ["oldStamp"] = oldStamp
                    };
                    AddTenantCondition(conditions, parameters);
                    var rows = await _sqlSugarClient.Updateable(entity)
                        .IgnoreColumns("DomainEvents")
                        .Where(string.Join(" AND ", conditions), parameters)
                        .ExecuteCommandAsync();
                    if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
                }
                else
                {
                    var update = _sqlSugarClient.Updateable(entity).IgnoreColumns("DomainEvents");
                    var tenantFilter = TenantFilter();
                    if (tenantFilter is not null)
                    {
                        update = update.Where(tenantFilter.Value.Sql, tenantFilter.Value.Parameters);
                    }
                    await update.ExecuteCommandAsync();
                }
                _logger?.LogDebug("Updated entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
                return entity;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
                throw;
            }
        }

        public override async Task<IEnumerable<TEntity>> UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            cancellationToken = effective;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Updating many entities {EntityType}", typeof(TEntity).Name);
                var entityList = entities.ToList();
                if (HasTenantMarker)
                {
                    throw new NotSupportedException(
                        "UpdateRangeAsync does not validate persisted tenant ownership. Update tenant-scoped entities one by one with UpdateAsync.");
                }
                if (entityList.Any(e => e is IHasConcurrencyStamp))
                    throw new NotSupportedException("UpdateRangeAsync does not support entities with IHasConcurrencyStamp. Use UpdateAsync for concurrency-safe updates.");
                foreach (var item in entityList)
                {
                    ApplyModificationAudit(item);
                }
                await _sqlSugarClient.Updateable(entityList).IgnoreColumns("DomainEvents").ExecuteCommandAsync();
                _logger?.LogDebug("Updated {Count} entities {EntityType}", entityList.Count, typeof(TEntity).Name);
                return entityList;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating many entities {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            cancellationToken = effective;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Deleting entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
                if (HasTenantMarker)
                {
                    var persistedForTenant = await FindPersistedForTenantCheckAsync(entity.Id);
                    EnsurePersistedTenant(persistedForTenant, entity.Id);
                }
                if (entity is ISoftDelete)
                {
                    if (entity is IHasConcurrencyStamp stamp)
                    {
                        var softConditions = new List<string>
                        {
                            $"{ColumnName("Id")} = @entityId",
                            $"{ColumnName(nameof(IHasConcurrencyStamp.ConcurrencyStamp))} = @entityStamp",
                            $"{ColumnName(nameof(ISoftDelete.IsDeleted))} = 0"
                        };
                        var softParameters = new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["entityId"] = entity.Id,
                            ["entityStamp"] = stamp.ConcurrencyStamp
                        };
                        AddTenantCondition(softConditions, softParameters);
                        var rows = await SqlSugarSoftDeleteOperation.ExecuteAsync(
                            _sqlSugarClient.Updateable<TEntity>().Where(
                                string.Join(" AND ", softConditions), softParameters),
                            _currentUserProvider,
                            advanceConcurrencyStamp: true,
                            cancellationToken);
                        if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
                    }
                    else
                    {
                        var softConditions = new List<string>
                        {
                            $"{ColumnName("Id")} = @entityId",
                            $"{ColumnName(nameof(ISoftDelete.IsDeleted))} = 0"
                        };
                        var softParameters = new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["entityId"] = entity.Id
                        };
                        AddTenantCondition(softConditions, softParameters);
                        await SqlSugarSoftDeleteOperation.ExecuteAsync(
                            _sqlSugarClient.Updateable<TEntity>().Where(
                                string.Join(" AND ", softConditions), softParameters),
                            _currentUserProvider,
                            advanceConcurrencyStamp: false,
                            cancellationToken);
                    }
                }
                else if (entity is IHasConcurrencyStamp stamp)
                {
                    var conditions = new List<string>
                    {
                        $"{ColumnName("Id")} = @entityId",
                        $"{ColumnName(nameof(IHasConcurrencyStamp.ConcurrencyStamp))} = @entityStamp"
                    };
                    var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["entityId"] = entity.Id,
                        ["entityStamp"] = stamp.ConcurrencyStamp
                    };
                    AddTenantCondition(conditions, parameters);
                    var rows = await _sqlSugarClient.Deleteable<TEntity>()
                        .Where(string.Join(" AND ", conditions), parameters)
                        .ExecuteCommandAsync();
                    if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, entity.Id);
                }
                else if (HasTenantMarker)
                {
                    var conditions = new List<string> { $"{ColumnName("Id")} = @entityId" };
                    var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["entityId"] = entity.Id
                    };
                    AddTenantCondition(conditions, parameters);
                    await _sqlSugarClient.Deleteable<TEntity>()
                        .Where(string.Join(" AND ", conditions), parameters)
                        .ExecuteCommandAsync();
                }
                else
                {
                    await _sqlSugarClient.Deleteable(entity).ExecuteCommandAsync();
                }
                _logger?.LogDebug("Deleted entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting entity {EntityType} with id: {Id}", typeof(TEntity).Name, entity.Id);
                throw;
            }
        }

        public override async Task DeleteAsync(TKey id, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            cancellationToken = effective;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Deleting entity {EntityType} by id: {Id}", typeof(TEntity).Name, id);
                if (HasTenantMarker)
                {
                    var persistedForTenant = await FindPersistedForTenantCheckAsync(id);
                    EnsurePersistedTenant(persistedForTenant, id);
                    if (persistedForTenant is null)
                    {
                        return;
                    }
                }
                if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
                {
                    var conditions = new List<string>
                    {
                        $"{ColumnName("Id")} = @entityId",
                        $"{ColumnName(nameof(ISoftDelete.IsDeleted))} = 0"
                    };
                    var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["entityId"] = id
                    };
                    AddTenantCondition(conditions, parameters);
                    await SqlSugarSoftDeleteOperation.ExecuteAsync(
                        _sqlSugarClient.Updateable<TEntity>().Where(
                            string.Join(" AND ", conditions), parameters),
                        _currentUserProvider,
                        advanceConcurrencyStamp: typeof(IHasConcurrencyStamp).IsAssignableFrom(typeof(TEntity)),
                        cancellationToken);
                }
                else if (HasTenantMarker)
                {
                    var conditions = new List<string> { $"{ColumnName("Id")} = @entityId" };
                    var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["entityId"] = id
                    };
                    AddTenantCondition(conditions, parameters);
                    await _sqlSugarClient.Deleteable<TEntity>()
                        .Where(string.Join(" AND ", conditions), parameters)
                        .ExecuteCommandAsync();
                }
                else
                {
                    await _sqlSugarClient.Deleteable<TEntity>()
                        .In(id)
                        .ExecuteCommandAsync();
                }
                _logger?.LogDebug("Deleted entity {EntityType} by id: {Id}", typeof(TEntity).Name, id);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting entity {EntityType} by id: {Id}", typeof(TEntity).Name, id);
                throw;
            }
        }

        public override async Task DeleteAsync(TKey id, string expectedStamp, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            cancellationToken = effective;
            try
            {
                if (!typeof(IHasConcurrencyStamp).IsAssignableFrom(typeof(TEntity)))
                {
                    await DeleteAsync(id, cancellationToken);
                    return;
                }
                _logger?.LogDebug("Deleting entity {EntityType} by id with concurrency check: {Id}", typeof(TEntity).Name, id);
                if (HasTenantMarker)
                {
                    var persistedForTenant = await FindPersistedForTenantCheckAsync(id);
                    EnsurePersistedTenant(persistedForTenant, id);
                    if (persistedForTenant is null)
                    {
                        throw new CrestConcurrencyException(typeof(TEntity).Name, id);
                    }
                }
                if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
                {
                    var softConditions = new List<string>
                    {
                        $"{ColumnName("Id")} = @entityId",
                        $"{ColumnName(nameof(IHasConcurrencyStamp.ConcurrencyStamp))} = @entityStamp",
                        $"{ColumnName(nameof(ISoftDelete.IsDeleted))} = 0"
                    };
                    var softParameters = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["entityId"] = id,
                        ["entityStamp"] = expectedStamp
                    };
                    AddTenantCondition(softConditions, softParameters);
                    var softRows = await SqlSugarSoftDeleteOperation.ExecuteAsync(
                        _sqlSugarClient.Updateable<TEntity>().Where(
                            string.Join(" AND ", softConditions), softParameters),
                        _currentUserProvider,
                        advanceConcurrencyStamp: true,
                        cancellationToken);
                    if (softRows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, id);
                }
                else
                {
                    var physicalConditions = new List<string>
                    {
                        $"{ColumnName("Id")} = @entityId",
                        $"{ColumnName(nameof(IHasConcurrencyStamp.ConcurrencyStamp))} = @entityStamp"
                    };
                    var physicalParameters = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["entityId"] = id,
                        ["entityStamp"] = expectedStamp
                    };
                    AddTenantCondition(physicalConditions, physicalParameters);
                    var rows = await _sqlSugarClient.Deleteable<TEntity>()
                        .Where(string.Join(" AND ", physicalConditions), physicalParameters)
                        .ExecuteCommandAsync();
                    if (rows == 0) throw new CrestConcurrencyException(typeof(TEntity).Name, id);
                }
                _logger?.LogDebug("Deleted entity {EntityType} by id with concurrency check: {Id}", typeof(TEntity).Name, id);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting entity {EntityType} by id with concurrency check: {Id}", typeof(TEntity).Name, id);
                throw;
            }
        }

        public override async Task DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            cancellationToken = effective;
            try
            {
                ThrowIfBatchDeleteHasNoPerRowStamp();
                _logger?.LogDebug("Deleting many entities {EntityType}", typeof(TEntity).Name);
                var entityList = entities.ToList();
                var ids = entityList.Select(entity => entity.Id).ToList();
                if (ids.Count == 0)
                {
                    return;
                }

                if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
                {
                    var conditions = new List<string> { $"{ColumnName(nameof(ISoftDelete.IsDeleted))} = 0" };
                    var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
                    AddTenantCondition(conditions, parameters);
                    await SqlSugarSoftDeleteOperation.ExecuteAsync(
                        _sqlSugarClient.Updateable<TEntity>()
                            .Where(entity => ids.Contains(entity.Id))
                            .Where(string.Join(" AND ", conditions), parameters),
                        _currentUserProvider,
                        advanceConcurrencyStamp: false,
                        cancellationToken);
                }
                else if (HasTenantMarker)
                {
                    var conditions = new List<string>();
                    var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
                    AddTenantCondition(conditions, parameters);
                    await _sqlSugarClient.Deleteable<TEntity>()
                        .Where(entity => ids.Contains(entity.Id))
                        .Where(string.Join(" AND ", conditions), parameters)
                        .ExecuteCommandAsync();
                }
                else
                {
                    await _sqlSugarClient.Deleteable(entityList)
                        .ExecuteCommandAsync();
                }
                _logger?.LogDebug("Deleted {Count} entities {EntityType}", entityList.Count, typeof(TEntity).Name);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting many entities {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task DeleteRangeAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            cancellationToken = effective;
            try
            {
                ThrowIfBatchDeleteHasNoPerRowStamp();
                _logger?.LogDebug("Deleting many entities {EntityType} by predicate", typeof(TEntity).Name);
                if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
                {
                    var conditions = new List<string> { $"{ColumnName(nameof(ISoftDelete.IsDeleted))} = 0" };
                    var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
                    AddTenantCondition(conditions, parameters);
                    await SqlSugarSoftDeleteOperation.ExecuteAsync(
                        _sqlSugarClient.Updateable<TEntity>()
                            .Where(predicate)
                            .Where(string.Join(" AND ", conditions), parameters),
                        _currentUserProvider,
                        advanceConcurrencyStamp: false,
                        cancellationToken);
                }
                else if (HasTenantMarker)
                {
                    var conditions = new List<string>();
                    var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
                    AddTenantCondition(conditions, parameters);
                    await _sqlSugarClient.Deleteable<TEntity>()
                        .Where(predicate)
                        .Where(string.Join(" AND ", conditions), parameters)
                        .ExecuteCommandAsync();
                }
                else
                {
                    await _sqlSugarClient.Deleteable<TEntity>()
                        .Where(predicate)
                        .ExecuteCommandAsync();
                }
                _logger?.LogDebug("Deleted entities {EntityType} by predicate", typeof(TEntity).Name);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting many entities {EntityType} by predicate", typeof(TEntity).Name);
                throw;
            }
        }

        private static void ThrowIfBatchDeleteHasNoPerRowStamp()
        {
            if (typeof(IHasConcurrencyStamp).IsAssignableFrom(typeof(TEntity)))
            {
                throw new NotSupportedException(
                    "DeleteRangeAsync does not accept entities with concurrency stamps because the batch overload has no per-row expectedStamp. Delete stamped entities one by one with DeleteAsync(id, expectedStamp).");
            }
        }

        public override async Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            cancellationToken = effective;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Getting paged entities {EntityType} - Page {PageIndex}, Size {PageSize}", typeof(TEntity).Name, pageIndex, pageSize);
                var total = await Queryable().CountAsync();
                var items = await Queryable()
                    .Skip(pageIndex * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
                _logger?.LogDebug("Got {Count} entities {EntityType} out of {Total}", items.Count, typeof(TEntity).Name, total);
                return new PagedResult<TEntity>(items, (int)total, pageIndex, pageSize);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting paged entities {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            cancellationToken = effective;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Getting paged entities {EntityType} with predicate - Page {PageIndex}, Size {PageSize}", typeof(TEntity).Name, pageIndex, pageSize);
                var total = await Queryable().Where(predicate).CountAsync();
                var items = await Queryable()
                    .Where(predicate)
                    .Skip(pageIndex * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
                _logger?.LogDebug("Got {Count} entities {EntityType} out of {Total}", items.Count, typeof(TEntity).Name, total);
                return new PagedResult<TEntity>(items, (int)total, pageIndex, pageSize);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting paged entities {EntityType} with predicate", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            cancellationToken = effective;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Getting paged entities {EntityType} with predicate and order - Page {PageIndex}, Size {PageSize}", typeof(TEntity).Name, pageIndex, pageSize);
                var query = Queryable().Where(predicate);
                query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
                var total = await query.CountAsync();
                var items = await query
                    .Skip(pageIndex * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
                _logger?.LogDebug("Got {Count} entities {EntityType} out of {Total}", items.Count, typeof(TEntity).Name, total);
                return new PagedResult<TEntity>(items, (int)total, pageIndex, pageSize);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting paged entities {EntityType} with predicate and order", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<PagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<TEntity, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            cancellationToken = effective;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Getting paged entities {EntityType} with order - Page {PageIndex}, Size {PageSize}", typeof(TEntity).Name, pageIndex, pageSize);
                var query = Queryable();
                query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
                var total = await query.CountAsync();
                var items = await query
                    .Skip(pageIndex * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
                _logger?.LogDebug("Got {Count} entities {EntityType} out of {Total}", items.Count, typeof(TEntity).Name, total);
                return new PagedResult<TEntity>(items, (int)total, pageIndex, pageSize);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting paged entities {EntityType} with order", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<long> GetCountAsync(CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            cancellationToken = effective;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Getting count of entities {EntityType}", typeof(TEntity).Name);
                var count = await Queryable().CountAsync();
                _logger?.LogDebug("Got count {Count} for entities {EntityType}", count, typeof(TEntity).Name);
                return count;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting count of entities {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<long> GetCountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            cancellationToken = effective;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Getting count of entities {EntityType} with predicate", typeof(TEntity).Name);
                var count = await Queryable().Where(predicate).CountAsync();
                _logger?.LogDebug("Got count {Count} for entities {EntityType} with predicate", count, typeof(TEntity).Name);
                return count;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting count of entities {EntityType} with predicate", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            cancellationToken = effective;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Checking if any entities {EntityType} exist", typeof(TEntity).Name);
                var result = await Queryable().AnyAsync();
                _logger?.LogDebug("Any entities {EntityType} exist: {Result}", typeof(TEntity).Name, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error checking if any entities {EntityType} exist", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            cancellationToken = effective;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Checking if any entities {EntityType} match predicate", typeof(TEntity).Name);
                var result = await Queryable().Where(predicate).AnyAsync();
                _logger?.LogDebug("Any entities {EntityType} match predicate: {Result}", typeof(TEntity).Name, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error checking if any entities {EntityType} match predicate", typeof(TEntity).Name);
                throw;
            }
        }

        public override async Task<bool> ExistsAsync(TKey id, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
            cancellationToken = effective;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogDebug("Checking if entity {EntityType} with id {Id} exists", typeof(TEntity).Name, id);
                var result = await Queryable().Where(e => e.Id.Equals(id)).AnyAsync();
                _logger?.LogDebug("Entity {EntityType} with id {Id} exists: {Result}", typeof(TEntity).Name, id, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error checking if entity {EntityType} with id {Id} exists", typeof(TEntity).Name, id);
                throw;
            }
        }
    }
}
