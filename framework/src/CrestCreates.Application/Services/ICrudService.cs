using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Common;

namespace CrestCreates.Application.Services
{
    /// <summary>
    /// CRUD 服务泛型接口
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    /// <typeparam name="TKey">主键类型</typeparam>
    /// <typeparam name="TDto">DTO 类型</typeparam>
    /// <typeparam name="TCreateDto">创建 DTO 类型</typeparam>
    /// <typeparam name="TUpdateDto">更新 DTO 类型</typeparam>
    [Obsolete("Use generated CRUD service or CrestAppServiceBase for concurrency support.")]
    public interface ICrudService<TEntity, in TKey, TDto, in TCreateDto, in TUpdateDto>
        where TEntity : class
        where TKey : IEquatable<TKey>
    {
        /// <summary>
        /// 创建实体
        /// </summary>
        /// <param name="input">创建 DTO</param>
        /// <returns>创建的 DTO</returns>
        Task<TDto> CreateAsync(TCreateDto input);

        /// <summary>
        /// 根据 ID 获取实体
        /// </summary>
        /// <param name="id">主键</param>
        /// <returns>DTO</returns>
        Task<TDto?> GetByIdAsync(TKey id);

        /// <summary>
        /// 获取分页列表
        /// </summary>
        /// <param name="request">分页请求</param>
        /// <returns>分页结果</returns>
        Task<PagedResultDto<TDto>> GetListAsync(PagedRequestDto request);

        /// <summary>
        /// 更新实体
        /// </summary>
        /// <param name="id">主键</param>
        /// <param name="input">更新 DTO</param>
        /// <returns>更新后的 DTO</returns>
        Task<TDto> UpdateAsync(TKey id, TUpdateDto input);

        /// <summary>
        /// 删除实体
        /// </summary>
        /// <param name="id">主键</param>
        Task DeleteAsync(TKey id);
    }
}
