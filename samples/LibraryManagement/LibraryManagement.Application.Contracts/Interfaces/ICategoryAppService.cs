using LibraryManagement.Application.Contracts.DTOs;
using System;
using CrestCreates.Application.Contracts.Interfaces;
using LibraryManagement.Domain.Entities;

namespace LibraryManagement.Application.Contracts.Interfaces;

public interface ICategoryAppService : ICrestAppServiceBase<Category, Guid, CategoryDto, CreateCategoryDto, UpdateCategoryDto>
{
}
