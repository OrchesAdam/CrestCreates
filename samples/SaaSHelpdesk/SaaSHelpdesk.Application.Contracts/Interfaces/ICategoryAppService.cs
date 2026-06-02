using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Contracts.Interfaces;

public interface ICategoryAppService
{
    Task<List<CategoryDto>> GetTreeAsync();
    Task<List<CategoryDto>> GetRootsAsync();
    Task<List<CategoryDto>> GetChildrenAsync(Guid parentId);
    Task<CategoryDto> MoveAsync(Guid id, Guid? newParentId);
    Task ReorderAsync(Guid id, int sortOrder);
}
