using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Application.Contracts.Query;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface ICrestAppServiceBase<TEntity, in TKey, TDto, in TCreateDto, in TUpdateDto>
    where TEntity : class
    where TKey : IEquatable<TKey>
{
    Task<TDto> CreateAsync(TCreateDto input, CancellationToken cancellationToken = default);
    Task<TDto?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<PagedResultDto<TDto>> GetListAsync(PagedRequestDto request, CancellationToken cancellationToken = default);
    Task<PagedResultDto<TDto>> QueryAsync(QueryRequest<TEntity> request, CancellationToken cancellationToken = default);
    Task<TDto> UpdateAsync(TKey id, TUpdateDto input, CancellationToken cancellationToken = default);
    Task DeleteAsync(TKey id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(TKey id, CancellationToken cancellationToken = default);
    Task<int> CountAsync(CancellationToken cancellationToken = default);
}