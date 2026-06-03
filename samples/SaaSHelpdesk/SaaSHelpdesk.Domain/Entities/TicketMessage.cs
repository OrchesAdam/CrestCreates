using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using SaaSHelpdesk.Domain.Shared.Enums;

namespace SaaSHelpdesk.Domain.Entities;

[Entity]
public class TicketMessage : AuditedEntity<Guid>
{
    public Guid TicketId { get; private set; }
    public string Content { get; private set; }
    public MessageSenderType SenderType { get; private set; }
    public Guid? SenderId { get; private set; }
    public string? SenderName { get; private set; }
    public bool IsInternal { get; private set; }
    public bool IsSystem { get; private set; }

    // Navigation
    public virtual Ticket? Ticket { get; private set; }
    public virtual ICollection<TicketAttachment> Attachments { get; private set; } = new HashSet<TicketAttachment>();

    protected TicketMessage() { }

    public TicketMessage(Guid id, Guid ticketId, string content, MessageSenderType senderType, Guid? senderId, string? senderName, bool isInternal = false, bool isSystem = false)
    {
        Id = id;
        TicketId = ticketId;
        SetContent(content);
        SenderType = senderType;
        SenderId = senderId;
        SenderName = senderName;
        IsInternal = isInternal;
        IsSystem = isSystem;
    }

    public void SetContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content cannot be empty", nameof(content));
        Content = content;
    }
}
