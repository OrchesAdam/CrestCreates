using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Application.Services;
using CrestCreates.Authorization;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.DataFilter;
using SaaSHelpdesk.Application.Contracts.DTOs;
using SaaSHelpdesk.Application.Contracts.Interfaces;
using SaaSHelpdesk.Domain.Entities;

namespace SaaSHelpdesk.Application.Services;

[CrestService]
public class CategoryAppService : CrestAppServiceBase<Category, Guid, CategoryDto, CreateCategoryDto, UpdateCategoryDto>, ICategoryAppService
{
    public CategoryAppService(
        ICrestRepositoryBase<Category, Guid> repository,
        IServiceProvider serviceProvider,
        ICurrentUser currentUser,
        IDataPermissionFilter dataPermissionFilter,
        IPermissionChecker permissionChecker)
        : base(repository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
    }

    protected override Category MapToEntity(CreateCategoryDto dto)
    {
        return new Category(Guid.NewGuid(), dto.Name, dto.SortOrder, dto.ParentId);
    }

    protected override CategoryDto MapToDto(Category entity)
    {
        return new CategoryDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            ParentId = entity.ParentId,
            SortOrder = entity.SortOrder,
            IsActive = entity.IsActive,
        };
    }

    protected override void MapToEntity(UpdateCategoryDto dto, Category entity)
    {
        entity.SetName(dto.Name);
        entity.SetDescription(dto.Description);
        entity.Reorder(dto.SortOrder);
    }

    public async Task<List<CategoryDto>> GetTreeAsync()
    {
        var categories = await Repository.GetListAsync();
        var dict = categories.ToDictionary(c => c.Id);
        var roots = categories.Where(c => c.ParentId == null).OrderBy(c => c.SortOrder).ToList();
        return roots.Select(r => BuildTree(r, dict)).ToList();
    }

    public async Task<List<CategoryDto>> GetRootsAsync()
    {
        var categories = await Repository.GetListAsync();
        return categories.Where(c => c.ParentId == null).OrderBy(c => c.SortOrder).Select(MapToDto).ToList();
    }

    public async Task<List<CategoryDto>> GetChildrenAsync(Guid parentId)
    {
        var categories = await Repository.GetListAsync();
        return categories.Where(c => c.ParentId == parentId).OrderBy(c => c.SortOrder).Select(MapToDto).ToList();
    }

    public async Task<CategoryDto> MoveAsync(Guid id, Guid? newParentId)
    {
        var category = await Repository.GetAsync(id);
        if (category == null)
            throw new KeyNotFoundException($"Category {id} not found");
        category.MoveTo(newParentId);
        await Repository.UpdateAsync(category);
        return MapToDto(category);
    }

    public async Task ReorderAsync(Guid id, int sortOrder)
    {
        var category = await Repository.GetAsync(id);
        if (category == null)
            throw new KeyNotFoundException($"Category {id} not found");
        category.Reorder(sortOrder);
        await Repository.UpdateAsync(category);
    }

    private CategoryDto BuildTree(Category node, Dictionary<Guid, Category> allCategories)
    {
        var dto = MapToDto(node);
        return dto;
    }
}
