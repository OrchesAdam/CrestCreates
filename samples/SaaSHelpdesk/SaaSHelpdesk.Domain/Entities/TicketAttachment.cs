using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace SaaSHelpdesk.Domain.Entities;

[Entity]
public class TicketAttachment : AuditedEntity<Guid>
{
    public Guid? TicketMessageId { get; private set; }
    public Guid? TicketId { get; private set; }
    public string FileName { get; private set; }
    public string ContentType { get; private set; }
    public long FileSize { get; private set; }
    public string FileHash { get; private set; }
    public string StoragePath { get; private set; }

    // Navigation
    public virtual TicketMessage? TicketMessage { get; private set; }
    public virtual Ticket? Ticket { get; private set; }

    protected TicketAttachment() { }

    public TicketAttachment(Guid id, string fileName, string contentType, long fileSize, string fileHash, string storagePath, Guid? ticketId = null, Guid? ticketMessageId = null)
    {
        Id = id;
        FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
        FileSize = fileSize;
        FileHash = fileHash ?? throw new ArgumentNullException(nameof(fileHash));
        StoragePath = storagePath ?? throw new ArgumentNullException(nameof(storagePath));
        TicketId = ticketId;
        TicketMessageId = ticketMessageId;
    }
}
