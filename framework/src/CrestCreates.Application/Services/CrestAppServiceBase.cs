using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Application.Contracts.Query;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.UnitOfWork;

namespace CrestCreates.Application.Services;

public abstract class CrestAppServiceBase<TEntity, TDto, TCreateDto, TUpdateDto, TKey> : ICrestAppServiceBase<TEntity, TDto, TCreateDto, TUpdateDto, TKey>
    where TEntity : class
    where TKey : IEquatable<TKey>
{
    protected readonly ICrestRepositoryBase<TEntity, TKey> Repository;
    protected readonly IMapper Mapper;
    protected readonly IUnitOfWork UnitOfWork;

    protected CrestAppServiceBase(ICrestRepositoryBase<TEntity, TKey> repository, IMapper mapper, IUnitOfWork unitOfWork)
    {
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        Mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        UnitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    protected virtual string CreatePermissionName => $"{typeof(TEntity).Name}.Create";
    protected virtual string UpdatePermissionName => $"{typeof(TEntity).Name}.Update";
    protected virtual string DeletePermissionName => $"{typeof(TEntity).Name}.Delete";
    protected virtual string ReadPermissionName => $"{typeof(TEntity).Name}.Read";

    protected virtual Task CheckPermissionAsync(string permissionName, CancellationToken cancellationToken = default)
    {
        // 预留权限检查逻辑，实际项目中需要集成权限系统
        // 例如：await PermissionChecker.IsGrantedAsync(permissionName, cancellationToken);
        return Task.CompletedTask;
    }

    public virtual async Task<TDto> CreateAsync(TCreateDto input, CancellationToken cancellationToken = default)
    {
        try
        {
            await CheckPermissionAsync(CreatePermissionName, cancellationToken);
            await UnitOfWork.BeginTransactionAsync();
            try
            {
                var entity = MapToEntity(input);
                var createdEntity = await Repository.InsertAsync(entity, cancellationToken);
                await UnitOfWork.CommitTransactionAsync();
                return MapToDto(createdEntity);
            }
            catch
            {
                await UnitOfWork.RollbackTransactionAsync();
                throw;
            }
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
        var entity = await Repository.GetAsync(id, cancellationToken);
        return entity == null ? default : MapToDto(entity);
    }

    public virtual async Task<IReadOnlyList<TDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ReadPermissionName, cancellationToken);
        var entities = await Repository.GetListAsync(cancellationToken);
        return Mapper.Map<List<TDto>>(entities);
    }

    public virtual async Task<Contracts.DTOs.Common.PagedResult<TDto>> GetListAsync(PagedRequestDto request, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(ReadPermissionName, cancellationToken);
        var query = Repository.GetQueryable();

        query = QueryExecutor<TEntity>.ApplyFilters(query, request.Filters ?? new List<FilterDescriptor>());
        query = QueryExecutor<TEntity>.ApplySorts(query, request.Sorts ?? new List<SortDescriptor>());

        var totalCount = query.Count();
        query = QueryExecutor<TEntity>.ApplyPaging(query, request.GetSkipCount(), request.PageSize);

        var entities = query.ToList();
        var dtos = Mapper.Map<List<TDto>>(entities);

        return new Contracts.DTOs.Common.PagedResult<TDto>(dtos, totalCount, request.PageIndex, request.PageSize);
    }

    public virtual async Task<Contracts.DTOs.Common.PagedResult<TDto>> QueryAsync(QueryRequest<TEntity> request, CancellationToken cancellationToken = default)
    {
        return await GetListAsync(request, cancellationToken);
    }

    public virtual async Task<TDto> UpdateAsync(TKey id, TUpdateDto input, CancellationToken cancellationToken = default)
    {
        try
        {
            await CheckPermissionAsync(UpdatePermissionName, cancellationToken);
            await UnitOfWork.BeginTransactionAsync();
            try
            {
                var entity = await Repository.GetAsync(id, cancellationToken);
                if (entity == null)
                {
                    throw new KeyNotFoundException($"实体不存在: {id}");
                }

                MapToEntity(input, entity);
                var updatedEntity = await Repository.UpdateAsync(entity, cancellationToken);
                await UnitOfWork.CommitTransactionAsync();
                return MapToDto(updatedEntity);
            }
            catch
            {
                await UnitOfWork.RollbackTransactionAsync();
                throw;
            }
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

    public virtual async Task DeleteAsync(TKey id, CancellationToken cancellationToken = default)
    {
        try
        {
            await CheckPermissionAsync(DeletePermissionName, cancellationToken);
            await UnitOfWork.BeginTransactionAsync();
            try
            {
                await Repository.DeleteAsync(id, cancellationToken);
                await UnitOfWork.CommitTransactionAsync();
            }
            catch
            {
                await UnitOfWork.RollbackTransactionAsync();
                throw;
            }
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
        return await Repository.ExistsAsync(id, cancellationToken);
    }

    public virtual async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return (int)await Repository.GetCountAsync(cancellationToken);
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