using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Common;

namespace CrestCreates.Application.Contracts.Interfaces;

/// <summary>
/// 标准 CRUD 应用服务契约，用于框架级泛型控制器适配生成的实体服务。
/// </summary>
public interface ICrudAppService<TKey, TDto, in TCreateDto, in TUpdateDto, in TListRequestDto>
    where TKey : IEquatable<TKey>
{
    Task<TDto> CreateAsync(TCreateDto input, CancellationToken cancellationToken = default);

    Task<TDto?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);

    Task<PagedResultDto<TDto>> GetListAsync(TListRequestDto input, CancellationToken cancellationToken = default);

    Task<TDto> UpdateAsync(TKey id, TUpdateDto input, CancellationToken cancellationToken = default);

    Task DeleteAsync(TKey id, string? expectedStamp = null, CancellationToken cancellationToken = default);
}
