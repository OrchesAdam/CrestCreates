using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.Samples.DescriptorControlPlane;

public sealed class CompanyCertificationSubmittedEvent : ILocalEvent
{
    public Guid CertificationId { get; init; }
    public string CompanyName { get; init; } = string.Empty;
}

public sealed class CompanyCertificationApprovedEvent : ILocalEvent
{
    public Guid CertificationId { get; init; }
    public string ApprovedBy { get; init; } = string.Empty;
}

public sealed class CompanyCertificationRejectedEvent : ILocalEvent
{
    public Guid CertificationId { get; init; }
    public string RejectedBy { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}
