using System;
using System.Collections.Generic;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.Enums;
using LibraryManagement.Domain.Shared.Constants;

namespace LibraryManagement.Domain.Entities;

[Entity]
public class Category : AuditedEntity<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentId { get; set; }
    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = new List<Category>();
    public ICollection<Book> Books { get; set; } = new List<Book>();

    protected Category() { }

    public Category(Guid id, string name, string? description = null, Guid? parentId = null)
    {
        Id = id;
        SetName(name);
        Description = description;
        ParentId = parentId;
    }

    public void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty", nameof(name));
        if (name.Length > LibraryConstants.MaxCategoryNameLength)
            throw new ArgumentException($"Name cannot exceed {LibraryConstants.MaxCategoryNameLength} characters", nameof(name));
        
        Name = name;
    }

    public void SetDescription(string? description)
    {
        Description = description;
    }

    public void SetParent(Guid? parentId)
    {
        ParentId = parentId;
    }
}
