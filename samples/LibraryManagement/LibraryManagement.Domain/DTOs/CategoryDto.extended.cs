namespace LibraryManagement.Application.Contracts.DTOs;

public partial class CategoryDto
{
    public int BookCount { get; set; }
    public string? ParentName { get; set; }
}
