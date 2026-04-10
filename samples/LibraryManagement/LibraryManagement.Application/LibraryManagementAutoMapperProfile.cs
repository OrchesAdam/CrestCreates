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
        
        // UpdateBookDto to Book mapping
        CreateMap<LibraryManagement.Application.Contracts.DTOs.UpdateBookDto, LibraryManagement.Domain.Entities.Book>()
            .ForMember(dest => dest.Title, opt => opt.Ignore())
            .ForMember(dest => dest.Author, opt => opt.Ignore())
            .ForMember(dest => dest.ISBN, opt => opt.Ignore())
            .ForMember(dest => dest.Description, opt => opt.Ignore())
            .ForMember(dest => dest.Status, opt => opt.Ignore())
            .ForMember(dest => dest.CategoryId, opt => opt.Ignore())
            .AfterMap((src, dest) => {
                dest.SetTitle(src.Title);
                dest.SetAuthor(src.Author);
                dest.SetISBN(src.ISBN);
                dest.SetParent(src.CategoryId);
                dest.SetStatus((LibraryManagement.Domain.Shared.Enums.BookStatus)src.Status);
                dest.SetDescription(src.Description);
            });


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
