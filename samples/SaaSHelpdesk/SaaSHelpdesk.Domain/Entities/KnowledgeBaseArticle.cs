using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace SaaSHelpdesk.Domain.Entities;

[Entity]
public class KnowledgeBaseArticle : AuditedEntity<Guid>
{
    public string Title { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public Guid? CategoryId { get; private set; }
    public bool IsPublished { get; private set; }
    public DateTime? PublishedAt { get; private set; }
    public int ViewCount { get; private set; }
    public string? Tags { get; private set; }

    // Navigation
    public virtual Category? Category { get; private set; }

    protected KnowledgeBaseArticle() { }

    public KnowledgeBaseArticle(Guid id, string title, string content)
    {
        Id = id;
        SetTitle(title);
        SetContent(content);
    }

    public void SetTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
            throw new ArgumentException("Title must be between 1 and 200 characters", nameof(title));
        Title = title;
    }

    public void SetContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content cannot be empty", nameof(content));
        Content = content;
    }

    public void SetCategory(Guid? categoryId)
    {
        CategoryId = categoryId;
    }

    public void SetTags(string? tags)
    {
        Tags = tags;
    }

    public void Publish()
    {
        IsPublished = true;
        PublishedAt = DateTime.UtcNow;
    }

    public void Unpublish()
    {
        IsPublished = false;
        PublishedAt = null;
    }

    public void IncrementViewCount()
    {
        ViewCount++;
    }
}
