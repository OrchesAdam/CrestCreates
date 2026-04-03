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
    private readonly IMapper _mapper;

    public BookAppService(
        IBookRepository bookRepository,
        IMapper mapper)
    {
        _bookRepository = bookRepository;
        _mapper = mapper;
    }

    public async Task<BookDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var book = await _bookRepository.GetByIdAsync(id, cancellationToken);
        if (book == null)
            throw new Exception($"Book with id {id} not found");

        return _mapper.Map<BookDto>(book);
    }

    public async Task<BookDto?> GetByIsbnAsync(string isbn, CancellationToken cancellationToken = default)
    {
        var book = await _bookRepository.GetByIsbnAsync(isbn, cancellationToken);
        return book != null ? _mapper.Map<BookDto>(book) : null;
    }

    public async Task<PagedResult<BookDto>> SearchAsync(BookSearchRequest request, CancellationToken cancellationToken = default)
    {
        // 简化实现，使用仓库的 Search 方法
        var books = await _bookRepository.SearchAsync(request.Keyword, cancellationToken);
        
        // 过滤结果
        if (request.CategoryId.HasValue)
        {
            books = books.Where(b => b.CategoryId == request.CategoryId.Value).ToList();
        }
        
        if (!string.IsNullOrWhiteSpace(request.Author))
        {
            books = books.Where(b => b.Author.Contains(request.Author, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        
        if (request.Status.HasValue)
        {
            books = books.Where(b => b.Status == request.Status.Value).ToList();
        }
        
        // 排序
        books = books.OrderBy(b => b.Title).ToList();
        
        // 分页
        var totalCount = books.Count;
        var pagedBooks = books.Skip(request.PageIndex * request.PageSize).Take(request.PageSize).ToList();
        
        // 映射到 DTO
        var dtos = _mapper.Map<List<BookDto>>(pagedBooks);
        return new PagedResult<BookDto>(dtos, totalCount, request.PageIndex, request.PageSize);
    }

    public async Task<BookDto> CreateAsync(CreateBookDto input, CancellationToken cancellationToken = default)
    {
        var book = _mapper.Map<Book>(input);
        await _bookRepository.AddAsync(book, cancellationToken);
        return _mapper.Map<BookDto>(book);
    }

    public async Task<BookDto> UpdateAsync(Guid id, UpdateBookDto input, CancellationToken cancellationToken = default)
    {
        var book = await _bookRepository.GetByIdAsync(id, cancellationToken);
        if (book == null)
            throw new Exception($"Book with id {id} not found");
        
        _mapper.Map(input, book);
        await _bookRepository.UpdateAsync(book, cancellationToken);
        return _mapper.Map<BookDto>(book);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var book = await _bookRepository.GetByIdAsync(id, cancellationToken);
        if (book == null)
            throw new Exception($"Book with id {id} not found");
        
        await _bookRepository.DeleteAsync(book, cancellationToken);
    }
}
