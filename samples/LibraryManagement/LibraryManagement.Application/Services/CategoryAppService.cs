using AutoMapper;
using CrestCreates.Domain.Shared.Attributes;
using LibraryManagement.Application.Contracts.DTOs;
using LibraryManagement.Application.Contracts.Interfaces;
using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Repositories;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LibraryManagement.Application.Services;

[Service(GenerateController = false)]
public class CategoryAppService : ICategoryAppService
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly IMapper _mapper;

    public CategoryAppService(
        ICategoryRepository categoryRepository,
        IMapper mapper)
    {
        _categoryRepository = categoryRepository;
        _mapper = mapper;
    }

    public async Task<CategoryDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await _categoryRepository.GetByIdAsync(id);
        if (category == null)
            throw new Exception($"Category with id {id} not found");

        return await MapToDtoAsync(category);
    }

    public async Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var categories = await _categoryRepository.GetAllAsync();
        var dtos = new List<CategoryDto>();
        foreach (var category in categories)
        {
            dtos.Add(await MapToDtoAsync(category));
        }
        return dtos;
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

    public async Task<CategoryDto> CreateAsync(CreateCategoryDto input, CancellationToken cancellationToken = default)
    {
        // Validate parent exists if specified
        if (input.ParentId.HasValue)
        {
            var parent = await _categoryRepository.GetByIdAsync(input.ParentId.Value);
            if (parent == null)
                throw new Exception($"Parent category with id {input.ParentId} not found");
        }

        var category = new Category(
            Guid.NewGuid(),
            input.Name,
            input.Description,
            input.ParentId
        );

        await _categoryRepository.AddAsync(category);
        return await MapToDtoAsync(category);
    }

    public async Task<CategoryDto> UpdateAsync(Guid id, UpdateCategoryDto input, CancellationToken cancellationToken = default)
    {
        var category = await _categoryRepository.GetByIdAsync(id);
        if (category == null)
            throw new Exception($"Category with id {id} not found");

        // Validate parent exists if specified
        if (input.ParentId.HasValue && input.ParentId.Value != id)
        {
            var parent = await _categoryRepository.GetByIdAsync(input.ParentId.Value);
            if (parent == null)
                throw new Exception($"Parent category with id {input.ParentId} not found");
        }

        category.SetName(input.Name);
        category.SetDescription(input.Description);
        category.SetParent(input.ParentId);

        await _categoryRepository.UpdateAsync(category);
        return await MapToDtoAsync(category);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Check if category has children
        if (await _categoryRepository.HasChildrenAsync(id, cancellationToken))
            throw new Exception("Cannot delete category with children");

        var category = await _categoryRepository.GetByIdAsync(id);
        if (category != null)
        {
            await _categoryRepository.DeleteAsync(category);
        }
    }

    private async Task<CategoryDto> MapToDtoAsync(Category category)
    {
        var dto = _mapper.Map<CategoryDto>(category);
        dto.BookCount = category.Books?.Count ?? 0;
        
        if (category.ParentId.HasValue)
        {
            var parent = await _categoryRepository.GetByIdAsync(category.ParentId.Value);
            if (parent != null)
            {
                dto.ParentName = parent.Name;
            }
        }
        
        return dto;
    }
}
