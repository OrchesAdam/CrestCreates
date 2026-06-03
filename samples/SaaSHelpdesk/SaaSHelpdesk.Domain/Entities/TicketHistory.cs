using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using SaaSHelpdesk.Domain.Shared.Enums;

namespace SaaSHelpdesk.Domain.Entities;

[Entity]
public class TicketHistory : AuditedEntity<Guid>
{
    public Guid TicketId { get; private set; }
    public HistoryChangeType ChangeType { get; private set; }
    public string? FieldName { get; private set; }
    public string? OldValue { get; private set; }
    public string? NewValue { get; private set; }
    public Guid? ChangedById { get; private set; }
    public string? ChangedByName { get; private set; }
    public string? Notes { get; private set; }

    // Navigation
    public virtual Ticket? Ticket { get; private set; }

    protected TicketHistory() { }

    public TicketHistory(Guid id, Guid ticketId, HistoryChangeType changeType, Guid? changedById = null, string? changedByName = null)
    {
        Id = id;
        TicketId = ticketId;
        ChangeType = changeType;
        ChangedById = changedById;
        ChangedByName = changedByName;
    }

    public void SetFieldChange(string fieldName, string? oldValue, string? newValue)
    {
        FieldName = fieldName;
        OldValue = oldValue;
        NewValue = newValue;
    }

    public void SetNotes(string? notes)
    {
        Notes = notes;
    }
}
