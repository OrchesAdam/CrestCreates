namespace CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

public enum DescriptorLifecycleOperation
{
    ValidateDraft,
    SubmitForReview,
    Approve,
    Activate,
    Deprecate,
    Retire,
    Reject
}
