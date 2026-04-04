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

namespace CrestCreates.Application.Services;

public abstract class CrestAppServiceBase<TEntity, TDto, TCreateDto, TUpdateDto, TKey> : ICrestAppServiceBase<TEntity, TDto, TCreateDto, TUpdateDto, TKey>
    where TEntity : class
    where TKey : IEquatable<TKey>
{
    protected readonly ICrestRepositoryBase<TEntity, TKey> Repository;
    protected readonly IMapper Mapper;

    protected CrestAppServiceBase(ICrestRepositoryBase<TEntity, TKey> repository, IMapper mapper)
    {
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        Mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    public virtual async Task<TDto> CreateAsync(TCreateDto input, CancellationToken cancellationToken = default)
    {
        var entity = MapToEntity(input);
        var createdEntity = await Repository.InsertAsync(entity, cancellationToken);
        return MapToDto(createdEntity);
    }

    public virtual async Task<TDto?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var entity = await Repository.GetAsync(id, cancellationToken);
        return entity == null ? default : MapToDto(entity);
    }

    public virtual async Task<IReadOnlyList<TDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await Repository.GetListAsync(cancellationToken);
        return Mapper.Map<List<TDto>>(entities);
    }

    public virtual async Task<Contracts.DTOs.Common.PagedResult<TDto>> GetListAsync(PagedRequestDto request, CancellationToken cancellationToken = default)
    {
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
        var entity = await Repository.GetAsync(id, cancellationToken);
        if (entity == null)
        {
            throw new KeyNotFoundException($"实体不存在: {id}");
        }

        MapToEntity(input, entity);
        var updatedEntity = await Repository.UpdateAsync(entity, cancellationToken);
        return MapToDto(updatedEntity);
    }

    public virtual async Task DeleteAsync(TKey id, CancellationToken cancellationToken = default)
    {
        await Repository.DeleteAsync(id, cancellationToken);
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