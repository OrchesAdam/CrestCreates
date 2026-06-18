namespace CrestCreates.Agent.ControlPlane.Abstractions;

public enum ActivationRequestStatus
{
    Submitted,
    UnderReview,
    Approved,
    Rejected,
    Cancelled,
    Expired
}
