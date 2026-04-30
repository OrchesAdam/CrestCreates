using LibraryManagement.Application.Contracts.DTOs;
using LibraryManagement.Domain.Converters;
using LibraryManagement.Domain.Entities;

namespace LibraryManagement.Domain.Entities.Extensions;

public static partial class BookMappingExtensions
{
    static partial void AfterToDto(Book source, BookDto destination)
    {
        destination.CategoryName = source.Category?.Name;
        destination.StatusDisplay = BookStatusToStringConverter.Convert(source.Status);
        destination.DisplayTitle = $"{source.Title} ({source.Author})";
    }
}
