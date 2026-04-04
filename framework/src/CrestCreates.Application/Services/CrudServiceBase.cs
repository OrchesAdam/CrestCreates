using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Domain.Repositories;

namespace CrestCreates.Application.Services
{
    /// <summary>
    /// CRUD 服务泛型基类
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    /// <typeparam name="TKey">主键类型</typeparam>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <typeparam name="TCreateDto">创建 DTO 类型</typeparam>
    /// <typeparam name="TUpdateDto">更新 DTO 类型</typeparam>
    public abstract class CrudServiceBase<TEntity, TKey, TDto, TCreateDto, TUpdateDto>
        : ICrudService<TEntity, TKey, TDto, TCreateDto, TUpdateDto>
        where TEntity : class
        where TKey : IEquatable<TKey>
    {
        protected readonly IRepository<TEntity, TKey> Repository;
        protected readonly IMapper Mapper;

        protected CrudServiceBase(IRepository<TEntity, TKey> repository, IMapper mapper)
        {
            Repository = repository ?? throw new ArgumentNullException(nameof(repository));
            Mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        /// <summary>
        /// 创建实体
        /// </summary>
        public virtual async Task<TDto> CreateAsync(TCreateDto input)
        {
            var entity = MapToEntity(input);
            var createdEntity = await Repository.AddAsync(entity);
            return MapToDto(createdEntity);
        }

        /// <summary>
        /// 根据 ID 获取实体
        /// </summary>
        public virtual async Task<TDto?> GetByIdAsync(TKey id)
        {
            var entity = await Repository.GetByIdAsync(id);
            return entity == null ? default : MapToDto(entity);
        }

        /// <summary>
        /// 获取分页列表
        /// </summary>
        public virtual async Task<Contracts.DTOs.Common.PagedResult<TDto>> GetListAsync(PagedRequestDto request)
        {
            var entities = await Repository.GetAllAsync();
            var totalCount = entities.Count;

            var pagedEntities = entities
                .Skip(request.GetSkipCount())
                .Take(request.PageSize)
                .ToList();

            var dtos = Mapper.Map<List<TDto>>(pagedEntities);

            return new Contracts.DTOs.Common.PagedResult<TDto>(
                dtos,
                totalCount,
                request.PageIndex,
                request.PageSize
            );
        }

        /// <summary>
        /// 更新实体
        /// </summary>
        public virtual async Task<TDto> UpdateAsync(TKey id, TUpdateDto input)
        {
            var entity = await Repository.GetByIdAsync(id);
            if (entity == null)
            {
                throw new KeyNotFoundException($"实体不存在: {id}");
            }

            MapToEntity(input, entity);
            var updatedEntity = await Repository.UpdateAsync(entity);
            return MapToDto(updatedEntity);
        }

        /// <summary>
        /// 删除实体
        /// </summary>
        public virtual async Task DeleteAsync(TKey id)
        {
            var entity = await Repository.GetByIdAsync(id);
            if (entity == null)
            {
                throw new KeyNotFoundException($"实体不存在: {id}");
            }

            await Repository.DeleteAsync(entity);
        }

        /// <summary>
        /// 将创建 DTO 映射为实体
        /// </summary>
        protected virtual TEntity MapToEntity(TCreateDto dto)
        {
            return Mapper.Map<TEntity>(dto);
        }

        /// <summary>
        /// 将更新 DTO 映射到现有实体
        /// </summary>
        protected virtual void MapToEntity(TUpdateDto dto, TEntity entity)
        {
            Mapper.Map(dto, entity);
        }

        /// <summary>
        /// 将实体映射为 DTO
        /// </summary>
        protected virtual TDto MapToDto(TEntity entity)
        {
            return Mapper.Map<TDto>(entity);
        }
    }
}
