using System;
using LibraryManagement.Domain.Shared.Enums;

namespace LibraryManagement.Application.Contracts.DTOs;

public class BookDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string ISBN { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? PublishDate { get; set; }
    public string? Publisher { get; set; }
    public BookStatus Status { get; set; }
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public int TotalCopies { get; set; }
    public int AvailableCopies { get; set; }
    public string? Location { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
}

public class CreateBookDto
{
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string ISBN { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? PublishDate { get; set; }
    public string? Publisher { get; set; }
    public Guid CategoryId { get; set; }
    public int TotalCopies { get; set; }
    public string? Location { get; set; }
}

public class UpdateBookDto
{
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? PublishDate { get; set; }
    public string? Publisher { get; set; }
    public Guid CategoryId { get; set; }
    public int TotalCopies { get; set; }
    public string? Location { get; set; }
}

public class BookSearchRequest
{
    public string? Keyword { get; set; }
    public Guid? CategoryId { get; set; }
    public string? Author { get; set; }
    public BookStatus? Status { get; set; }
    public int PageIndex { get; set; } = 0;
    public int PageSize { get; set; } = 10;
}
