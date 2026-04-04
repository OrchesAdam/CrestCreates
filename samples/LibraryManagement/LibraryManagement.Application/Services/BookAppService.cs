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
public class BookAppService : CrestAppServiceBase<Book,Guid, BookDto, CreateBookDto, UpdateBookDto>, IBookAppService
{
    private readonly IBookRepository _repository;
    private readonly IMapper _mapper;


    /// <inheritdoc />
    public BookAppService(ICrestRepositoryBase<Book, Guid> repository, IMapper mapper, IUnitOfWork unitOfWork, ICurrentUser currentUser, IDataPermissionFilter dataPermissionFilter, IPermissionChecker permissionChecker, IBookRepository repository2) : base(repository, mapper, unitOfWork, currentUser, dataPermissionFilter, permissionChecker)
    {
        _repository = repository2;
        _mapper = mapper;
    }

    public async Task<BookDto?> GetByIsbnAsync(string isbn, CancellationToken cancellationToken = default)
    {
        var book = await _repository.GetByIsbnAsync(isbn, cancellationToken);
        return book == null ? null : _mapper.Map<BookDto>(book);
    }
}