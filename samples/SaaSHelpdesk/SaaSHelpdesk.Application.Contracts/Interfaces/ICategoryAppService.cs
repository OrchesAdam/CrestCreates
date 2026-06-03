using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Contracts.Interfaces;

public interface ICategoryAppService
{
    Task<CategoryDto> CreateAsync(CreateCategoryDto input, CancellationToken cancellationToken = default);
    Task<CategoryDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<CategoryDto> UpdateAsync(Guid id, UpdateCategoryDto input, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default);
    Task<List<CategoryDto>> GetTreeAsync();
    Task<List<CategoryDto>> GetRootsAsync();
    Task<List<CategoryDto>> GetChildrenAsync(Guid parentId);
    Task<CategoryDto> MoveAsync(Guid id, Guid? newParentId);
    Task ReorderAsync(Guid id, int sortOrder);
}
