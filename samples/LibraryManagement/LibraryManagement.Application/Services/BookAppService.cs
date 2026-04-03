using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using CrestCreates.Domain.Shared.Attributes;
using LibraryManagement.Application.Contracts.DTOs;
using LibraryManagement.Application.Contracts.Interfaces;
using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Repositories;

namespace LibraryManagement.Application.Services;

[Service(GenerateController = false)]
public class BookAppService : IBookAppService
{
    private readonly IBookRepository _bookRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IMapper _mapper;

    public BookAppService(
        IBookRepository bookRepository,
        ICategoryRepository categoryRepository,
        IMapper mapper)
    {
        _bookRepository = bookRepository;
        _categoryRepository = categoryRepository;
        _mapper = mapper;
    }

    public async Task<BookDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var book = await _bookRepository.GetByIdAsync(id);
        if (book == null)
            throw new Exception($"Book with id {id} not found");

        return await MapToDtoAsync(book);
    }

    public async Task<BookDto?> GetByIsbnAsync(string isbn, CancellationToken cancellationToken = default)
    {
        var book = await _bookRepository.GetByIsbnAsync(isbn, cancellationToken);
        if (book == null)
            return null;

        return await MapToDtoAsync(book);
    }

    public async Task<PagedResult<BookDto>> SearchAsync(BookSearchRequest request, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Book> books;
        int totalCount;

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            books = await _bookRepository.SearchAsync(request.Keyword, cancellationToken);
            totalCount = books.Count;
        }
        else if (request.CategoryId.HasValue)
        {
            books = await _bookRepository.GetByCategoryAsync(request.CategoryId.Value, cancellationToken);
            totalCount = books.Count;
        }
        else if (!string.IsNullOrWhiteSpace(request.Author))
        {
            books = await _bookRepository.GetByAuthorAsync(request.Author, cancellationToken);
            totalCount = books.Count;
        }
        else
        {
            books = await _bookRepository.GetAllAsync();
            totalCount = books.Count;
        }

        if (request.Status.HasValue)
        {
            books = books.Where(b => b.Status == request.Status.Value).ToList();
        }

        var pagedBooks = books
            .Skip(request.PageIndex * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        var dtos = new List<BookDto>();
        foreach (var book in pagedBooks)
        {
            dtos.Add(await MapToDtoAsync(book));
        }

        return new PagedResult<BookDto>(dtos, totalCount, request.PageIndex, request.PageSize);
    }

    public async Task<BookDto> CreateAsync(CreateBookDto input, CancellationToken cancellationToken = default)
    {
        // Validate ISBN uniqueness
        if (!await _bookRepository.IsIsbnUniqueAsync(input.ISBN, cancellationToken: cancellationToken))
            throw new Exception($"ISBN '{input.ISBN}' already exists");

        // Validate category exists
        var category = await _categoryRepository.GetByIdAsync(input.CategoryId);
        if (category == null)
            throw new Exception($"Category with id {input.CategoryId} not found");

        var book = new Book(
            Guid.NewGuid(),
            input.Title,
            input.Author,
            input.ISBN,
            input.CategoryId,
            input.TotalCopies,
            input.Description,
            input.PublishDate,
            input.Publisher,
            input.Location
        );

        await _bookRepository.AddAsync(book);
        return await MapToDtoAsync(book);
    }

    public async Task<BookDto> UpdateAsync(Guid id, UpdateBookDto input, CancellationToken cancellationToken = default)
    {
        var book = await _bookRepository.GetByIdAsync(id);
        if (book == null)
            throw new Exception($"Book with id {id} not found");

        // Validate category exists
        var category = await _categoryRepository.GetByIdAsync(input.CategoryId);
        if (category == null)
            throw new Exception($"Category with id {input.CategoryId} not found");

        book.SetTitle(input.Title);
        book.SetAuthor(input.Author);
        book.SetDescription(input.Description);
        book.SetParent(input.CategoryId); // This should be updated in the entity

        await _bookRepository.UpdateAsync(book);
        return await MapToDtoAsync(book);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var book = await _bookRepository.GetByIdAsync(id);
        if (book != null)
        {
            await _bookRepository.DeleteAsync(book);
        }
    }

    private async Task<BookDto> MapToDtoAsync(Book book)
    {
        var dto = _mapper.Map<BookDto>(book);
        var category = await _categoryRepository.GetByIdAsync(book.CategoryId);
        if (category != null)
        {
            dto.CategoryName = category.Name;
        }
        return dto;
    }
}
