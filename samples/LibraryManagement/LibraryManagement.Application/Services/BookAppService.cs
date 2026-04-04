using AutoMapper;
using LibraryManagement.Application.Contracts.DTOs;
using LibraryManagement.Application.Contracts.Interfaces;
using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Repositories;
using CrestCreates.Application.Services;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Attributes;

namespace LibraryManagement.Application.Services;

[CrestService]
public class BookAppService : CrudServiceBase<Book,Guid,BookDto, CreateBookDto, UpdateBookDto>, IBookAppService
{
    private readonly IBookRepository _repository;
    private readonly IMapper _mapper;


    public BookAppService(IRepository<Book, Guid> repository, IMapper mapper, IBookRepository repository2) : base(repository, mapper)
    {
        _repository = repository2;
        _mapper = mapper;
    }

    public async Task<BookDto> CreateAsync(CreateBookDto input, CancellationToken cancellationToken = default)
    {
        var book = _mapper.Map<Book>(input);
        var created = await _repository.AddAsync(book, cancellationToken);
        return _mapper.Map<BookDto>(created);
    }

    public async Task<BookDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var book = await _repository.GetByIdAsync(id, cancellationToken);
        return book == null ? null : _mapper.Map<BookDto>(book);
    }

    public async Task<BookDto> UpdateAsync(Guid id, UpdateBookDto input, CancellationToken cancellationToken = default)
    {
        var book = await _repository.GetByIdAsync(id, cancellationToken);
        if (book == null)
            throw new InvalidOperationException($"Book with id {id} not found");

        _mapper.Map(input, book);
        var updated = await _repository.UpdateAsync(book, cancellationToken);
        return _mapper.Map<BookDto>(updated);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var book = await _repository.GetByIdAsync(id, cancellationToken);
        if (book == null)
            throw new InvalidOperationException($"Book with id {id} not found");
        await _repository.DeleteAsync(book, cancellationToken);
    }

    public async Task<BookDto?> GetByIsbnAsync(string isbn, CancellationToken cancellationToken = default)
    {
        var book = await _repository.GetByIsbnAsync(isbn, cancellationToken);
        return book == null ? null : _mapper.Map<BookDto>(book);
    }
}