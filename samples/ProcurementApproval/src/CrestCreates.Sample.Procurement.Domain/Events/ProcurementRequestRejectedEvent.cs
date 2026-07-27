using CrestCreates.Domain.DomainEvents;

namespace CrestCreates.Sample.Procurement.Domain.Events;

public sealed class ProcurementRequestRejectedEvent : DomainEvent
{
    public Guid RequestId { get; }
    public string ApproverId { get; }
    public string Reason { get; }

    public ProcurementRequestRejectedEvent(Guid requestId, string approverId, string reason)
    {
        RequestId = requestId;
        ApproverId = approverId;
        Reason = reason;
    }
}
