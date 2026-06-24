using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.DescriptorBinding;

public static class BindingStatusSynthesizer
{
    public static DescriptorBindingStatus SynthesizeStatus(IReadOnlyList<DescriptorBindingIssue> issues)
    {
        if (issues.Count == 0) return DescriptorBindingStatus.RuntimeReady;

        if (issues.Any(i => i.Severity == ValidationSeverity.Error && i.Code.StartsWith("REF_")))
            return DescriptorBindingStatus.Invalid;

        if (issues.Any(i => i.Severity == ValidationSeverity.Error && i.Code.StartsWith("BIND_")))
            return DescriptorBindingStatus.Unbound;

        if (issues.Any(i => i.Severity == ValidationSeverity.Error && i.Code.StartsWith("UNSUPPORTED_")))
            return DescriptorBindingStatus.Unsupported;

        return DescriptorBindingStatus.PartiallyBound; // warnings only
    }
}
