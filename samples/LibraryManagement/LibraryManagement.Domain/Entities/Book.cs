using System;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.Enums;
using CrestCreates.Domain.Shared.ObjectMapping;
using LibraryManagement.Application.Contracts.DTOs;
using LibraryManagement.Domain.Shared.Constants;
using LibraryManagement.Domain.Shared.Enums;

namespace LibraryManagement.Domain.Entities;

[Entity]
public class Book : AuditedEntity<Guid>
{
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string ISBN { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? PublishDate { get; set; }
    public string? Publisher { get; set; }
    public BookStatus Status { get; set; }
    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public int TotalCopies { get; set; }
    public int AvailableCopies { get; set; }
    public string? Location { get; set; }

    protected Book() { }

    public Book(
        Guid id,
        string title,
        string author,
        string isbn,
        Guid categoryId,
        int totalCopies,
        string? description = null,
        DateTime? publishDate = null,
        string? publisher = null,
        string? location = null)
    {
        Id = id;
        SetTitle(title);
        SetAuthor(author);
        SetISBN(isbn);
        CategoryId = categoryId;
        TotalCopies = totalCopies;
        AvailableCopies = totalCopies;
        Status = BookStatus.Available;
        Description = description;
        PublishDate = publishDate;
        Publisher = publisher;
        Location = location;
    }

    public void SetTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title cannot be empty", nameof(title));
        if (title.Length > LibraryConstants.MaxBookTitleLength)
            throw new ArgumentException($"Title cannot exceed {LibraryConstants.MaxBookTitleLength} characters", nameof(title));
        
        Title = title;
    }

    public void SetAuthor(string author)
    {
        if (string.IsNullOrWhiteSpace(author))
            throw new ArgumentException("Author cannot be empty", nameof(author));
        if (author.Length > LibraryConstants.MaxAuthorNameLength)
            throw new ArgumentException($"Author cannot exceed {LibraryConstants.MaxAuthorNameLength} characters", nameof(author));
        
        Author = author;
    }

    public void SetISBN(string isbn)
    {
        if (string.IsNullOrWhiteSpace(isbn))
            throw new ArgumentException("ISBN cannot be empty", nameof(isbn));
        if (isbn.Length > LibraryConstants.MaxIsbnLength)
            throw new ArgumentException($"ISBN cannot exceed {LibraryConstants.MaxIsbnLength} characters", nameof(isbn));
        
        ISBN = isbn;
    }

    public void UpdateAvailableCopies(int change)
    {
        var newAvailable = AvailableCopies + change;
        if (newAvailable < 0)
            throw new InvalidOperationException("Available copies cannot be negative");
        if (newAvailable > TotalCopies)
            throw new InvalidOperationException("Available copies cannot exceed total copies");
        
        AvailableCopies = newAvailable;
        UpdateStatus();
    }

    public void SetStatus(BookStatus status)
    {
        Status = status;
    }

    public void SetDescription(string? description)
    {
        Description = description;
    }

    public void SetParent(Guid categoryId)
    {
        CategoryId = categoryId;
    }

    private void UpdateStatus()
    {
        if (AvailableCopies == 0)
            Status = BookStatus.Borrowed;
        else if (Status == BookStatus.Borrowed && AvailableCopies > 0)
            Status = BookStatus.Available;
    }

    public bool CanBorrow()
    {
        return Status == BookStatus.Available && AvailableCopies > 0;
    }
}
