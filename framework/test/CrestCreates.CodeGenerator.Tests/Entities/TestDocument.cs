using System;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.CodeGenerator.Tests.Entities;

[Entity(CustomPermissions = new[] { "Approve", "Reject", "Review", "Publish" })]
public class TestDocument : AuditedEntity<Guid>
{
    public string Title { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public DocumentStatus Status { get; private set; }
    public string? ReviewComments { get; private set; }
    public Guid? ReviewerId { get; private set; }

    protected TestDocument() { }

    public TestDocument(Guid id, string title, string content)
    {
        Id = id;
        SetTitle(title);
        SetContent(content);
        Status = DocumentStatus.Draft;
    }

    public void SetTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title cannot be empty", nameof(title));
        Title = title.Trim();
    }

    public void SetContent(string content)
    {
        Content = content ?? string.Empty;
    }

    public void SubmitForReview()
    {
        if (Status != DocumentStatus.Draft)
            throw new InvalidOperationException("Only draft documents can be submitted for review");
        Status = DocumentStatus.PendingReview;
    }

    public void Approve(Guid reviewerId, string? comments = null)
    {
        if (Status != DocumentStatus.PendingReview)
            throw new InvalidOperationException("Only documents pending review can be approved");
        Status = DocumentStatus.Approved;
        ReviewerId = reviewerId;
        ReviewComments = comments;
    }

    public void Reject(Guid reviewerId, string reason)
    {
        if (Status != DocumentStatus.PendingReview)
            throw new InvalidOperationException("Only documents pending review can be rejected");
        Status = DocumentStatus.Rejected;
        ReviewerId = reviewerId;
        ReviewComments = reason;
    }

    public void Publish()
    {
        if (Status != DocumentStatus.Approved)
            throw new InvalidOperationException("Only approved documents can be published");
        Status = DocumentStatus.Published;
    }

    public void Archive()
    {
        Status = DocumentStatus.Archived;
    }
}

public enum DocumentStatus
{
    Draft = 0,
    PendingReview = 1,
    Approved = 2,
    Rejected = 3,
    Published = 4,
    Archived = 5
}
