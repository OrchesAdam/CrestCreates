using LibraryManagement.Application.Contracts.DTOs;
using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Shared.Enums;

namespace LibraryManagement.Domain.Entities.Extensions;

public static partial class BookMappingExtensions
{
    static partial void AfterToDto(Book source, BookDto destination)
    {
        destination.CategoryName = source.Category?.Name;
        destination.StatusDisplay = source.Status.GetDisplayName();
        destination.DisplayTitle = $"{source.Title} ({source.Author})";
    }
}
