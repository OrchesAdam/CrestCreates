namespace SaaSHelpdesk.Application.Contracts.DTOs;

/// <summary>
/// Partial extension for the generated CategoryDto.
/// Adds tree-structure properties that are not auto-generated from entity navigation properties.
/// </summary>
public partial class CategoryDto
{
    /// <summary>
    /// Child categories in tree structure (populated by GetTreeAsync).
    /// </summary>
    public List<CategoryDto>? Children { get; set; }

    /// <summary>
    /// Parent category in tree structure (populated by GetTreeAsync).
    /// </summary>
    public CategoryDto? Parent { get; set; }
}
