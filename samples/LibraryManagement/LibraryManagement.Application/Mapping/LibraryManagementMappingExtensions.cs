using LibraryManagement.Application.Contracts.DTOs;
using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Shared.Enums;

namespace LibraryManagement.Domain.Entities.Extensions;

public static class LibraryManagementMappingExtensions
{
    public static BookDto ToDto(this Book source)
        => new()
        {
            Id = source.Id,
            Title = source.Title,
            Author = source.Author,
            ISBN = source.ISBN,
            Description = source.Description,
            PublishDate = source.PublishDate,
            Publisher = source.Publisher,
            Status = source.Status,
            CategoryId = source.CategoryId,
            TotalCopies = source.TotalCopies,
            AvailableCopies = source.AvailableCopies,
            Location = source.Location,
            CreationTime = source.CreationTime,
            CreatorId = source.CreatorId,
            LastModificationTime = source.LastModificationTime,
            LastModifierId = source.LastModifierId,
            ConcurrencyStamp = source.ConcurrencyStamp
        };

    public static Book ApplyTo(this CreateBookDto source, Book target)
    {
        target.Title = source.Title;
        target.Author = source.Author;
        target.ISBN = source.ISBN;
        target.Description = source.Description;
        target.PublishDate = source.PublishDate;
        target.Publisher = source.Publisher;
        target.Status = source.Status;
        target.CategoryId = source.CategoryId;
        target.TotalCopies = source.TotalCopies;
        target.AvailableCopies = source.AvailableCopies;
        target.Location = source.Location;
        return target;
    }

    public static Book ApplyTo(this UpdateBookDto source, Book target)
    {
        target.Title = source.Title;
        target.Author = source.Author;
        target.ISBN = source.ISBN;
        target.Description = source.Description;
        target.PublishDate = source.PublishDate;
        target.Publisher = source.Publisher;
        target.Status = source.Status;
        target.CategoryId = source.CategoryId;
        target.TotalCopies = source.TotalCopies;
        target.AvailableCopies = source.AvailableCopies;
        target.Location = source.Location;
        target.ConcurrencyStamp = source.ConcurrencyStamp;
        return target;
    }

    public static BookDto AddDisplayFields(this BookDto destination, Book source)
    {
        destination.CategoryName = source.Category?.Name;
        destination.StatusDisplay = source.Status.GetDisplayName();
        destination.DisplayTitle = $"{source.Title} ({source.Author})";
        return destination;
    }

    public static CategoryDto ToDto(this Category source)
        => new()
        {
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
            ParentId = source.ParentId,
            CreationTime = source.CreationTime,
            CreatorId = source.CreatorId,
            LastModificationTime = source.LastModificationTime,
            LastModifierId = source.LastModifierId,
            ConcurrencyStamp = source.ConcurrencyStamp
        };

    public static Category ApplyTo(this UpdateCategoryDto source, Category target)
    {
        target.Name = source.Name;
        target.Description = source.Description;
        target.ParentId = source.ParentId;
        target.ConcurrencyStamp = source.ConcurrencyStamp;
        return target;
    }

    public static MemberDto ToDto(this Member source)
        => new()
        {
            Id = source.Id,
            Name = source.Name,
            Email = source.Email,
            Phone = source.Phone,
            Address = source.Address,
            Type = source.Type,
            RegistrationDate = source.RegistrationDate,
            ExpiryDate = source.ExpiryDate,
            IsActive = source.IsActive,
            MaxBooksAllowed = source.MaxBooksAllowed,
            OutstandingBalance = source.OutstandingBalance,
            CreationTime = source.CreationTime,
            CreatorId = source.CreatorId,
            LastModificationTime = source.LastModificationTime,
            LastModifierId = source.LastModifierId,
            ConcurrencyStamp = source.ConcurrencyStamp
        };

    public static LoanDto ToDto(this Loan source)
        => new()
        {
            Id = source.Id,
            BookId = source.BookId,
            MemberId = source.MemberId,
            LoanDate = source.LoanDate,
            DueDate = source.DueDate,
            ReturnDate = source.ReturnDate,
            Status = source.Status,
            LateFee = source.LateFee,
            Notes = source.Notes,
            CreationTime = source.CreationTime,
            CreatorId = source.CreatorId,
            LastModificationTime = source.LastModificationTime,
            LastModifierId = source.LastModifierId,
            ConcurrencyStamp = source.ConcurrencyStamp
        };

    private static string GetDisplayName(this BookStatus status)
        => status switch
        {
            BookStatus.Available => "Available",
            BookStatus.Borrowed => "Borrowed",
            BookStatus.Reserved => "Reserved",
            BookStatus.Maintenance => "Maintenance",
            BookStatus.Lost => "Lost",
            _ => status.ToString()
        };
}
