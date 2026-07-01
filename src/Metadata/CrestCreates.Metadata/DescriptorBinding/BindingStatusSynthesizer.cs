using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.DescriptorBinding;

public static class BindingStatusSynthesizer
{
    public static DescriptorBindingStatus SynthesizeStatus(IReadOnlyList<DescriptorBindingIssue> issues)
    {
        if (issues.Count == 0) return DescriptorBindingStatus.RuntimeReady;

        // Fail-closed: any Error-severity issue with an unknown/empty code is classified as Invalid.
        // DiagnosticCode.Value is null only for default(DiagnosticCode) — a struct with no explicit value.
        // Such issues must not silently fall through to PartiallyBound.
        if (issues.Any(i => i.Severity == SeverityLevel.Error && i.Code.IsEmpty))
            return DescriptorBindingStatus.Invalid;

        if (issues.Any(i => i.Severity == SeverityLevel.Error && i.Code.RequireValue().StartsWith("REF_")))
            return DescriptorBindingStatus.Invalid;

        if (issues.Any(i => i.Severity == SeverityLevel.Error && i.Code.RequireValue().StartsWith("BIND_")))
            return DescriptorBindingStatus.Unbound;

        if (issues.Any(i => i.Severity == SeverityLevel.Error && i.Code.RequireValue().StartsWith("UNSUPPORTED_")))
            return DescriptorBindingStatus.Unsupported;

        return DescriptorBindingStatus.PartiallyBound; // warnings only
    }
}
