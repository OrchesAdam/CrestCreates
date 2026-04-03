using System;
using AutoMapper;
using LibraryManagement.Application.Contracts.DTOs;
using LibraryManagement.Domain.Entities;

namespace LibraryManagement.Application;

public class LibraryManagementAutoMapperProfile : Profile
{
    public LibraryManagementAutoMapperProfile()
    {
        // Book mappings
        CreateMap<Book, BookDto>();
        CreateMap<CreateBookDto, Book>()
            .ConstructUsing(dto => new Book(
                Guid.NewGuid(),
                dto.Title,
                dto.Author,
                dto.ISBN,
                dto.CategoryId,
                dto.TotalCopies,
                dto.Description,
                dto.PublishDate,
                dto.Publisher,
                dto.Location));

        // Category mappings
        CreateMap<Category, CategoryDto>();
        CreateMap<CreateCategoryDto, Category>()
            .ConstructUsing(dto => new Category(
                Guid.NewGuid(),
                dto.Name,
                dto.Description,
                dto.ParentId));

        // Member mappings
        CreateMap<Member, MemberDto>();
        CreateMap<CreateMemberDto, Member>()
            .ConstructUsing(dto => new Member(
                Guid.NewGuid(),
                dto.Name,
                dto.Email,
                dto.Type,
                dto.Phone,
                dto.Address,
                dto.ExpiryDate));

        // Loan mappings
        CreateMap<Loan, LoanDto>();
    }
}
