namespace CrestCreates.Agent.ControlPlane.Abstractions;

public enum ActivationRequestStatus
{
    Submitted,
    UnderReview,
    Approved,
    Activated,
    ActivationFailed,
    Rejected,
    Cancelled,
    Expired,
    Stale
}
