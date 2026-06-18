using CrestCreates.Metadata.Abstractions.DescriptorImpact;

namespace CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

public sealed record DescriptorLifecycleGovernanceOptions
{
    // Validation
    public bool TreatValidationWarningsAsReviewRequired { get; init; } = false;

    // Binding — SubmitForReview
    public bool BlockSubmitForReviewOnUnboundBinding { get; init; } = false;
    public bool BlockSubmitForReviewOnUnsupportedBinding { get; init; } = false;
    public bool TreatSubmitForReviewUnsupportedBindingAsReviewRequired { get; init; } = true;
    public bool TreatSubmitForReviewPartialBindingAsReviewRequired { get; init; } = true;

    // Binding — Activate
    public bool BlockActivateOnUnboundBinding { get; init; } = true;
    public bool BlockActivateOnUnsupportedBinding { get; init; } = true;
    public bool TreatBindingPartialAsReviewRequired { get; init; } = false;

    // Compatibility
    public bool TreatBreakingCompatibilityAsReviewRequired { get; init; } = true;
    public bool TreatSecuritySensitiveAsReviewRequired { get; init; } = true;
    public bool TreatRiskyCompatibilityAsReviewRequired { get; init; } = true;
    public bool TreatCompatibilityUnsupportedAsReviewRequired { get; init; } = true;
    public bool BlockActivateOnBreakingCompatibility { get; init; } = false;

    // Impact
    public DescriptorImpactSeverity ReviewRequiredImpactThreshold { get; init; }
        = DescriptorImpactSeverity.Critical;

    // Diagnostics
    public bool BlockOnTopologyErrors { get; init; } = true;
    public bool BlockOnImpactDiagnosticsErrors { get; init; } = true;
    public bool BlockOnCompatibilityDiagnosticsErrors { get; init; } = true;
}
