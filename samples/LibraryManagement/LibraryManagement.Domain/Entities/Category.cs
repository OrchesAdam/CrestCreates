using System;
using System.Collections.Generic;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.Enums;
using LibraryManagement.Domain.Shared.Constants;

namespace LibraryManagement.Domain.Entities;

[GenerateRepository(OrmProvider = OrmProvider.EfCore)]
[GenerateCrudService(GenerateDto = true, GenerateController = true, ServiceRoute = "api/categories")]
[GenerateQueryBuilder]
public class Category : AuditedEntity<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid? ParentId { get; private set; }
    public Category? Parent { get; private set; }
    public ICollection<Category> Children { get; private set; } = new List<Category>();
    public ICollection<Book> Books { get; private set; } = new List<Book>();

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
