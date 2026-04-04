using AutoMapper;
using LibraryManagement.Application.Contracts.DTOs;
using LibraryManagement.Application.Contracts.Interfaces;
using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Repositories;
using CrestCreates.Application.Services;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.Infrastructure.Authorization;

namespace LibraryManagement.Application.Services;

[CrestService]
public class CategoryAppService :CrestAppServiceBase<Category, Guid, CategoryDto, CreateCategoryDto, UpdateCategoryDto>, ICategoryAppService
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly IMapper _mapper;


    public CategoryAppService(ICrestRepositoryBase<Category, Guid> repository, IMapper mapper, IUnitOfWork unitOfWork, ICurrentUser currentUser, IDataPermissionFilter dataPermissionFilter, IPermissionChecker permissionChecker, ICategoryRepository categoryRepository) : base(repository, mapper, unitOfWork, currentUser, dataPermissionFilter, permissionChecker)
    {
        _categoryRepository = categoryRepository;
        _mapper = mapper;
    }

    public async Task<CategoryDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await _categoryRepository.GetAsync(id);
        if (category == null)
            throw new Exception($"Category with id {id} not found");

        return await MapToDtoAsync(category);
    }

    public async Task<IReadOnlyList<CategoryDto>> GetRootCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var categories = await _categoryRepository.GetRootCategoriesAsync(cancellationToken);
        var dtos = new List<CategoryDto>();
        foreach (var category in categories)
        {
            dtos.Add(await MapToDtoAsync(category));
        }
        return dtos;
    }

    public async Task<IReadOnlyList<CategoryDto>> GetChildrenAsync(Guid parentId, CancellationToken cancellationToken = default)
    {
        var categories = await _categoryRepository.GetChildrenAsync(parentId, cancellationToken);
        var dtos = new List<CategoryDto>();
        foreach (var category in categories)
        {
            dtos.Add(await MapToDtoAsync(category));
        }
        return dtos;
    }


    private async Task<CategoryDto> MapToDtoAsync(Category category)
    {
        var dto = _mapper.Map<CategoryDto>(category);
        dto.BookCount = category.Books?.Count ?? 0;
        
        if (category.ParentId.HasValue)
        {
            var parent = await _categoryRepository.GetAsync(category.ParentId.Value);
            if (parent != null)
            {
                dto.ParentName = parent.Name;
            }
        }
        
        return dto;
    }
}
