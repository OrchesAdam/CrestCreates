using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace SaaSHelpdesk.Domain.Entities;

[Entity]
public class Category : FullyAuditedEntity<Guid>
{
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public Guid? ParentId { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    // Navigation
    public virtual Category? Parent { get; private set; }
    public virtual ICollection<Category> Children { get; private set; } = new HashSet<Category>();

    protected Category() { }

    public Category(Guid id, string name, int sortOrder = 0, Guid? parentId = null)
    {
        Id = id;
        SetName(name);
        SortOrder = sortOrder;
        ParentId = parentId;
        IsActive = true;
    }

    public void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 50)
            throw new ArgumentException("Name must be between 1 and 50 characters", nameof(name));
        Name = name;
    }

    public void SetDescription(string? description)
    {
        Description = description;
    }

    public void MoveTo(Guid? newParentId)
    {
        // Note: Circular reference check should be done at repository level
        ParentId = newParentId;
    }

    public void Reorder(int sortOrder)
    {
        SortOrder = sortOrder;
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void SoftDelete()
    {
        IsDeleted = true;
    }
}
