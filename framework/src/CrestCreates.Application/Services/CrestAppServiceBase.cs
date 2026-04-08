using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using CrestCreates.Aop.Interceptors;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Application.Contracts.Query;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.DataFilter;
using CrestCreates.Domain.Shared.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.Domain.Shared.Permissions;
using CrestCreates.Domain.UnitOfWork;

namespace CrestCreates.Application.Services;

public abstract class CrestAppServiceBase<TEntity, TKey, TDto, TCreateDto, TUpdateDto> : ICrestAppServiceBase<TEntity, TKey, TDto, TCreateDto, TUpdateDto>
    where TEntity : class, IEntity<TKey>
    where TKey : IEquatable<TKey>
{
    private readonly ICrestRepositoryBase<TEntity, TKey> _repository;
    protected virtual ICrestRepositoryBase<TEntity, TKey> Repository => _repository;
    protected readonly IMapper Mapper;
    protected readonly ICurrentUser CurrentUser;
    protected readonly IDataPermissionFilter DataPermissionFilter;
    protected readonly IPermissionChecker PermissionChecker;

    public IServiceProvider? ServiceProvider { get; }

    protected IEntityPermissions? EntityPermissions { get; set; }

    protected CrestAppServiceBase(
        ICrestRepositoryBase<TEntity, TKey> repository,
        IMapper mapper,
        IServiceProvider serviceProvider,
        ICurrentUser currentUser,
        IDataPermissionFilter dataPermissionFilter,
        IPermissionChecker permissionChecker)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        Mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        CurrentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        DataPermissionFilter = dataPermissionFilter ?? throw new ArgumentNullException(nameof(dataPermissionFilter));
        PermissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));
    }

    [Obsolete("工作单元由 UnitOfWorkMoAttribute 统一管理。请注入 IServiceProvider 并使用包含 serviceProvider 参数的构造函数。")]
    protected CrestAppServiceBase(
        ICrestRepositoryBase<TEntity, TKey> repository,
        IMapper mapper,
        ICurrentUser currentUser,
        IDataPermissionFilter dataPermissionFilter,
        IPermissionChecker permissionChecker)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        Mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        CurrentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        DataPermissionFilter = dataPermissionFilter ?? throw new ArgumentNullException(nameof(dataPermissionFilter));
        PermissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));
    }

    [Obsolete("工作单元由 UnitOfWorkMoAttribute 统一管理。请使用不包含 IUnitOfWork 参数的构造函数。")]
    protected CrestAppServiceBase(
        ICrestRepositoryBase<TEntity, TKey> repository,
        IMapper mapper,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IDataPermissionFilter dataPermissionFilter,
        IPermissionChecker permissionChecker)
        : this(repository, mapper, currentUser, dataPermissionFilter, permissionChecker)
    {
    }

    protected virtual string CreatePermissionName => EntityPermissions != null
        ? EntityPermissions.GetPermissionName("Create")
        : $"{typeof(TEntity).Name}.Create";

    protected virtual string UpdatePermissionName => EntityPermissions != null
        ? EntityPermissions.GetPermissionName("Update")
        : $"{typeof(TEntity).Name}.Update";

    protected virtual string DeletePermissionName => EntityPermissions != null
        ? EntityPermissions.GetPermissionName("Delete")
        : $"{typeof(TEntity).Name}.Delete";

    protected virtual string ReadPermissionName => EntityPermissions != null
        ? EntityPermissions.GetPermissionName("Get")
        : $"{typeof(TEntity).Name}.Get";

    protected virtual string SearchPermissionName => EntityPermissions != null
        ? EntityPermissions.GetPermissionName("Search")
        : $"{typeof(TEntity).Name}.Search";

    protected virtual TPermissions GetEntityPermissions<TPermissions>() where TPermissions : IEntityPermissions, new()
    {
        return new TPermissions();
    }

    protected virtual async Task CheckEntityPermissionAsync(string action, CancellationToken cancellationToken = default)
    {
        var permissionName = EntityPermissions != null
            ? EntityPermissions.GetPermissionName(action)
            : $"{typeof(TEntity).Name}.{action}";
        await CheckPermissionAsync(permissionName, cancellationToken);
    }

    protected virtual async Task CheckPermissionAsync(string permissionName, CancellationToken cancellationToken = default)
    {
        await PermissionChecker.CheckAsync(permissionName);
    }

    protected virtual async Task<IQueryable<TEntityFilter>> ApplyDataPermissionFilterAsync<TEntityFilter>(IQueryable<TEntityFilter> query) where TEntityFilter : class
    {
        return await DataPermissionFilter.ApplyFilterAsync(query);
    }

    protected virtual Task SetCreationAuditPropertiesAsync<TEntityAudit>(TEntityAudit entity) where TEntityAudit : class
    {
        if (entity is IMustHaveTenant mustHaveTenant)
        {
            mustHaveTenant.TenantId = CurrentUser.TenantId ?? throw new InvalidOperationException("当前用户没有关联租户");
        }

        if (entity is IMayHaveOrganization mayHaveOrganization)
        {
            mayHaveOrganization.OrganizationId = CurrentUser.OrganizationId;
        }

        var creatorId = Guid.TryParse(CurrentUser.Id, out var userId) ? userId : (Guid?)null;

        if (entity is IHasCreator hasCreator)
        {
            hasCreator.CreatorId = creatorId;
        }

        if (entity is Domain.Shared.Entities.Auditing.IAuditedEntity auditedEntity)
        {
            auditedEntity.CreationTime = DateTime.UtcNow;
            auditedEntity.CreatorId = creatorId;
        }

        return Task.CompletedTask;
    }

    protected virtual Task SetModificationAuditPropertiesAsync<TEntityAudit>(TEntityAudit entity) where TEntityAudit : class
    {
        if (entity is Domain.Shared.Entities.Auditing.IAuditedEntity auditedEntity)
        {
            auditedEntity.LastModificationTime = DateTime.UtcNow;
            auditedEntity.LastModifierId = Guid.TryParse(CurrentUser.Id, out var userId) ? userId : (Guid?)null;
        }

        return Task.CompletedTask;
    }

    protected virtual Task ValidateDataOwnershipAsync<TEntityOwnership>(TEntityOwnership entity) where TEntityOwnership : class
    {
        if (entity is IMustHaveTenant mustHaveTenant)
        {
            if (mustHaveTenant.TenantId != CurrentUser.TenantId)
            {
                throw new UnauthorizedAccessException("您没有权限访问此数据：租户不匹配");
            }
        }

        if (entity is IMayHaveOrganization mayHaveOrganization && mayHaveOrganization.OrganizationId.HasValue)
        {
            if (!CurrentUser.IsInOrganization(mayHaveOrganization.OrganizationId.Value))
            {
                throw new UnauthorizedAccessException("您没有权限访问此数据：组织不匹配");
            }
        }

        return Task.CompletedTask;
    }

    [UnitOfWorkMo]
    public virtual async Task<TDto> CreateAsync(TCreateDto input, CancellationToken cancellationToken = default)
    {
        try
        {
            await CheckPermissionAsync(CreatePermissionName, cancellationToken);
            var entity = MapToEntity(input);
            await SetCreationAuditPropertiesAsync(entity);
            var createdEntity = await Repository.InsertAsync(entity, cancellationToken);
            return MapToDto(createdEntity);
        }
        catch (System.Data.Common.DbException ex)
        {
            throw new Exception($"创建 {typeof(TEntity).Name} 失败: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new Exception($"创建 {typeof(TEntity).Name} 时发生错误: {ex.Message}", ex);
        }
    }

    public virtual async Task<TDto?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ReadPermissionName, cancellationToken);
        var query = Repository.GetQueryable();
        query = await ApplyDataPermissionFilterAsync(query);
        var entity = query.FirstOrDefault(e => e.Id.Equals(id));
        if (entity == null)
        {
            return default;
        }
        await ValidateDataOwnershipAsync(entity);
        return MapToDto(entity);
    }

    public virtual async Task<IReadOnlyList<TDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(SearchPermissionName, cancellationToken);
        var query = Repository.GetQueryable();
        query = await ApplyDataPermissionFilterAsync(query);
        var entities = query.ToList();
        return Mapper.Map<List<TDto>>(entities);
    }

    public virtual async Task<PagedResultDto<TDto>> GetListAsync(PagedRequestDto request, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(SearchPermissionName, cancellationToken);
        var query = Repository.GetQueryable();
        query = await ApplyDataPermissionFilterAsync(query);

        query = QueryExecutor<TEntity>.ApplyFilters(query, request.Filters ?? new List<FilterDescriptor>());
        query = QueryExecutor<TEntity>.ApplySorts(query, request.Sorts ?? new List<SortDescriptor>());

        var totalCount = query.Count();
        query = QueryExecutor<TEntity>.ApplyPaging(query, request.GetSkipCount(), request.PageSize);

        var entities = query.ToList();
        var dtos = Mapper.Map<List<TDto>>(entities);

        return new PagedResultDto<TDto>(dtos, totalCount, request.PageIndex, request.PageSize);
    }

    public virtual async Task<Contracts.DTOs.Common.PagedResultDto<TDto>> QueryAsync(QueryRequest<TEntity> request, CancellationToken cancellationToken = default)
    {
        return await GetListAsync(request, cancellationToken);
    }

    [UnitOfWorkMo]
    public virtual async Task<TDto> UpdateAsync(TKey id, TUpdateDto input, CancellationToken cancellationToken = default)
    {
        try
        {
            await CheckPermissionAsync(UpdatePermissionName, cancellationToken);
            var entity = await Repository.GetAsync(id, cancellationToken);
            if (entity == null)
            {
                throw new KeyNotFoundException($"实体不存在: {id}");
            }

            await ValidateDataOwnershipAsync(entity);
            MapToEntity(input, entity);
            await SetModificationAuditPropertiesAsync(entity);
            var updatedEntity = await Repository.UpdateAsync(entity, cancellationToken);
            return MapToDto(updatedEntity);
        }
        catch (KeyNotFoundException)
        {
            throw;
        }
        catch (System.Data.Common.DbException ex)
        {
            throw new Exception($"更新 {typeof(TEntity).Name} 失败: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new Exception($"更新 {typeof(TEntity).Name} 时发生错误: {ex.Message}", ex);
        }
    }

    [UnitOfWorkMo]
    public virtual async Task DeleteAsync(TKey id, CancellationToken cancellationToken = default)
    {
        try
        {
            await CheckPermissionAsync(DeletePermissionName, cancellationToken);
            var entity = await Repository.GetAsync(id, cancellationToken);
            if (entity != null)
            {
                await ValidateDataOwnershipAsync(entity);
            }

            await Repository.DeleteAsync(id, cancellationToken);
        }
        catch (System.Data.Common.DbException ex)
        {
            throw new Exception($"删除 {typeof(TEntity).Name} 失败: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new Exception($"删除 {typeof(TEntity).Name} 时发生错误: {ex.Message}", ex);
        }
    }

    public virtual async Task<bool> ExistsAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var query = Repository.GetQueryable();
        query = await ApplyDataPermissionFilterAsync(query);
        return query.Any(e => e.Id.Equals(id));
    }

    public virtual async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        var query = Repository.GetQueryable();
        query = await ApplyDataPermissionFilterAsync(query);
        return query.Count();
    }

    protected virtual TEntity MapToEntity(TCreateDto dto)
    {
        return Mapper.Map<TEntity>(dto);
    }

    protected virtual void MapToEntity(TUpdateDto dto, TEntity entity)
    {
        Mapper.Map(dto, entity);
    }

    protected virtual TDto MapToDto(TEntity entity)
    {
        return Mapper.Map<TDto>(entity);
    }
}
