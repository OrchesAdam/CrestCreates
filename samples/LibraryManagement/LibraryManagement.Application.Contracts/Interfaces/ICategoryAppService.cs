using LibraryManagement.Application.Contracts.DTOs;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LibraryManagement.Application.Contracts.Interfaces;

public interface ICategoryAppService
{
    Task<CategoryDto> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CategoryDto>> GetRootCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CategoryDto>> GetChildrenAsync(Guid parentId, CancellationToken cancellationToken = default);
    Task<CategoryDto> CreateAsync(CreateCategoryDto input, CancellationToken cancellationToken = default);
    Task<CategoryDto> UpdateAsync(Guid id, UpdateCategoryDto input, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
