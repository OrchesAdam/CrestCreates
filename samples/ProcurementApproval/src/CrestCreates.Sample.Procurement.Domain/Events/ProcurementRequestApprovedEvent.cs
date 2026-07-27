using CrestCreates.Domain.DomainEvents;

namespace CrestCreates.Sample.Procurement.Domain.Events;

public sealed class ProcurementRequestApprovedEvent : DomainEvent
{
    public Guid RequestId { get; }
    public string ApproverId { get; }
    public string Comment { get; }

    public ProcurementRequestApprovedEvent(Guid requestId, string approverId, string comment)
    {
        RequestId = requestId;
        ApproverId = approverId;
        Comment = comment;
    }
}
