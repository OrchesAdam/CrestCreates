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
using CrestCreates.Domain.Shared.DataFilter;

namespace LibraryManagement.Application.Services;

[CrestService]
public class BookAppService : CrestAppServiceBase<Book,Guid, BookDto, CreateBookDto, UpdateBookDto>, IBookAppService
{
    private readonly IBookRepository _repository;
    private readonly IMapper _mapper;


    public BookAppService(ICrestRepositoryBase<Book, Guid> repository, IMapper mapper, IServiceProvider serviceProvider, ICurrentUser currentUser, IDataPermissionFilter dataPermissionFilter, IPermissionChecker permissionChecker, IBookRepository repository2) : base(repository, mapper, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
        _repository = repository2;
        _mapper = mapper;
    }

    public async Task<BookDto?> GetByIsbnAsync(string isbn, CancellationToken cancellationToken = default)
    {
        var book = await _repository.GetByIsbnAsync(isbn, cancellationToken);
        return book == null ? null : _mapper.Map<BookDto>(book);
    }

    protected override void MapToEntity(UpdateBookDto dto, Book entity)
    {
        entity.SetTitle(dto.Title);
        entity.SetAuthor(dto.Author);
        entity.SetISBN(dto.ISBN);
        entity.SetParent(dto.CategoryId);
        entity.SetStatus((LibraryManagement.Domain.Shared.Enums.BookStatus)dto.Status);
        entity.SetDescription(dto.Description);
        
        // 注意：TotalCopies 和 AvailableCopies 是只读属性，需要通过其他方法修改
        // 这里我们只更新可以直接设置的属性
        // entity.TotalCopies = dto.TotalCopies; // 只读
        // entity.AvailableCopies = dto.AvailableCopies; // 只读
        // entity.Location = dto.Location; // 只读
        // entity.Publisher = dto.Publisher; // 只读
        // entity.PublishDate = dto.PublishDate; // 只读
    }
}
