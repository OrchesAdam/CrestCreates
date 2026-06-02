using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.DomainEvents;
using SaaSHelpdesk.Domain.Shared.Enums;

namespace SaaSHelpdesk.Domain.Entities;

[Entity]
public class Ticket : AuditedEntity<Guid>, IHasDomainEvents
{
    public string Title { get; private set; }
    public string Description { get; private set; }
    public TicketStatus Status { get; private set; }
    public TicketPriority Priority { get; private set; }
    public TicketType Type { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid? AssigneeId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public DateTime? DueDate { get; private set; }
    public DateTime? ResolvedAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public bool IsEscalated { get; private set; }

    // Navigation properties
    public virtual Customer? Customer { get; private set; }
    public virtual Category? Category { get; private set; }
    public virtual ICollection<TicketMessage> Messages { get; private set; } = new HashSet<TicketMessage>();
    public virtual ICollection<TicketHistory> History { get; private set; } = new HashSet<TicketHistory>();

    protected Ticket() { }

    public Ticket(Guid id, string title, string description, TicketPriority priority, TicketType type, Guid customerId)
    {
        Id = id;
        SetTitle(title);
        SetDescription(description);
        Priority = priority;
        Type = type;
        CustomerId = customerId;
        Status = TicketStatus.Open;
    }

    public void SetTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
            throw new ArgumentException("Title must be between 1 and 200 characters", nameof(title));
        Title = title;
    }

    public void SetDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description cannot be empty", nameof(description));
        Description = description;
    }

    public void SetPriority(TicketPriority priority)
    {
        Priority = priority;
    }

    public void AssignTo(Guid agentId)
    {
        AssigneeId = agentId;
        Status = TicketStatus.InProgress;
    }

    public void Resolve()
    {
        Status = TicketStatus.Resolved;
        ResolvedAt = DateTime.UtcNow;
    }

    public void Close()
    {
        Status = TicketStatus.Closed;
        ClosedAt = DateTime.UtcNow;
    }

    public void Escalate()
    {
        IsEscalated = true;
    }

    public bool CanTransition()
    {
        return Status != TicketStatus.Closed;
    }
}
