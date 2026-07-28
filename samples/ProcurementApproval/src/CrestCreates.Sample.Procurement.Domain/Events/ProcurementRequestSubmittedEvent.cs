using CrestCreates.Domain.DomainEvents;

namespace CrestCreates.Sample.Procurement.Domain.Events;

public sealed class ProcurementRequestSubmittedEvent : DomainEvent
{
    public Guid RequestId { get; }
    public string Title { get; }
    public decimal Amount { get; }
    public string Currency { get; }
    public string RequesterId { get; }
    public string Category { get; }
    public ProcurementRequestStatus InitialStatus { get; }

    public ProcurementRequestSubmittedEvent(
        Guid requestId,
        string title,
        decimal amount,
        string currency,
        string requesterId,
        string category,
        ProcurementRequestStatus initialStatus)
    {
        RequestId = requestId;
        Title = title;
        Amount = amount;
        Currency = currency;
        RequesterId = requesterId;
        Category = category;
        InitialStatus = initialStatus;
    }
}
